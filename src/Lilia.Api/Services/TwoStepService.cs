using System.Security.Cryptography;
using System.Text;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace Lilia.Api.Services;

public record TrustedDeviceDto(Guid Id, string Label, DateTime CreatedAt, DateTime LastUsedAt, DateTime ExpiresAt, bool Current);

public enum CodeCheck { Ok, Wrong, Expired, TooMany, NoSession }

/// <summary>
/// Two-step by email, and trusted devices (25 Oct 2026). The owner, 24 Oct: "the login cannot be just
/// through the authenticator app, the choice of receiving code by email is important. Also, if the user
/// trusts his laptop or tablet, he should be able to only have to login."
/// <list type="bullet">
/// <item>An email code is six digits, valid 10 minutes, 5 tries, its own sending limit (one every 30 s,
/// six an hour) so it never waits behind a sign-in link. Only hashes are stored.</item>
/// <item>A passed code is remembered for the Stytch session (Stytch records authenticator codes in the
/// session; an email code is Lilia's).</item>
/// <item>A trusted device is a random token in an HttpOnly cookie, 30 days; only its SHA-256 is stored.
/// A session that brings one passes the second step.</item>
/// </list>
/// </summary>
public class TwoStepService
{
    public const string DeviceCookie = "lilia_trusted_device";
    public static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan TrustLife = TimeSpan.FromDays(30);
    public static readonly TimeSpan PassLife = TimeSpan.FromDays(7);
    public const int MaxAttempts = 5;
    private static readonly TimeSpan Cached = TimeSpan.FromSeconds(60);

    private readonly LiliaDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IDistributedCache? _limits;
    private readonly IEmailService _email;
    private readonly ILogger<TwoStepService> _logger;

    public TwoStepService(LiliaDbContext db, IMemoryCache cache, IEmailService email, ILogger<TwoStepService> logger, IDistributedCache? limits = null)
    {
        _db = db; _cache = cache; _email = email; _logger = logger; _limits = limits;
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string CodeHash(string userId, string code) => Hash($"{userId}:{code}");

    /// <summary>"oussama@gmail.com" → "o•••@gmail.com": enough to recognise, not to read.</summary>
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}•••{email[at..]}";
    }

    /// <summary>"Chrome on Windows" from a user agent; "This browser" when it can't tell.</summary>
    public static string DeviceLabel(string? ua)
    {
        if (string.IsNullOrEmpty(ua)) return "This browser";
        var browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Chrome/") ? "Chrome"
            : ua.Contains("Safari/") ? "Safari" : "A browser";
        var os = ua.Contains("Windows") ? "Windows" : ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad"
            : ua.Contains("Android") ? "Android" : ua.Contains("Mac OS X") ? "Mac" : ua.Contains("Linux") ? "Linux" : null;
        return os is null ? browser : $"{browser} on {os}";
    }

    // ── Email codes ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a code to the account's own address. Returns the masked address, or null when the account
    /// has none. Past the sending limit nothing is sent and the answer is the same.
    /// </summary>
    public async Task<string?> SendCodeAsync(string userId, string? locale, DateTime now, CancellationToken ct = default)
    {
        var email = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(email) || email.EndsWith("@auth.liliaeditor.com", StringComparison.OrdinalIgnoreCase)) return null;
        if (!await TakeSendAsync(userId, ct))
        {
            _logger.LogWarning("[TwoStep] code for {UserId} held back: over the sending limit", userId);
            return Mask(email);
        }
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        // A newer code replaces the older ones.
        await _db.TwoStepCodes.Where(c => c.UserId == userId && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), ct);
        _db.TwoStepCodes.Add(new TwoStepCode { UserId = userId, CodeHash = CodeHash(userId, code), ExpiresAt = now + CodeLife, CreatedAt = now });
        await _db.SaveChangesAsync(ct);
        await _email.SendTwoStepCodeAsync(email, code, locale);
        _logger.LogInformation("[TwoStep] code sent to {UserId}", userId);
        return Mask(email);
    }

    private async Task<bool> TakeSendAsync(string userId, CancellationToken ct)
    {
        if (_limits is null) return true;
        var soon = $"twostep:send:soon:{userId}";
        var hour = $"twostep:send:hour:{userId}";
        if (await _limits.GetAsync(soon, ct) is not null) return false;
        var sent = int.TryParse(await _limits.GetStringAsync(hour, ct), out var n) ? n : 0;
        if (sent >= 6) return false;
        await _limits.SetAsync(soon, [1], new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, ct);
        await _limits.SetStringAsync(hour, (sent + 1).ToString(), new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) }, ct);
        return true;
    }

    /// <summary>Checks a code; on success remembers this session as having passed the second step.</summary>
    public async Task<CodeCheck> VerifyCodeAsync(string userId, string? sessionId, string code, DateTime now, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId)) return CodeCheck.NoSession;
        var row = await _db.TwoStepCodes.Where(c => c.UserId == userId && c.UsedAt == null)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        if (row is null || row.ExpiresAt <= now) return CodeCheck.Expired;
        if (row.Attempts >= MaxAttempts) return CodeCheck.TooMany;
        row.Attempts++;
        var given = CodeHash(userId, (code ?? "").Trim());
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given), Encoding.ASCII.GetBytes(row.CodeHash));
        if (!ok)
        {
            await _db.SaveChangesAsync(ct);
            return row.Attempts >= MaxAttempts ? CodeCheck.TooMany : CodeCheck.Wrong;
        }
        row.UsedAt = now;
        var pass = await _db.TwoStepSessionPasses.FirstOrDefaultAsync(p => p.SessionId == sessionId, ct);
        if (pass is null) _db.TwoStepSessionPasses.Add(new TwoStepSessionPass { SessionId = sessionId, UserId = userId, CreatedAt = now, ExpiresAt = now + PassLife });
        else if (pass.UserId == userId) { pass.CreatedAt = now; pass.ExpiresAt = now + PassLife; }
        else return CodeCheck.NoSession;
        await _db.SaveChangesAsync(ct);
        _cache.Remove(PassKey(sessionId));
        _logger.LogInformation("[TwoStep] {UserId} passed with an email code", userId);
        return CodeCheck.Ok;
    }

    private static string PassKey(string sessionId) => $"two-step-pass:{sessionId}";

    /// <summary>When this session passed the second step with an email code, if it did (cached a minute).</summary>
    public async Task<DateTime?> PassedAtAsync(string userId, string? sessionId, DateTime now, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        if (!_cache.TryGetValue(PassKey(sessionId), out DateTime? at))
        {
            at = await _db.TwoStepSessionPasses.AsNoTracking()
                .Where(p => p.SessionId == sessionId && p.UserId == userId && p.ExpiresAt > now)
                .Select(p => (DateTime?)p.CreatedAt).FirstOrDefaultAsync(ct);
            _cache.Set(PassKey(sessionId), at, Cached);
        }
        return at;
    }

    // ── Trusted devices ─────────────────────────────────────────────────────

    /// <summary>Trusts this browser: returns the token for the cookie (never stored, only its hash).</summary>
    public async Task<string> TrustAsync(string userId, string? userAgent, DateTime now, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _db.TrustedDevices.Add(new TrustedDevice
        {
            UserId = userId, TokenHash = Hash(token), Label = DeviceLabel(userAgent),
            CreatedAt = now, LastUsedAt = now, ExpiresAt = now + TrustLife,
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[TwoStep] {UserId} trusted a device ({Label})", userId, DeviceLabel(userAgent));
        return token;
    }

    private static string DeviceKey(string hash) => $"trusted-device:{hash}";

    /// <summary>Whether this cookie is one of the account's trusted devices, still valid (cached a minute).</summary>
    public async Task<bool> IsTrustedAsync(string userId, string? token, DateTime now, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var hash = Hash(token);
        if (!_cache.TryGetValue(DeviceKey(hash), out (string User, Guid Id)? hit))
        {
            var row = await _db.TrustedDevices.AsNoTracking()
                .Where(d => d.TokenHash == hash && d.RevokedAt == null && d.ExpiresAt > now)
                .Select(d => new { d.UserId, d.Id, d.LastUsedAt }).FirstOrDefaultAsync(ct);
            hit = row is null ? null : (row.UserId, row.Id);
            _cache.Set(DeviceKey(hash), hit, Cached);
            if (row is not null && now - row.LastUsedAt > TimeSpan.FromHours(1))
                await _db.TrustedDevices.Where(d => d.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.LastUsedAt, now), ct);
        }
        return hit is { } h && h.User == userId;
    }

    public async Task<IReadOnlyList<TrustedDeviceDto>> DevicesAsync(string userId, string? token, DateTime now, CancellationToken ct = default)
    {
        var mine = string.IsNullOrEmpty(token) ? null : Hash(token);
        var rows = await _db.TrustedDevices.AsNoTracking()
            .Where(d => d.UserId == userId && d.RevokedAt == null && d.ExpiresAt > now)
            .OrderByDescending(d => d.LastUsedAt).ToListAsync(ct);
        return rows.Select(d => new TrustedDeviceDto(d.Id, d.Label, d.CreatedAt, d.LastUsedAt, d.ExpiresAt, d.TokenHash == mine)).ToList();
    }

    /// <summary>Forgets one device, or all of them (optionally keeping this one). Returns how many.</summary>
    public async Task<int> ForgetAsync(string userId, Guid? only, string? keepToken, DateTime now, CancellationToken ct = default)
    {
        var keep = string.IsNullOrEmpty(keepToken) ? null : Hash(keepToken);
        var rows = await _db.TrustedDevices
            .Where(d => d.UserId == userId && d.RevokedAt == null && (only == null ? d.TokenHash != keep : d.Id == only))
            .ToListAsync(ct);
        foreach (var d in rows) { d.RevokedAt = now; _cache.Remove(DeviceKey(d.TokenHash)); }
        await _db.SaveChangesAsync(ct);
        if (rows.Count > 0) _logger.LogInformation("[TwoStep] {UserId} forgot {Count} trusted device(s)", userId, rows.Count);
        return rows.Count;
    }
}
