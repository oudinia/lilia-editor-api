using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Local development utilities that have no place in production.
///
/// Every endpoint here is hard-gated on
/// <see cref="IHostEnvironment.IsDevelopment"/> — non-Development
/// environments return 404 before the action even runs, so accidentally
/// shipping a build with these enabled is harmless.
///
/// Today this controller hosts one thing: a shortcut to fire the same
/// verification-email pipeline as <c>StytchWebhookController</c> without
/// requiring Stytch's webhook to actually reach localhost (no ngrok).
/// Used by shared / automated signup-flow tests that need to read the
/// resulting email from Mailpit.
/// </summary>
[ApiController]
[Route("api/dev")]
[AllowAnonymous]
public class DevToolsController : ControllerBase
{
    private readonly IEmailService _emails;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<DevToolsController> _logger;

    public DevToolsController(
        IEmailService emails,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<DevToolsController> logger)
    {
        _emails = emails;
        _httpFactory = httpFactory;
        _config = config;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Re-fires the same flow the Stytch <c>direct.user.create</c>
    /// webhook does — look up the Stytch user by email, mint a magic
    /// link via Stytch's admin API, and send the verification email
    /// via <see cref="IEmailService"/> (which is the SMTP / Mailpit
    /// transport in development).
    ///
    /// Returns the magic-link URL in the JSON body too, so tests that
    /// don't want to round-trip through Mailpit can use it directly.
    /// </summary>
    [HttpPost("trigger-stytch-verification")]
    public async Task<IActionResult> TriggerStytchVerification(
        [FromQuery] string email,
        CancellationToken ct)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "missing 'email' query parameter" });
        }

        var (projectId, secret, apiBase) = ReadStytchAdminConfig();
        if (projectId is null || secret is null)
        {
            return Problem(
                title: "Stytch admin credentials missing",
                detail: "Set Stytch__ProjectId + Stytch__Secret to enable this endpoint.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(apiBase);
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{projectId}:{secret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        var userId = await LookupUserIdAsync(http, email, ct);
        if (userId is null)
        {
            return NotFound(new { error = $"no Stytch user matches '{email}'" });
        }

        var magicLink = await MintMagicLinkAsync(http, userId, ct);
        if (magicLink is null)
        {
            return Problem(
                title: "Failed to mint magic link",
                detail: "Stytch admin POST /v1/magic_links returned a non-success status. Check API logs.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        try
        {
            await _emails.SendStytchVerificationAsync(email, magicLink, locale: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DevTools trigger-stytch-verification: email send failed for {Email}", email);
            return Problem(
                title: "Email send failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }

        _logger.LogInformation(
            "DevTools trigger-stytch-verification: sent verification email to {Email} for user {UserId}",
            email, userId);

        return Ok(new
        {
            email,
            userId,
            magicLink,
            note = "Verification email queued via the configured Email__Transport.",
        });
    }

    private (string? ProjectId, string? Secret, string ApiBase) ReadStytchAdminConfig()
    {
        var projectId = _config["Stytch:ProjectId"];
        var secret = _config["Stytch:Secret"];
        var apiBase = _config["Stytch:ApiBase"] ?? "https://api.stytch.com";
        return (
            string.IsNullOrEmpty(projectId) ? null : projectId,
            string.IsNullOrEmpty(secret) ? null : secret,
            apiBase);
    }

    private async Task<string?> LookupUserIdAsync(HttpClient http, string email, CancellationToken ct)
    {
        // Stytch admin POST /v1/users/search filters by email. Asking
        // for at most 1 hit keeps the response shape simple. Returns
        // null if no user matches.
        var payload = new
        {
            limit = 1,
            query = new
            {
                @operator = "AND",
                operands = new[]
                {
                    new { filter_name = "email_address", filter_value = new[] { email } },
                },
            },
        };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var res = await http.PostAsync("/v1/users/search", content, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Stytch admin POST /v1/users/search failed {Status}: {Body}",
                (int)res.StatusCode, body);
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("results", out var results)) return null;
        if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) return null;

        var first = results[0];
        return first.TryGetProperty("user_id", out var idEl) ? idEl.GetString() : null;
    }

    private async Task<string?> MintMagicLinkAsync(HttpClient http, string userId, CancellationToken ct)
    {
        // Same call StytchWebhookController.MintMagicLinkAsync makes.
        // Kept inline rather than refactored into a shared service —
        // the controller is dev-only and the shape may drift.
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
        if (!doc.RootElement.TryGetProperty("token", out var tokenEl)) return null;
        var token = tokenEl.GetString();
        if (string.IsNullOrEmpty(token)) return null;

        var editorOrigin = _config["Email:BaseUrl"] ?? "http://localhost:3001";
        return $"{editorOrigin}/auth/callback?stytch_token_type=magic_links&token={Uri.EscapeDataString(token)}";
    }
}
