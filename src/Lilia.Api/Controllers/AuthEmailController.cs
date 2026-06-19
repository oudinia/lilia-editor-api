using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Pre-auth email triggers that the Stytch event webhook doesn't
/// cover. Stytch's free tier doesn't fire events for password-reset
/// requests, magic-link logins, or signup-verification resends, so
/// the frontend hits these endpoints instead of Stytch's SDK methods
/// (which would send unbranded emails from *.customers.stytch.dev →
/// spam folder).
///
/// Flow for each endpoint:
///   1. Validate the request shape (email, locale).
///   2. Look up the user in Stytch via the admin API
///      (POST /v1/users/search). If the email isn't on file we
///      silently 200 to avoid leaking who's registered.
///   3. Mint an embedded magic-link token via
///      POST /v1/magic_links { user_id, … } — Stytch returns the
///      token, no email sent.
///   4. Build a callback URL routing back into the editor's
///      /auth/callback handler.
///   5. Send the branded email via IEmailService (Resend, verified
///      liliaeditor.com sender).
///
/// All endpoints are [AllowAnonymous]: they run before the user has
/// a session. Defense-in-depth: rate-limit per IP at the proxy layer
/// once we have one.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthEmailController : ControllerBase
{
    private readonly IEmailService _emails;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AuthEmailController> _logger;

    public AuthEmailController(
        IEmailService emails,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<AuthEmailController> logger)
    {
        _emails = emails;
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [HttpPost("send-password-reset")]
    public Task<IActionResult> SendPasswordReset(
        [FromBody] AuthEmailRequest req, CancellationToken ct)
        => DispatchAsync(req, AuthEmailKind.PasswordReset, ct);

    [HttpPost("send-magic-link")]
    public Task<IActionResult> SendMagicLink(
        [FromBody] AuthEmailRequest req, CancellationToken ct)
        => DispatchAsync(req, AuthEmailKind.MagicLink, ct);

    [HttpPost("resend-verification")]
    public Task<IActionResult> ResendVerification(
        [FromBody] AuthEmailRequest req, CancellationToken ct)
        => DispatchAsync(req, AuthEmailKind.Verification, ct);

    private async Task<IActionResult> DispatchAsync(
        AuthEmailRequest req, AuthEmailKind kind, CancellationToken ct)
    {
        var email = req.Email?.Trim();
        if (string.IsNullOrEmpty(email) || !IsLikelyEmail(email))
        {
            return BadRequest(new { error = "invalid_email" });
        }

        // Resolve the callback origin. Default = editor (back-compat).
        // Callers may pass redirectTo (e.g. the storefront wants the
        // magic link to land back on liliaeditor.com, not editor.*)
        // — only honored if the origin is on the Auth:CallbackOrigins
        // allowlist, otherwise we silently fall back to the editor
        // default to prevent open-redirect / phishing.
        var callbackOrigin = ResolveCallbackOrigin(req.RedirectTo);

        var http = BuildAdminClient(out var apiBase);
        if (http is null)
        {
            _logger.LogError("Stytch admin credentials missing — cannot dispatch {Kind} email", kind);
            // Don't reveal infra issues to the caller.
            return Ok(new { sent = false, reason = "service_unavailable" });
        }

        // Look up the user by email. If they're not registered, silently
        // 200 — leaking "this email exists" enables enumeration attacks.
        var userId = await ResolveUserIdAsync(http, apiBase, email, ct);
        if (userId is null)
        {
            _logger.LogInformation(
                "{Kind} email requested for unregistered {Email} — silently 200", kind, email);
            return Ok(new { sent = true });  // Plausible-success response.
        }

        var token = await MintMagicLinkTokenAsync(http, apiBase, userId, ct);
        if (token is null)
        {
            _logger.LogWarning("Could not mint magic-link token for {UserId} ({Kind})", userId, kind);
            return Ok(new { sent = false, reason = "mint_failed" });
        }

        var intent = kind switch
        {
            AuthEmailKind.PasswordReset => "&intent=password_reset",
            AuthEmailKind.MagicLink     => "",                       // default = sign-in
            AuthEmailKind.Verification  => "&intent=verify",
            _ => "",
        };
        var url = $"{callbackOrigin}/auth/callback?stytch_token_type=magic_links" +
                  $"&token={Uri.EscapeDataString(token)}{intent}";

        try
        {
            switch (kind)
            {
                case AuthEmailKind.PasswordReset:
                    await _emails.SendStytchPasswordResetAsync(email, url, req.Locale);
                    break;
                case AuthEmailKind.MagicLink:
                    await _emails.SendStytchMagicLinkLoginAsync(email, url, req.Locale);
                    break;
                case AuthEmailKind.Verification:
                    await _emails.SendStytchVerificationAsync(email, url, req.Locale);
                    break;
            }
            _logger.LogInformation("{Kind} email sent to {Email}", kind, email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend send failed for {Kind} → {Email}", kind, email);
            return Ok(new { sent = false, reason = "send_failed" });
        }

        return Ok(new { sent = true });
    }

    /// <summary>
    /// Pick the callback origin for the magic-link URL. Caller may
    /// request a different origin via <c>redirectTo</c> (e.g. the
    /// storefront wants <c>https://liliaeditor.com</c>); we honor it
    /// only if its scheme+host appears in <c>Auth:CallbackOrigins</c>.
    /// The path/query of <c>redirectTo</c> is discarded — the actual
    /// path is always <c>/auth/callback</c> on the chosen origin.
    /// Unknown / malformed origins silently fall back to the editor
    /// default to prevent open-redirect abuse.
    /// </summary>
    private string ResolveCallbackOrigin(string? redirectTo)
    {
        var fallback = _config["Email:BaseUrl"] ?? "https://editor.liliaeditor.com";
        if (string.IsNullOrWhiteSpace(redirectTo)) return fallback;

        if (!Uri.TryCreate(redirectTo, UriKind.Absolute, out var uri))
            return fallback;
        var origin = $"{uri.Scheme}://{uri.Authority}";

        var allowlist = (_config["Auth:CallbackOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var allowed in allowlist)
        {
            if (string.Equals(origin, allowed, StringComparison.OrdinalIgnoreCase))
                return origin;
        }
        _logger.LogWarning(
            "Rejected redirectTo origin {Origin} (not on Auth:CallbackOrigins) — using default", origin);
        return fallback;
    }

    // ---- Stytch admin helpers ----

    private HttpClient? BuildAdminClient(out string apiBase)
    {
        var projectId = _config["Stytch:ProjectId"];
        var secret = _config["Stytch:Secret"];
        apiBase = _config["Stytch:ApiBase"] ?? "https://api.stytch.com";
        if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(secret))
            return null;
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(apiBase);
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{projectId}:{secret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        return http;
    }

    /// <summary>
    /// POST /v1/users/search → returns matching users by email.
    /// Returns the first user_id, or null when no user matches.
    /// </summary>
    private async Task<string?> ResolveUserIdAsync(
        HttpClient http, string apiBase, string email, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            query = new
            {
                @operator = "AND",
                operands = new[]
                {
                    new { filter_name = "email_address", filter_value = new[] { email } },
                },
            },
            limit = 1,
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var res = await http.PostAsync("/v1/users/search", content, ct);
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stytch /v1/users/search → {Status}", (int)res.StatusCode);
            return null;
        }
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }
        var first = results[0];
        return first.TryGetProperty("user_id", out var uid) ? uid.GetString() : null;
    }

    /// <summary>
    /// POST /v1/magic_links → embedded magic-link token (no email sent).
    /// We email it ourselves via IEmailService.
    /// </summary>
    private async Task<string?> MintMagicLinkTokenAsync(
        HttpClient http, string _apiBase, string userId, CancellationToken ct)
    {
        // /v1/magic_links rejects session_duration_minutes — that param
        // belongs on the *authenticate* call once the user clicks the
        // link. The mint endpoint only takes user_id (+ optional
        // expiration_minutes to bound the click window).
        var payload = new { user_id = userId, expiration_minutes = 60 };
        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var res = await http.PostAsync("/v1/magic_links", content, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Stytch /v1/magic_links → {Status} for {UserId}: {Body}",
                (int)res.StatusCode, userId, body.Length > 200 ? body[..200] : body);
            return null;
        }
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("token", out var tk) ? tk.GetString() : null;
    }

    private static bool IsLikelyEmail(string s)
    {
        var at = s.IndexOf('@');
        var dot = s.LastIndexOf('.');
        return at > 0 && dot > at + 1 && dot < s.Length - 1;
    }
}

public class AuthEmailRequest
{
    public string? Email { get; set; }
    public string? Locale { get; set; }

    /// <summary>
    /// Origin the magic-link should land on (e.g. <c>https://liliaeditor.com</c>).
    /// Only honored if listed in <c>Auth:CallbackOrigins</c>; otherwise the
    /// editor origin is used. The path is always <c>/auth/callback</c>.
    /// </summary>
    public string? RedirectTo { get; set; }
}

internal enum AuthEmailKind
{
    PasswordReset,
    MagicLink,
    Verification,
}
