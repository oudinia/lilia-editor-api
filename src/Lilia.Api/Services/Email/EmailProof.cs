using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Distributed;

namespace Lilia.Api.Services.Email;

/// <summary>
/// A one-time code carried by every sign-in, reset and confirmation link Lilia emails: whoever brings it
/// back has opened that inbox, so the address is real (8 Nov 2026).
///
/// <para>Stytch can't say so. Lilia's emails carry embeddable magic links, and Stytch never marks an
/// address verified when one is used, since it can't know how the link travelled. The welcome waited for
/// Stytch's flag, so no email-and-password sign-up got one (the owner's own test, PROD-27).</para>
///
/// <para>Only the code's hash is kept, for as long as its link is valid; a code works once.</para>
/// </summary>
public static class EmailProof
{
    /// <summary>A new code for <paramref name="userId"/>, valid <paramref name="minutes"/>; null without a cache.</summary>
    public static async Task<string?> IssueAsync(IDistributedCache? cache, string userId, int minutes, CancellationToken ct = default)
    {
        if (cache is null) return null;
        var code = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await cache.SetAsync(Key(userId, code), [1], new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(minutes) }, ct);
        return code;
    }

    /// <summary>Whether <paramref name="code"/> is a live code of <paramref name="userId"/>'s; it is spent either way.</summary>
    public static async Task<bool> RedeemAsync(IDistributedCache? cache, string userId, string? code, CancellationToken ct = default)
    {
        if (cache is null || string.IsNullOrWhiteSpace(code) || code.Length > 100) return false;
        var key = Key(userId, code.Trim());
        if (await cache.GetAsync(key, ct) is null) return false;
        await cache.RemoveAsync(key, ct);
        return true;
    }

    /// <summary>The link's query part, <c>&amp;proof=…</c>, or nothing when no code was issued.</summary>
    public static string Query(string? code) => code is null ? "" : $"&proof={Uri.EscapeDataString(code)}";

    private static string Key(string userId, string code) =>
        $"emailproof:{userId}:{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)))}";

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
