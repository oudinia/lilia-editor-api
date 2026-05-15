using System.Text;
using System.Text.Json;
using Lilia.Api.Events.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Wolverine;

namespace Lilia.Api.Controllers;

/// <summary>
/// Inbound webhook receivers — currently Kinde only.
///
/// Kinde signs every webhook payload as a JWT (header.payload.signature)
/// using the same JWKS keys it uses for auth tokens — there is no
/// per-webhook shared secret. The request body IS the JWT (not JSON);
/// we validate the signature against
/// <c>{Auth:Authority}/.well-known/jwks</c>, then decode the payload
/// and treat it as the original event JSON.
///
/// On <c>user.created</c> we publish a <see cref="UserCreatedEvent"/>
/// onto Wolverine; the Teams slice picks it up and mints a default
/// team + sends the welcome email.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhooksController : ControllerBase
{
    private readonly IMessageBus _bus;
    private readonly IConfiguration _config;
    private readonly IKindeJwksProvider _jwks;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IMessageBus bus,
        IConfiguration config,
        IKindeJwksProvider jwks,
        ILogger<WebhooksController> logger)
    {
        _bus = bus;
        _config = config;
        _jwks = jwks;
        _logger = logger;
    }

    [HttpPost("kinde")]
    public async Task<IActionResult> Kinde(CancellationToken ct)
    {
        // The body is the JWT. Read raw text — no JSON parsing yet.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = (await reader.ReadToEndAsync(ct)).Trim();
        Request.Body.Position = 0;

        if (string.IsNullOrEmpty(body))
        {
            _logger.LogWarning("Kinde webhook: empty body");
            return BadRequest(new { error = "empty_body" });
        }

        // Validate the JWT against Kinde's published keys. In Production
        // this is mandatory; in Development we skip validation if the
        // Kinde authority isn't reachable so local replay/curl still works.
        string payloadJson;
        try
        {
            payloadJson = await ValidateAndExtractPayloadAsync(body, ct);
        }
        catch (SecurityTokenException ex)
        {
            // Surface the *actual* validation failure — the type and
            // message tell us which check tripped (kid lookup, issuer
            // mismatch, expired, wrong alg). Also log the JWT's own
            // header + iss claim so we can compare against what we
            // expect from JWKS without re-reading prod logs blind.
            string? hdrJson = null, payloadIss = null, payloadAud = null, payloadType = null;
            try
            {
                var parts = body.Split('.');
                if (parts.Length == 3)
                {
                    hdrJson = DecodeBase64UrlAsString(parts[0]);
                    using var pdoc = JsonDocument.Parse(DecodeBase64UrlAsString(parts[1]));
                    payloadIss = TryGetString(pdoc.RootElement, "iss");
                    payloadAud = TryGetString(pdoc.RootElement, "aud");
                    payloadType = TryGetString(pdoc.RootElement, "type") ?? TryGetString(pdoc.RootElement, "event_type");
                }
            }
            catch { /* diagnostics only */ }
            _logger.LogWarning(
                "Kinde webhook signature validation failed: {ExceptionType}: {ExceptionMessage}. JWT header={Header}, iss={Iss}, aud={Aud}, type={Type}",
                ex.GetType().Name, ex.Message, hdrJson ?? "<unparseable>", payloadIss, payloadAud, payloadType);
            return Unauthorized(new { error = "invalid_signature" });
        }
        catch (Exception ex) when (!_config.GetValue<string>("ASPNETCORE_ENVIRONMENT")!.Equals("Production"))
        {
            // Non-prod fallback: assume the body might be raw JSON (e.g.
            // local smoke probes via curl). Fail loud if it isn't.
            _logger.LogWarning(ex, "Kinde JWT validation skipped in non-prod; treating body as raw JSON");
            payloadJson = body;
        }

        // Parse the (now-trusted) payload exactly like the previous
        // version did. Schema variants tolerated defensively.
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var type = TryGetString(root, "type") ?? TryGetString(root, "event_type") ?? "";
            _logger.LogInformation("Kinde webhook received: {Type}", type);

            if (type == "user.created" || type == "user.authenticated")
            {
                var data = root.TryGetProperty("data", out var d) ? d : root;
                JsonElement user = data;
                if (data.TryGetProperty("user", out var userEl)) user = userEl;
                var userId = TryGetString(user, "id") ?? TryGetString(user, "sub");
                var email = TryGetString(user, "email") ?? TryGetString(user, "preferred_email");
                var firstName = TryGetString(user, "first_name") ?? TryGetString(user, "given_name");

                if (type == "user.created" && !string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(userId))
                {
                    await _bus.PublishAsync(new UserCreatedEvent(userId, email, firstName));
                    _logger.LogInformation("UserCreatedEvent published for {UserId}", userId);
                }
                else if (type == "user.created")
                {
                    _logger.LogWarning("Kinde user.created payload missing id/email — skipping fan-out.");
                }
            }
            return Ok(new { received = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Kinde webhook payload was not valid JSON: {Body}",
                payloadJson.Length > 500 ? payloadJson[..500] : payloadJson);
            return BadRequest(new { error = "invalid_json" });
        }
    }

    /// <summary>
    /// Validate <paramref name="jwt"/> against Kinde's signing keys and
    /// return the decoded JSON payload. Throws
    /// <see cref="SecurityTokenException"/> on any signature/format
    /// failure.
    /// </summary>
    private async Task<string> ValidateAndExtractPayloadAsync(string jwt, CancellationToken ct)
    {
        var keys = await _jwks.GetSigningKeysAsync(ct);
        var authority = _config["Auth:Authority"] ?? "";

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrEmpty(authority),
            ValidIssuer = authority,
            ValidateAudience = false,
            ValidateLifetime = true,
            // Kinde's webhook JWTs are short-lived; the small skew
            // covers clock drift between their signers and our pod.
            ClockSkew = TimeSpan.FromMinutes(2),
            IssuerSigningKeys = keys,
            ValidateIssuerSigningKey = true,
        };

        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(jwt, parameters, out var validated);
        // The payload portion of the JWT is base64url-encoded JSON —
        // pull it straight from the validated token rather than
        // re-decoding by hand.
        return ((JwtSecurityToken)validated).Payload.SerializeToJson();
    }

    private static string? TryGetString(JsonElement el, string property)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(property, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string DecodeBase64UrlAsString(string segment)
    {
        // JWT segments are base64url-encoded (no padding). Pad to 4-byte
        // boundary, swap url-safe chars, decode as UTF-8.
        var s = segment.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}

/// <summary>
/// Caches Kinde's JWKS document via OIDC discovery so we don't hit
/// the well-known endpoint on every webhook. Refreshed automatically
/// by <see cref="ConfigurationManager{T}"/> (default 24h cache).
/// </summary>
public interface IKindeJwksProvider
{
    Task<IEnumerable<SecurityKey>> GetSigningKeysAsync(CancellationToken ct);
}

public class KindeJwksProvider : IKindeJwksProvider
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _manager;

    public KindeJwksProvider(IConfiguration config)
    {
        var authority = config["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority must be set to enable Kinde webhook verification");
        _manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    public async Task<IEnumerable<SecurityKey>> GetSigningKeysAsync(CancellationToken ct)
    {
        var oidc = await _manager.GetConfigurationAsync(ct);
        return oidc.SigningKeys;
    }
}
