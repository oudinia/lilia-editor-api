using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Tests.Integration.Infrastructure;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";

    private static readonly AsyncLocal<ClaimsOverride?> _claimsOverride = new();

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    public static IDisposable SetClaims(string userId, string? email = null, string? name = null)
    {
        _claimsOverride.Value = new ClaimsOverride(userId, email ?? $"{userId}@lilia.test", name ?? userId);
        return new ClaimsScope();
    }

    public static void ClearClaims()
    {
        _claimsOverride.Value = null;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey("X-Test-Anonymous"))
        {
            return Task.FromResult(AuthenticateResult.Fail("Anonymous request"));
        }

        // Support header-based user override for multi-user tests
        var headerUserId = Request.Headers["X-Test-UserId"].FirstOrDefault();
        var headerEmail = Request.Headers["X-Test-Email"].FirstOrDefault();
        var headerName = Request.Headers["X-Test-Name"].FirstOrDefault();

        var overrideValue = _claimsOverride.Value;
        var userId = headerUserId ?? overrideValue?.UserId ?? "test_user_001";
        var email = headerEmail ?? overrideValue?.Email ?? "test@lilia.test";
        var name = headerName ?? overrideValue?.Name ?? "Test User";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId),
            new Claim(ClaimTypes.Email, email),
            new Claim("email", email),
            new Claim(ClaimTypes.Name, name),
            new Claim("name", name),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private record ClaimsOverride(string UserId, string Email, string Name);

    private class ClaimsScope : IDisposable
    {
        public void Dispose() => _claimsOverride.Value = null;
    }
}
