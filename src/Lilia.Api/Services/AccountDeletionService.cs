using System.Security.Claims;
using System.Text.Json;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Lilia.Api.Services;

public record AccountStatusDto(string Email, int Documents, int SharedDocuments, int People, DateTime? DeletionRequestedAt, DateTime? ErasedOn);

public enum DeletionRefusal { None, EmailMismatch, ReauthRequired, AlreadyRequested }

/// <summary>
/// Delete account (Settings → Your data, launch redesign 3g). Asking closes the account at once and
/// erases it after <see cref="RestoreDays"/> days unless the author restores it. Erasing deletes every
/// document they own and their shares, and anonymises the user row: comments others can still read
/// show "Deleted user", and the rows that point at the account stay valid. Nothing is erased on request.
/// </summary>
public class AccountDeletionService
{
    public const int RestoreDays = 30;
    /// <summary>Deleting needs a sign-in this recent (Stytch's session factors), so an open laptop isn't enough.</summary>
    public static readonly TimeSpan ReauthWindow = TimeSpan.FromMinutes(10);

    private readonly LiliaDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<AccountDeletionService> _logger;
    private readonly IEmailService? _email;
    private readonly IHttpClientFactory? _http;

    public AccountDeletionService(LiliaDbContext db, IMemoryCache cache, IConfiguration config, ILogger<AccountDeletionService> logger,
        IEmailService? email = null, IHttpClientFactory? http = null)
    {
        _db = db; _cache = cache; _config = config; _logger = logger; _email = email; _http = http;
    }

    public static string CacheKey(string userId) => $"account-closed:{userId}";

    public async Task<AccountStatusDto?> StatusAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;
        var owned = _db.Documents.IgnoreQueryFilters().Where(d => d.OwnerId == userId && !d.IsPlayground && !d.IsHelpContent);
        var shared = owned.Where(d => _db.DocumentCollaborators.Any(c => c.DocumentId == d.Id && c.UserId != userId));
        var people = await _db.DocumentCollaborators.Where(c => c.UserId != userId && owned.Any(d => d.Id == c.DocumentId))
            .Select(c => c.UserId).Distinct().CountAsync(ct);
        return new AccountStatusDto(user.Email, await owned.CountAsync(ct), await shared.CountAsync(ct), people,
            user.DeletionRequestedAt, user.DeletionRequestedAt?.AddDays(RestoreDays));
    }

    /// <summary>Is the sign-in behind this request recent enough to delete the account?</summary>
    public static bool RecentlySignedIn(ClaimsPrincipal user, DateTime now, bool developmentIdentity)
    {
        var session = user.FindFirst("https://stytch.com/session")?.Value;
        if (string.IsNullOrEmpty(session)) return developmentIdentity;
        try
        {
            using var doc = JsonDocument.Parse(session);
            if (!doc.RootElement.TryGetProperty("authentication_factors", out var factors)) return false;
            foreach (var f in factors.EnumerateArray())
                if (f.TryGetProperty("last_authenticated_at", out var at) && at.TryGetDateTime(out var when)
                    && now - when.ToUniversalTime() <= ReauthWindow)
                    return true;
        }
        catch (JsonException) { }
        return false;
    }

    public async Task<(DeletionRefusal Refusal, DateTime? ErasedOn)> RequestAsync(string userId, string? confirmEmail, bool recentlySignedIn, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return (DeletionRefusal.EmailMismatch, null);
        if (user.DeletionRequestedAt is not null) return (DeletionRefusal.AlreadyRequested, user.DeletionRequestedAt.Value.AddDays(RestoreDays));
        if (!string.Equals(confirmEmail?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase)) return (DeletionRefusal.EmailMismatch, null);
        if (!recentlySignedIn) return (DeletionRefusal.ReauthRequired, null);

        user.DeletionRequestedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _cache.Remove(CacheKey(userId));
        // What was cached for them (PDFs, bibliographies, checks; 18 Oct) goes with the account.
        var purged = await UserCache.PurgeUserAsync(userId);
        _logger.LogInformation("[AccountDeletion] {UserId}: {Count} cached entries removed", userId, purged);
        var erasedOn = user.DeletionRequestedAt.Value.AddDays(RestoreDays);
        _logger.LogInformation("[AccountDeletion] {UserId} asked to delete; erasure on {Date:yyyy-MM-dd}", userId, erasedOn);
        await SendAsync(e => e.SendAccountClosedAsync(user.Email, user.Locale, user.DeletionRequestedAt.Value, erasedOn), user.Email);
        return (DeletionRefusal.None, erasedOn);
    }

    public async Task<bool> RestoreAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.DeletionRequestedAt is null) return false;
        user.DeletionRequestedAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _cache.Remove(CacheKey(userId));
        _logger.LogInformation("[AccountDeletion] {UserId} restored their account", userId);
        return true;
    }

    /// <summary>True while the account is closed (asked to delete, or erased). Cached briefly per user.</summary>
    public async Task<bool> IsClosedAsync(string userId, CancellationToken ct = default) =>
        await StateAsync(userId, ct) is AccountState.Closed;

    /// <summary>
    /// Open, closed (asked to delete, or erased) or suspended by the operator from the admin console
    /// (17 Oct: Banned, until BanExpires if set). Cached 30 s per user, so a suspension applies
    /// within 30 seconds.
    /// </summary>
    public async Task<AccountState> StateAsync(string userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(userId), out AccountState state)) return state;
        var now = DateTime.UtcNow;
        var u = await _db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.DeletionRequestedAt, x.Banned, x.BanReason, x.BanExpires }).FirstOrDefaultAsync(ct);
        state = u is null ? AccountState.Open
            : u.DeletionRequestedAt != null || u.BanReason == ErasedReason ? AccountState.Closed
            : u.Banned && (u.BanExpires == null || u.BanExpires > now) ? AccountState.Suspended
            : AccountState.Open;
        _cache.Set(CacheKey(userId), state, TimeSpan.FromSeconds(30));
        return state;
    }

    public const string ErasedReason = "erased";

    /// <summary>Erase every account whose restore window has passed. Returns how many.</summary>
    public async Task<int> EraseDueAsync(DateTime now, CancellationToken ct = default)
    {
        var cutoff = now.AddDays(-RestoreDays);
        var due = await _db.Users.Where(u => u.DeletionRequestedAt != null && u.DeletionRequestedAt <= cutoff).Select(u => u.Id).ToListAsync(ct);
        foreach (var id in due) await EraseAsync(id, ct);
        return due.Count;
    }

    private async Task EraseAsync(string userId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var docs = await _db.Documents.IgnoreQueryFilters().Where(d => d.OwnerId == userId).ToListAsync(ct);
        _db.Documents.RemoveRange(docs);
        _db.DocumentCollaborators.RemoveRange(_db.DocumentCollaborators.Where(c => c.UserId == userId));
        _db.UserPreferences.RemoveRange(_db.UserPreferences.Where(p => p.UserId == userId));
        var user = await _db.Users.FirstAsync(u => u.Id == userId, ct);
        var email = user.Email;
        var locale = user.Locale;
        user.Email = $"erased-{Guid.NewGuid():N}@erased.invalid";
        user.Name = "Deleted user";
        user.Image = null;
        user.Username = null;
        user.DisplayUsername = null;
        user.Banned = true;
        user.BanReason = ErasedReason;
        user.DeletionRequestedAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await UserCache.PurgeUserAsync(userId);
        _cache.Remove(CacheKey(userId));

        var exports = Path.GetFullPath(Path.Combine(_config["AccountExport:Path"] ?? "./account-exports", userId));
        try { if (Directory.Exists(exports)) Directory.Delete(exports, recursive: true); } catch (IOException) { }
        await DeleteFromStytchAsync(userId);
        _logger.LogInformation("[AccountDeletion] {UserId} erased: {Count} documents", userId, docs.Count);
        await SendAsync(e => e.SendAccountErasedAsync(email, locale, DateTime.UtcNow), email);
    }

    /// <summary>The sign-in identity goes too, when the Stytch admin credentials are configured.</summary>
    private async Task DeleteFromStytchAsync(string userId)
    {
        var project = _config["Stytch:ProjectId"]; var secret = _config["Stytch:Secret"];
        if (_http is null || string.IsNullOrEmpty(project) || string.IsNullOrEmpty(secret) || !userId.StartsWith("user-", StringComparison.Ordinal)) return;
        try
        {
            using var client = _http.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Delete, $"{StytchUserLookup.ApiBase(_config)}/v1/users/{Uri.EscapeDataString(userId)}");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{project}:{secret}")));
            var res = await client.SendAsync(req);
            if (!res.IsSuccessStatusCode) _logger.LogWarning("[AccountDeletion] Stytch delete for {UserId}: {Status}", userId, res.StatusCode);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "[AccountDeletion] Stytch delete for {UserId} failed", userId); }
    }

    private async Task SendAsync(Func<IEmailService, Task> send, string to)
    {
        if (_email is null || string.IsNullOrEmpty(to)) return;
        try { await send(_email); }
        catch (Exception ex) { _logger.LogWarning(ex, "[AccountDeletion] email not sent"); }
    }
}

/// <summary>A closed account reaches only its status and Restore (403 ACCOUNT_CLOSED elsewhere).</summary>
public enum AccountState { Open, Closed, Suspended }

public class AccountClosedMiddleware
{
    private static readonly string[] Open = ["/api/me/account", "/api/me/restore"];
    private readonly RequestDelegate _next;
    public AccountClosedMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AccountDeletionService accounts)
    {
        var userId = context.User.FindFirst("sub")?.Value ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var path = context.Request.Path.Value ?? "";
        if (!string.IsNullOrEmpty(userId) && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            var state = await accounts.StateAsync(userId, context.RequestAborted);
            if (state == AccountState.Closed && !Open.Any(o => path.Equals(o, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { code = "ACCOUNT_CLOSED", message = "This account is closed. Restore it from Settings to use it again." });
                return;
            }
            if (state == AccountState.Suspended)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { code = "ACCOUNT_SUSPENDED", message = "This account is suspended. Write to support to have it restored." });
                return;
            }
        }
        await _next(context);
    }
}

/// <summary>Once a day, erase the accounts whose 30 days to restore have passed.</summary>
public class AccountErasureBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AccountErasureBackgroundService> _logger;
    public AccountErasureBackgroundService(IServiceScopeFactory scopes, ILogger<AccountErasureBackgroundService> logger) { _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<AccountDeletionService>().EraseDueAsync(DateTime.UtcNow, stoppingToken);
                if (n > 0) _logger.LogInformation("[AccountErasure] Erased {Count} accounts", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "[AccountErasure] failed"); }
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
