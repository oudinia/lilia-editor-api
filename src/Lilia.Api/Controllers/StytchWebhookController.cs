using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Stytch event webhook receiver — listens for <c>direct.user.create</c>
/// (fires when our editor's SDK creates a user via passwords.create or
/// oauth.start). On each new user we:
///
///   1. Verify Stytch's email isn't already verified (social signups
///      arrive pre-verified; skip those).
///   2. Mint an embedded magic link via Stytch's admin API
///      (POST /v1/magic_links) — this returns a token, NO email sent.
///   3. Render our branded verification email using
///      <see cref="IEmailService.SendStytchVerificationAsync"/> and ship
///      it via Resend on the verified <c>liliaeditor.com</c> domain.
///
/// Why webhook-based: free tier doesn't expose email-send events, but
/// it does expose user-CRUD ones. By driving off
/// <c>direct.user.create</c> we don't need the $99 BYOM add-on AND we
/// stay decoupled from the frontend (mobile / admin signups
/// auto-trigger too).
///
/// Endpoint: <c>POST /api/webhooks/stytch/email</c>
///
/// Signature: Svix format (Stytch's Webhooks UI is Svix-powered). Three
/// headers — <c>svix-id</c>, <c>svix-timestamp</c>, <c>svix-signature</c>
/// — are HMAC-SHA-256 over <c>{id}.{timestamp}.{body}</c> using the
/// signing secret with the <c>whsec_</c> prefix stripped and the
/// remainder base64-decoded.
///
/// Retry contract: 200 = handled. 401 = signature fail (Svix will retry
/// a few times then back off). 4xx/5xx = body issue / send failure;
/// we still 200 after logging so Stytch doesn't flood retries — the
/// recovery path is the resend button on <c>/verify-email</c>.
/// </summary>
[ApiController]
[Route("api/webhooks/stytch")]
[AllowAnonymous]
public class StytchWebhookController : ControllerBase
{
    private readonly IEmailService _emails;
    private readonly StytchWebhookSettings _settings;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<StytchWebhookController> _logger;

    public StytchWebhookController(
        IEmailService emails,
        StytchWebhookSettings settings,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<StytchWebhookController> logger)
    {
        _emails = emails;
        _settings = settings;
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [HttpPost("email")]
    public async Task<IActionResult> Email(CancellationToken ct)
    {
        // Re-read the raw body — signature is over exact bytes, so
        // re-serialising through a model would change the hash.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        if (string.IsNullOrEmpty(rawBody))
            return BadRequest(new { error = "empty_body" });

        var svixId = Request.Headers["svix-id"].ToString();
        var svixTimestamp = Request.Headers["svix-timestamp"].ToString();
        var svixSignature = Request.Headers["svix-signature"].ToString();

        if (!SvixSignatureValidator.IsValid(
                svixId, svixTimestamp, rawBody, svixSignature, _settings.WebhookSecret, _settings.RequireSignature))
        {
            _logger.LogWarning(
                "Stytch webhook signature mismatch (id={Id}, ts={Ts}, sig_present={SigPresent})",
                svixId, svixTimestamp, !string.IsNullOrEmpty(svixSignature));
            return Unauthorized(new { error = "signature_mismatch" });
        }

        StytchEventEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<StytchEventEnvelope>(rawBody, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Stytch webhook: invalid JSON");
            return BadRequest(new { error = "invalid_json" });
        }

        if (envelope?.EventType is null)
            return BadRequest(new { error = "missing_event_type" });

        try
        {
            switch (envelope.EventType)
            {
                case "direct.user.create":
                    await HandleUserCreatedAsync(envelope, ct);
                    break;
                default:
                    _logger.LogDebug("Stytch webhook: ignoring event {EventType}", envelope.EventType);
                    break;
            }
            return Ok(new { handled = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stytch webhook: handler failed for {EventType}", envelope.EventType);
            // 200 to suppress Stytch retries — user can resend via UI.
            return Ok(new { handled = false, reason = "handler_failed" });
        }
    }

    private async Task HandleUserCreatedAsync(StytchEventEnvelope envelope, CancellationToken ct)
    {
        // Stytch's event envelope nests the user under .data.user. The
        // SDK signup path sets emails[0].verified=false; social signups
        // (Google/GitHub) arrive verified — skip those.
        var user = envelope.Data?.User;
        if (user is null)
        {
            _logger.LogWarning("Stytch webhook user.create: no .data.user in payload");
            return;
        }
        var emailEntry = user.Emails?.FirstOrDefault();
        if (emailEntry is null || string.IsNullOrEmpty(emailEntry.Email))
        {
            _logger.LogDebug("Stytch webhook user.create {UserId}: no email — skip", user.UserId);
            return;
        }
        if (emailEntry.Verified == true)
        {
            _logger.LogDebug("Stytch webhook user.create {UserId}: email already verified — skip", user.UserId);
            return;
        }

        var magicLinkUrl = await MintMagicLinkAsync(user.UserId!, ct);
        if (string.IsNullOrEmpty(magicLinkUrl))
        {
            _logger.LogWarning("Stytch webhook user.create {UserId}: could not mint magic link, no email sent", user.UserId);
            return;
        }

        var locale = ExtractLocale(user);
        await _emails.SendStytchVerificationAsync(emailEntry.Email!, magicLinkUrl, locale);
        _logger.LogInformation(
            "Stytch webhook user.create {UserId}: verification email queued for {Email} (locale={Locale})",
            user.UserId, emailEntry.Email, locale);
    }

    /// <summary>
    /// Mints an embedded magic link via Stytch's admin API. Returns the
    /// click-through URL or null if Stytch rejects. Uses
    /// <c>Stytch:ProjectId</c> + <c>Stytch:Secret</c> for Basic auth.
    /// </summary>
    private async Task<string?> MintMagicLinkAsync(string userId, CancellationToken ct)
    {
        var projectId = _config["Stytch:ProjectId"];
        var secret = _config["Stytch:Secret"];
        // Same default as Program.cs (line 143). For test projects DO
        // must set Stytch__ApiBase=https://test.stytch.com explicitly.
        var apiBase = _config["Stytch:ApiBase"] ?? "https://api.stytch.com";
        if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(secret))
        {
            _logger.LogError("Stytch admin credentials not configured (Stytch:ProjectId / Stytch:Secret)");
            return null;
        }

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(apiBase);
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{projectId}:{secret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        // POST /v1/magic_links creates an embedded magic-link token for
        // the user — no email is sent (we send our own). The endpoint
        // rejects session_duration_minutes (that belongs on the
        // authenticate call); use expiration_minutes for the click
        // window instead. 60 minutes is the Stytch default and matches
        // typical "verify your email within an hour" UX.
        var payload = new { user_id = userId, expiration_minutes = 60 };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await http.PostAsync("/v1/magic_links", content, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Stytch admin POST /v1/magic_links failed {Status}: {Body}",
                (int)res.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        // Stytch returns the token directly. We construct the URL that
        // routes back to the editor's /auth/callback so the SDK can
        // consume it via magicLinks.authenticate.
        if (!root.TryGetProperty("token", out var tokenEl))
        {
            _logger.LogWarning("Stytch admin POST /v1/magic_links: response missing 'token' — body: {Body}", body);
            return null;
        }
        var token = tokenEl.GetString();
        if (string.IsNullOrEmpty(token)) return null;

        // Email:BaseUrl is the editor origin (appsettings.json) — reuse
        // it instead of inventing a new config key.
        var editorOrigin = _config["Email:BaseUrl"] ?? "https://editor.liliaeditor.com";
        return $"{editorOrigin}/auth/callback?stytch_token_type=magic_links&token={Uri.EscapeDataString(token)}";
    }

    private static string? ExtractLocale(StytchEventUser user)
    {
        // Future: read locale from user.trusted_metadata.locale or the
        // request header captured during signup. For now we don't have
        // the locale in the payload, so let the EmailService fall back
        // to English.
        _ = user;
        return null;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}

// =====================================================================
//  Svix payload envelope. Stytch wraps every event in this shape.
// =====================================================================

public class StytchEventEnvelope
{
    [JsonPropertyName("event_type")] public string? EventType { get; set; }
    [JsonPropertyName("data")] public StytchEventData? Data { get; set; }
    [JsonPropertyName("event_id")] public string? EventId { get; set; }
    [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
}

public class StytchEventData
{
    [JsonPropertyName("user")] public StytchEventUser? User { get; set; }
    [JsonPropertyName("session")] public JsonElement? Session { get; set; }
}

public class StytchEventUser
{
    [JsonPropertyName("user_id")] public string? UserId { get; set; }
    [JsonPropertyName("emails")] public List<StytchEventEmail>? Emails { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("trusted_metadata")] public JsonElement? TrustedMetadata { get; set; }
}

public class StytchEventEmail
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("email_id")] public string? EmailId { get; set; }
    [JsonPropertyName("verified")] public bool? Verified { get; set; }
}

public class StytchWebhookSettings
{
    public string WebhookSecret { get; set; } = "";
    public bool RequireSignature { get; set; } = true;
}

// =====================================================================
//  Svix HMAC SHA-256 signature validation. Algorithm:
//    1. Strip "whsec_" prefix and base64-decode secret → raw key.
//    2. signedPayload = $"{svix-id}.{svix-timestamp}.{body}".
//    3. expected = base64(HMAC-SHA-256(rawKey, signedPayload)).
//    4. svix-signature header is space-separated "v1,<base64>" entries
//       (multiple in case of secret rotation). Match any one.
// =====================================================================

public static class SvixSignatureValidator
{
    public static bool IsValid(string svixId, string svixTimestamp, string body, string signatureHeader, string secret, bool require)
    {
        if (string.IsNullOrEmpty(secret))
            return !require;
        if (string.IsNullOrEmpty(svixId) || string.IsNullOrEmpty(svixTimestamp) || string.IsNullOrEmpty(signatureHeader))
            return false;

        // Optional: reject stale timestamps (>5min skew) to prevent
        // replay. Svix's reference implementation does this.
        if (long.TryParse(svixTimestamp, out var ts))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(now - ts) > 5 * 60) return false;
        }

        // Strip whsec_ prefix, base64-decode the remainder.
        var keyPart = secret.StartsWith("whsec_", StringComparison.Ordinal)
            ? secret["whsec_".Length..]
            : secret;
        byte[] keyBytes;
        try { keyBytes = Convert.FromBase64String(keyPart); }
        catch { return false; }

        var signedPayload = $"{svixId}.{svixTimestamp}.{body}";
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var expected = Convert.ToBase64String(hashBytes);

        // Parse header — space-separated "v1,base64" pairs. Any match.
        foreach (var entry in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(',', 2);
            if (parts.Length != 2 || parts[0] != "v1") continue;
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(parts[1]),
                    Encoding.ASCII.GetBytes(expected)))
            {
                return true;
            }
        }
        return false;
    }
}
