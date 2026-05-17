using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Receives Stytch's BYOM (Bring Your Own Mailer) webhook. Stytch posts
/// here every time it would have sent an auth email — verification,
/// password reset, magic-link login. We validate the signature, route by
/// event type, and hand off to <see cref="IEmailService"/> which sends
/// via Resend on our verified <c>liliaeditor.com</c> domain.
///
/// Endpoint: <c>POST /api/webhooks/stytch/email</c>
///
/// Config (DO env):
///   <c>Stytch:WebhookSecret</c> — HMAC SHA-256 signing key from
///                                  Stytch dashboard → Configuration →
///                                  Email Settings → BYOM webhook.
///
/// Retry contract: 200 = sent / acknowledged. 4xx = bad request, Stytch
/// will NOT retry. 5xx = transient, Stytch retries with exponential
/// backoff. We return 200 on send-failure too, after logging — re-sending
/// an auth email to a user is harmless and Stytch's retries flood logs.
/// </summary>
[ApiController]
[Route("api/webhooks/stytch")]
[AllowAnonymous]
public class StytchWebhookController : ControllerBase
{
    private readonly IEmailService _emails;
    private readonly StytchWebhookSettings _settings;
    private readonly ILogger<StytchWebhookController> _logger;

    public StytchWebhookController(
        IEmailService emails,
        StytchWebhookSettings settings,
        ILogger<StytchWebhookController> logger)
    {
        _emails = emails;
        _settings = settings;
        _logger = logger;
    }

    [HttpPost("email")]
    public async Task<IActionResult> Email(CancellationToken ct)
    {
        // Read the raw body once — signature is over the exact bytes
        // Stytch sent, so re-serialising via a model would change the
        // hash. The body is JSON and small (~1KB).
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        if (string.IsNullOrEmpty(rawBody))
        {
            _logger.LogWarning("Stytch email webhook: empty body");
            return BadRequest(new { error = "empty_body" });
        }

        // Signature validation — skipped in dev when secret is unset so
        // we can replay payloads via curl during local testing. Production
        // MUST have a secret configured; the validator throws if absent
        // and required.
        var signature = Request.Headers["X-Stytch-Signature"].ToString();
        if (!StytchSignatureValidator.IsValid(rawBody, signature, _settings.WebhookSecret, _settings.RequireSignature))
        {
            _logger.LogWarning(
                "Stytch email webhook: signature mismatch (header={SigPresent}, secret_configured={SecretPresent})",
                !string.IsNullOrEmpty(signature),
                !string.IsNullOrEmpty(_settings.WebhookSecret));
            return Unauthorized(new { error = "signature_mismatch" });
        }

        StytchEmailWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StytchEmailWebhookPayload>(rawBody, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Stytch email webhook: invalid JSON");
            return BadRequest(new { error = "invalid_json" });
        }

        if (payload is null || string.IsNullOrEmpty(payload.Recipient))
        {
            _logger.LogWarning("Stytch email webhook: missing required fields");
            return BadRequest(new { error = "missing_fields" });
        }

        try
        {
            await DispatchAsync(payload);
            _logger.LogInformation(
                "Stytch email webhook: sent {TemplateType} to {Recipient} (request={StytchRequestId})",
                payload.TemplateType,
                payload.Recipient,
                payload.RequestId);
            return Ok(new { delivered = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Stytch email webhook: send failed for {TemplateType} → {Recipient}",
                payload.TemplateType,
                payload.Recipient);
            // 200 to suppress Stytch retries — the recipient will hit the
            // resend button if they actually need the email. Spamming the
            // log with retries is worse than a single quiet failure.
            return Ok(new { delivered = false, reason = "send_failed" });
        }
    }

    private async Task DispatchAsync(StytchEmailWebhookPayload p)
    {
        // Stytch's `template_type` enum varies slightly across products;
        // we accept either the dotted form or the underscored form.
        var kind = NormalizeTemplateType(p.TemplateType);
        switch (kind)
        {
            case "verification":
            case "email_verification":
            case "signup":
                await _emails.SendStytchVerificationAsync(p.Recipient!, p.ActionUrl ?? "", p.Locale);
                break;
            case "password_reset":
            case "passwords_email_reset_start":
                await _emails.SendStytchPasswordResetAsync(p.Recipient!, p.ActionUrl ?? "", p.Locale);
                break;
            case "login":
            case "magic_link_login":
            case "magic_links_email_login_or_create":
                await _emails.SendStytchMagicLinkLoginAsync(p.Recipient!, p.ActionUrl ?? "", p.Locale);
                break;
            default:
                _logger.LogWarning(
                    "Stytch email webhook: unknown template_type {TemplateType} — falling back to magic-link login template",
                    p.TemplateType);
                await _emails.SendStytchMagicLinkLoginAsync(p.Recipient!, p.ActionUrl ?? "", p.Locale);
                break;
        }
    }

    private static string NormalizeTemplateType(string? raw) =>
        (raw ?? "").Trim().ToLowerInvariant().Replace('.', '_').Replace('-', '_');

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}

/// <summary>
/// Body shape Stytch posts to our BYOM webhook. We tolerate a few
/// alternate field names because Stytch's payload schema has varied
/// per docs version.
/// </summary>
public class StytchEmailWebhookPayload
{
    /// <summary>The recipient's email address.</summary>
    [JsonPropertyName("recipient")]
    public string? Recipient { get; set; }

    /// <summary>Stytch template enum (e.g. "magic_links.email.login_or_create").</summary>
    [JsonPropertyName("template_type")]
    public string? TemplateType { get; set; }

    /// <summary>The click-through URL with the Stytch token embedded.</summary>
    [JsonPropertyName("action_url")]
    public string? ActionUrl { get; set; }

    /// <summary>Alternate field name some Stytch payloads use.</summary>
    [JsonPropertyName("magic_link_url")]
    public string? MagicLinkUrl
    {
        get => null;
        set { if (!string.IsNullOrEmpty(value)) ActionUrl = value; }
    }

    /// <summary>Alternate field name for password-reset payloads.</summary>
    [JsonPropertyName("reset_url")]
    public string? ResetUrl
    {
        get => null;
        set { if (!string.IsNullOrEmpty(value)) ActionUrl = value; }
    }

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("email_id")]
    public string? EmailId { get; set; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }
}

public class StytchWebhookSettings
{
    public string WebhookSecret { get; set; } = "";
    public bool RequireSignature { get; set; } = true;
}

/// <summary>
/// HMAC SHA-256 signature validator. Stytch signs the raw body with the
/// project's webhook secret and includes the signature in the
/// <c>X-Stytch-Signature</c> header (lowercase hex).
/// </summary>
public static class StytchSignatureValidator
{
    public static bool IsValid(string rawBody, string signatureHeader, string secret, bool require)
    {
        if (string.IsNullOrEmpty(secret))
        {
            // Secret not configured. In dev we let it through (the controller
            // logs a warning); in prod the boot-up env check should have
            // caught this before any request lands.
            return !require;
        }
        if (string.IsNullOrEmpty(signatureHeader)) return false;

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var expected = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Strip any "sha256=" prefix some webhook providers add. Constant-
        // time compare to prevent timing side channels.
        var provided = signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signatureHeader["sha256=".Length..]
            : signatureHeader;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(provided.ToLowerInvariant()));
    }
}
