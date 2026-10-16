using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Lilia.Admin.Tests;

/// <summary>
/// The second lock behind Cloudflare Access: the console opens only with a token Access signed,
/// for this Access app, still valid, and for an email on the admin list.
/// </summary>
[Collection("Admin")]
public class AccessGateTests(AdminFixture fx)
{
    [Fact]
    public async Task Without_the_Access_token_nothing_opens()
    {
        foreach (var path in new[] { "/", "/users", "/errors", "/feedback" })
            (await fx.Client(null).GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, path);
    }

    [Fact]
    public async Task The_admin_with_a_valid_token_gets_the_overview()
    {
        var r = await fx.Client(AdminFixture.Token(AdminFixture.AdminEmail)).GetAsync("/");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("Overview");
    }

    [Fact]
    public async Task Another_signed_in_email_is_forbidden()
    {
        (await fx.Client(AdminFixture.Token("someone@else.test")).GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_signed_by_anyone_else_wrong_audience_wrong_issuer_or_expired_is_refused()
    {
        var forged = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };
        var tokens = new Dictionary<string, string>
        {
            ["forged signature"] = AdminFixture.Token(AdminFixture.AdminEmail, key: forged),
            ["another Access app"] = AdminFixture.Token(AdminFixture.AdminEmail, audience: "some-other-app"),
            ["another issuer"] = AdminFixture.Token(AdminFixture.AdminEmail, issuer: "https://evil.cloudflareaccess.com"),
            ["expired"] = AdminFixture.Token(AdminFixture.AdminEmail, expires: DateTime.UtcNow.AddMinutes(-10)),
        };
        foreach (var (why, token) in tokens)
            (await fx.Client(token).GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, why);
    }

    [Fact]
    public void Outside_development_it_refuses_to_start_without_the_Access_settings()
    {
        using var factory = fx.Create("Production", withAccess: false);
        var start = () => factory.CreateClient();
        start.Should().Throw<InvalidOperationException>().WithMessage("*Cloudflare Access*");
    }
}
