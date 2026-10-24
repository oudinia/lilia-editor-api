using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Lilia.Api.Services;

public record SignInSessionDto(string Id, DateTime? StartedAt, DateTime? LastActiveAt, string? Device, string? Ip, bool Current);
public record SecurityStatusDto(bool TwoStepEnabled, bool SessionsAvailable);

/// <summary>
/// Two-step sign-in and signed-in sessions (Settings → Security, launch redesign 3b). The authenticator
/// itself is Stytch's (enrolled and checked in the browser); the API remembers who turned it on and,
/// from then on, accepts only sessions that used a second factor, so a password alone no longer
/// reaches the account. Sessions are listed and signed out through Stytch's server API, which needs
/// <c>Stytch:Secret</c>; without it the section isn't offered.
/// </summary>
public class AccountSecurityService
{
    public static readonly TimeSpan FreshFactor = TimeSpan.FromMinutes(10);
    private static readonly string[] SecondFactors = ["totp", "recovery_codes", "recovery_code"];

    private readonly LiliaDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory? _http;
    private readonly ILogger<AccountSecurityService> _logger;
    private readonly TwoStepService? _twoStep;

    public AccountSecurityService(LiliaDbContext db, IMemoryCache cache, IConfiguration config, ILogger<AccountSecurityService> logger,
        IHttpClientFactory? http = null, TwoStepService? twoStep = null)
    {
        _db = db; _cache = cache; _config = config; _logger = logger; _http = http; _twoStep = twoStep;
    }

    /// <summary>
    /// When this session last passed the second step: an authenticator or recovery code (Stytch's
    /// session), or an email code (Lilia's, since 25 Oct). Null if it never did.
    /// </summary>
    public async Task<DateTime?> SecondStepAtAsync(ClaimsPrincipal user, string userId, DateTime now, CancellationToken ct = default)
    {
        var stytch = SecondFactorAt(user);
        var email = _twoStep is null ? null : await _twoStep.PassedAtAsync(userId, SessionId(user), now, ct);
        return stytch is null ? email : email is null ? stytch : (stytch > email ? stytch : email);
    }

    /// <summary>Whether this request has passed the second step: a code in this session, or a trusted device.</summary>
    public async Task<bool> PassedAsync(ClaimsPrincipal user, string userId, string? deviceToken, DateTime now, CancellationToken ct = default) =>
        await SecondStepAtAsync(user, userId, now, ct) is not null
        || (_twoStep is not null && await _twoStep.IsTrustedAsync(userId, deviceToken, now, ct));

    /// <summary>A second step in the last 10 minutes: what turning two-step on, or trusting a device, needs.</summary>
    public async Task<bool> FreshSecondStepAsync(ClaimsPrincipal user, string userId, DateTime now, CancellationToken ct = default) =>
        await SecondStepAtAsync(user, userId, now, ct) is { } at && now - at <= FreshFactor;

    public static string CacheKey(string userId) => $"two-step:{userId}";

    /// <summary>Stytch's session claim, parsed; null when the token has none (development identities, apps).</summary>
    public static JsonElement? Session(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("https://stytch.com/session")?.Value;
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonDocument.Parse(raw).RootElement.Clone(); } catch (JsonException) { return null; }
    }

    /// <summary>When this session last used a second factor (an authenticator or a recovery code), if ever.</summary>
    public static DateTime? SecondFactorAt(ClaimsPrincipal user)
    {
        if (Session(user) is not { } s || !s.TryGetProperty("authentication_factors", out var factors)) return null;
        DateTime? latest = null;
        foreach (var f in factors.EnumerateArray())
        {
            var type = f.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is null || !SecondFactors.Contains(type, StringComparer.OrdinalIgnoreCase)) continue;
            if (f.TryGetProperty("last_authenticated_at", out var at) && at.TryGetDateTime(out var when))
                latest = latest is null || when > latest ? when.ToUniversalTime() : latest;
        }
        return latest;
    }

    public static string? SessionId(ClaimsPrincipal user) =>
        Session(user) is { } s && s.TryGetProperty("id", out var id) ? id.GetString() : null;

    public async Task<bool> IsEnabledAsync(string userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey(userId), out bool on)) return on;
        on = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.TwoStepEnabled, ct);
        _cache.Set(CacheKey(userId), on, TimeSpan.FromSeconds(30));
        return on;
    }

    public bool SessionsAvailable => !string.IsNullOrEmpty(_config["Stytch:Secret"]) && !string.IsNullOrEmpty(_config["Stytch:ProjectId"]) && _http is not null;

    public async Task<SecurityStatusDto> StatusAsync(string userId, CancellationToken ct = default) =>
        new(await IsEnabledAsync(userId, ct), SessionsAvailable);

    /// <summary>
    /// Turn it on: only from a session that has just passed a second step (the authenticator's enrolment,
    /// or, since 25 Oct, an email code). Off: from a session that passed one; it also forgets every
    /// trusted device.
    /// </summary>
    public async Task<bool> SetAsync(string userId, ClaimsPrincipal user, bool enabled, DateTime now, CancellationToken ct = default)
    {
        var at = await SecondStepAtAsync(user, userId, now, ct);
        if (at is null || (enabled && now - at > FreshFactor)) return false;
        var row = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (row is null) return false;
        row.TwoStepEnabled = enabled;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _cache.Remove(CacheKey(userId));
        if (!enabled && _twoStep is not null) await _twoStep.ForgetAsync(userId, null, null, now, ct);
        _logger.LogInformation("[Security] {UserId} turned two-step sign-in {State}", userId, enabled ? "on" : "off");
        return true;
    }

    // ── Sessions, through Stytch's server API ───────────────────────────────

    private HttpClient Client()
    {
        var c = _http!.CreateClient();
        c.BaseAddress = new Uri((_config["Stytch:ApiBase"] ?? "https://api.stytch.com").TrimEnd('/') + "/");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config["Stytch:ProjectId"]}:{_config["Stytch:Secret"]}")));
        return c;
    }

    public async Task<IReadOnlyList<SignInSessionDto>?> SessionsAsync(string userId, string? currentSessionId, CancellationToken ct = default)
    {
        if (!SessionsAvailable) return null;
        using var res = await Client().GetAsync($"v1/sessions?user_id={Uri.EscapeDataString(userId)}", ct);
        if (!res.IsSuccessStatusCode) { _logger.LogWarning("[Security] Stytch sessions for {UserId}: {Status}", userId, res.StatusCode); return null; }
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var list = new List<SignInSessionDto>();
        if (doc.RootElement.TryGetProperty("sessions", out var sessions))
            foreach (var s in sessions.EnumerateArray())
            {
                var id = s.GetProperty("session_id").GetString()!;
                string? ua = null, ip = null;
                if (s.TryGetProperty("attributes", out var a))
                {
                    ua = a.TryGetProperty("user_agent", out var u) ? u.GetString() : null;
                    ip = a.TryGetProperty("ip_address", out var i) ? i.GetString() : null;
                }
                list.Add(new SignInSessionDto(id, Date(s, "started_at"), Date(s, "last_accessed_at"), ua, ip, id == currentSessionId));
            }
        return list.OrderByDescending(s => s.Current).ThenByDescending(s => s.LastActiveAt).ToList();
    }

    /// <summary>Sign out one session, or every one but this. Only the author's own sessions.</summary>
    public async Task<int> RevokeAsync(string userId, string? currentSessionId, string? only, CancellationToken ct = default)
    {
        var mine = await SessionsAsync(userId, currentSessionId, ct);
        if (mine is null) return 0;
        var targets = mine.Where(s => only is null ? !s.Current : s.Id == only).ToList();
        var client = Client();
        var n = 0;
        foreach (var s in targets)
        {
            using var res = await client.PostAsJsonAsync("v1/sessions/revoke", new { session_id = s.Id }, ct);
            if (res.IsSuccessStatusCode) n++;
            else _logger.LogWarning("[Security] revoke {SessionId}: {Status}", s.Id, res.StatusCode);
        }
        return n;
    }

    private static DateTime? Date(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTime(out var d) ? d.ToUniversalTime() : null;
}

/// <summary>
/// Two-step sign-in, enforced: once an author turns it on, a session that hasn't passed the second step
/// gets 403 TWO_STEP_REQUIRED (the editor then asks for a code). It passes with an authenticator or
/// recovery code, an email code (25 Oct), or a trusted device's cookie (25 Oct). The two email-code
/// routes stay open so the code can be asked for. Apps connected over the connector are let through:
/// their consent was given from a signed-in browser. Tokens without Stytch's session (development
/// identities on a test stack) are let through too.
/// </summary>
public class TwoStepMiddleware
{
    /// <summary>Reachable before the second step: asking for and entering an email code.</summary>
    public static readonly string[] OpenBeforeSecondStep = ["/api/me/two-step/email/send", "/api/me/two-step/email/verify"];

    private readonly RequestDelegate _next;
    public TwoStepMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AccountSecurityService security)
    {
        var user = context.User;
        var userId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var path = context.Request.Path.Value ?? "";
        if (!string.IsNullOrEmpty(userId)
            && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            && !OpenBeforeSecondStep.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase))
            && !Lilia.Api.Mcp.McpOAuth.IsConnectedAppToken(user)
            && AccountSecurityService.Session(user) is not null
            && await security.IsEnabledAsync(userId, context.RequestAborted)
            && !await security.PassedAsync(user, userId, context.Request.Cookies[TwoStepService.DeviceCookie], DateTime.UtcNow, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { code = "TWO_STEP_REQUIRED", message = "Enter a code from your authenticator app or your email to continue." });
            return;
        }
        await _next(context);
    }
}
