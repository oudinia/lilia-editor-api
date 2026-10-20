using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Lilia.Api.Services;

/// <summary>A person's email and name as Stytch holds them, and whether that email is verified.</summary>
public sealed record StytchUserInfo(string? Email, string? Name, bool EmailVerified = false);

/// <summary>Asks Stytch who a user is, when a sign-in token does not say.</summary>
public interface IStytchUserLookup
{
    /// <summary>The user's email and name, or null when Stytch can't be asked (no secret) or doesn't answer.</summary>
    Task<StytchUserInfo?> FindAsync(string userId, CancellationToken ct = default);

    /// <summary>Whether Stytch can be asked at all (a project and its secret are set).</summary>
    bool CanAsk => true;
}

/// <summary>
/// Stytch's session tokens carry no email by default, so the first Google sign-up (20 Oct) was
/// stored with a stand-in address (<c>&lt;id&gt;@auth.liliaeditor.com</c>). With <c>Stytch:Secret</c>
/// set, Lilia reads the email and name from Stytch's server API (GET /v1/users/{id}). Without it,
/// nothing changes. Test projects (<c>project-test-…</c>) are on test.stytch.com, live ones on
/// api.stytch.com, unless <c>Stytch:ApiBase</c> says otherwise.
/// </summary>
public sealed class StytchUserLookup(IConfiguration config, IHttpClientFactory http, ILogger<StytchUserLookup> logger) : IStytchUserLookup
{
    /// <summary>The address a user gets while their real one is unknown.</summary>
    public const string PlaceholderDomain = "@auth.liliaeditor.com";

    public static bool IsPlaceholder(string? email) =>
        email is not null && email.EndsWith(PlaceholderDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>Stytch's API for this project.</summary>
    public static string ApiBase(IConfiguration config) =>
        (config["Stytch:ApiBase"] is { Length: > 0 } b ? b
            : (config["Stytch:ProjectId"] ?? "").StartsWith("project-test-", StringComparison.Ordinal)
                ? "https://test.stytch.com" : "https://api.stytch.com").TrimEnd('/');

    public bool CanAsk => !string.IsNullOrEmpty(config["Stytch:ProjectId"]) && !string.IsNullOrEmpty(config["Stytch:Secret"]);

    public async Task<StytchUserInfo?> FindAsync(string userId, CancellationToken ct = default)
    {
        var project = config["Stytch:ProjectId"];
        var secret = config["Stytch:Secret"];
        if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(secret) || !userId.StartsWith("user-", StringComparison.Ordinal))
            return null;
        try
        {
            using var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase(config)}/v1/users/{Uri.EscapeDataString(userId)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{project}:{secret}")));
            using var res = await client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("[Stytch] user lookup for {UserId}: {Status}", userId, (int)res.StatusCode);
                return null;
            }
            return Parse(await res.Content.ReadAsStringAsync(ct));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(e, "[Stytch] user lookup for {UserId} failed", userId);
            return null;
        }
    }

    /// <summary>
    /// Reads Stytch's Get User answer: the fields at the top level, or under <c>user</c>. The first
    /// verified email, else the first; the name from first, middle and last, else the name typed at
    /// sign-up (<c>display_name</c> in the trusted or untrusted metadata).
    /// </summary>
    public static StytchUserInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var user = root.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u : root;
        string? email = null;
        var verified = false;
        if (user.TryGetProperty("emails", out var emails) && emails.ValueKind == JsonValueKind.Array)
        {
            var all = emails.EnumerateArray()
                .Select(e => (Email: e.TryGetProperty("email", out var a) ? a.GetString() : null,
                              Verified: e.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True))
                .Where(e => !string.IsNullOrWhiteSpace(e.Email)).ToList();
            var chosen = all.FirstOrDefault(e => e.Verified);
            if (chosen.Email is null) chosen = all.FirstOrDefault();
            (email, verified) = (chosen.Email, chosen.Verified);
        }
        string? name = null;
        if (user.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.Object)
        {
            var parts = new[] { "first_name", "middle_name", "last_name" }
                .Select(k => n.TryGetProperty(k, out var p) ? p.GetString() : null)
                .Where(p => !string.IsNullOrWhiteSpace(p));
            name = string.Join(" ", parts) is { Length: > 0 } full ? full : null;
        }
        name ??= Metadata(user, "display_name");
        return email is null && name is null ? null : new StytchUserInfo(email, name, verified);
    }

    /// <summary>A string the sign-up page stored in the user's trusted, else untrusted, metadata.</summary>
    public static string? Metadata(JsonElement user, string key)
    {
        foreach (var bag in new[] { "trusted_metadata", "untrusted_metadata" })
            if (user.TryGetProperty(bag, out var m) && m.ValueKind == JsonValueKind.Object
                && m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(v.GetString()))
                return v.GetString()!.Trim();
        return null;
    }
}
