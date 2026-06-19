using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

// =====================================================================
//  Session-side auth endpoints — the ones the storefront / editor /
//  desktop / mobile clients need *after* they hold a Stytch session_jwt.
//
//    GET  /api/auth/me        — current user profile (auth)
//    POST /api/auth/sign-out  — revoke the current Stytch session (auth)
//
//  Anonymous, pre-auth email triggers (send-magic-link, etc.) live in
//  AuthEmailController — those run *before* the user has a session.
// =====================================================================

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthSessionController : ControllerBase
{
    private readonly IUserService _users;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AuthSessionController> _logger;

    public AuthSessionController(
        IUserService users,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<AuthSessionController> logger)
    {
        _users = users;
        _config = config;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Return the authenticated user's profile. Used by every client
    /// (store account page, editor sidebar, native apps) right after
    /// sign-in to display the user's name/avatar.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = await _users.GetUserAsync(userId);
        if (user is null)
        {
            // Should only happen if UserSync hasn't run yet for this
            // session (race on first sign-in). The caller can retry.
            _logger.LogWarning("/auth/me: no User row for {UserId} — UserSync race?", userId);
            return NotFound(new { error = "user_not_synced" });
        }

        return Ok(user);
    }

    /// <summary>
    /// Revoke the current Stytch session server-side. Idempotent —
    /// returns 200 even if the session was already revoked, so callers
    /// can fire-and-forget. Clients should also clear their local SDK
    /// session state (the Stytch JS SDK does this automatically when
    /// the revoke succeeds; native SDKs should call their own revoke).
    /// </summary>
    [HttpPost("sign-out")]
    public async Task<IActionResult> SignOut(CancellationToken ct)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Pull the bearer JWT off the incoming request — that's what
        // Stytch's session.revoke wants, not the user_id.
        var authHeader = Request.Headers.Authorization.ToString();
        var sessionJwt = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : null;
        if (string.IsNullOrEmpty(sessionJwt))
        {
            // Already gone — treat as a no-op.
            return Ok(new { revoked = true });
        }

        var http = BuildAdminClient(out _);
        if (http is null)
        {
            _logger.LogError("Stytch admin credentials missing — cannot revoke session for {UserId}", userId);
            // Don't fail the client's sign-out UX over a config gap;
            // the SDK will clear local state regardless.
            return Ok(new { revoked = false, reason = "service_unavailable" });
        }

        var payload = JsonSerializer.Serialize(new { session_jwt = sessionJwt });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var res = await http.PostAsync("/v1/sessions/revoke", content, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Stytch /v1/sessions/revoke → {Status} for {UserId}: {Body}",
                (int)res.StatusCode, userId, body.Length > 200 ? body[..200] : body);
            // Stytch returns 400 if the session is already expired —
            // still a successful sign-out from the user's perspective.
            return Ok(new { revoked = false, reason = "stytch_error" });
        }

        return Ok(new { revoked = true });
    }

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
}
