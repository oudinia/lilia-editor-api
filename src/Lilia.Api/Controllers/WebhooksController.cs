using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Inbound webhook receivers — currently Kinde only. Verified with HMAC
/// over the raw request body using a shared secret in
/// <c>Webhooks:Kinde:Secret</c>. Failure paths log + 401; success paths
/// dispatch into our service layer.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhooksController : ControllerBase
{
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IEmailService email, IConfiguration config, ILogger<WebhooksController> logger)
    {
        _email = email;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Kinde webhook receiver (LILIA-95). Configure in Kinde admin →
    /// "Workflows" → "Webhooks" → New, point at
    /// <c>https://editor.liliaeditor.com/api/webhooks/kinde</c>, subscribe
    /// to <c>user.created</c>.
    ///
    /// Set the secret in DO env: <c>Webhooks__Kinde__Secret</c>. Until
    /// then, the endpoint accepts unsigned requests in development only.
    /// </summary>
    [HttpPost("kinde")]
    public async Task<IActionResult> Kinde()
    {
        // Read the raw body for HMAC verification + JSON parsing.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var secret = _config["Webhooks:Kinde:Secret"];
        if (!string.IsNullOrEmpty(secret))
        {
            var signature = Request.Headers["X-Kinde-Signature"].FirstOrDefault();
            if (string.IsNullOrEmpty(signature) || !VerifyHmac(body, secret, signature))
            {
                _logger.LogWarning("Kinde webhook signature invalid or missing — rejecting.");
                return Unauthorized(new { error = "invalid_signature" });
            }
        }
        else if (_config.GetValue<string>("ASPNETCORE_ENVIRONMENT") == "Production")
        {
            _logger.LogError("Kinde webhook called in Production but Webhooks:Kinde:Secret not set — refusing.");
            return Unauthorized(new { error = "secret_not_configured" });
        }

        // Parse the event. Kinde wraps the payload as { type, data: { ... } }.
        // Schema variants exist; we read defensively.
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var type = TryGetString(root, "type") ?? TryGetString(root, "event_type") ?? "";
            _logger.LogInformation("Kinde webhook received: {Type}", type);

            if (type == "user.created" || type == "user.authenticated")
            {
                var data = root.TryGetProperty("data", out var d) ? d : root;
                // Kinde shape: data.user.email, data.user.first_name
                JsonElement user = data;
                if (data.TryGetProperty("user", out var userEl)) user = userEl;
                var email = TryGetString(user, "email") ?? TryGetString(user, "preferred_email");
                var firstName = TryGetString(user, "first_name") ?? TryGetString(user, "given_name");

                if (!string.IsNullOrEmpty(email))
                {
                    // Only fire welcome on user.created — user.authenticated is a
                    // sign-in, not a registration. Defensive: some Kinde tenants
                    // emit user.created multiple times if the signal is replayed,
                    // but we accept that — better double-greet than miss the moment.
                    if (type == "user.created")
                    {
                        try
                        {
                            await _email.SendWelcomeAsync(email, firstName);
                            _logger.LogInformation("Welcome email dispatched to {Email}", email);
                        }
                        catch (Exception ex)
                        {
                            // Don't fail the webhook — Kinde retries on non-2xx,
                            // but a flaky email shouldn't loop the whole flow.
                            _logger.LogError(ex, "Welcome email failed for {Email}", email);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Kinde {Type} payload had no email — skipping welcome.", type);
                }
            }
            return Ok(new { received = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Kinde webhook body was not valid JSON: {Body}",
                body.Length > 500 ? body[..500] : body);
            return BadRequest(new { error = "invalid_json" });
        }
    }

    private static bool VerifyHmac(string body, string secret, string headerValue)
    {
        // Kinde signs with HMAC-SHA256 over the raw body, hex-encoded. The
        // header may be prefixed (e.g. "sha256=...") — normalise both ways.
        var sig = headerValue.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? headerValue[7..]
            : headerValue;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hex),
            Encoding.UTF8.GetBytes(sig.Trim().ToLowerInvariant())
        );
    }

    private static string? TryGetString(JsonElement el, string property)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(property, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
