using FluentAssertions;
using Lilia.Api.Controllers;
using Xunit;

namespace Lilia.Api.Tests.Features.Accounts;

/// <summary>
/// A confirmation or sign-in link brings the author back where they were going (connector onboarding, 14 Nov 2026):
/// someone signing up while Claude waits to connect confirms the email, often on another device, and the link
/// landed on Home because the return path lived only in the first browser. Never an open redirect.
/// </summary>
public class AuthEmailReturnPathTests
{
    private const string Authorize = "/oauth/authorize?client_id=c1&redirect_uri=https%3A%2F%2Fclaude.ai%2Fcb&scope=openid";

    [Fact]
    public void The_link_carries_the_path_back_to_the_connection()
    {
        var q = AuthEmailController.ReturnQuery(Authorize, AuthEmailKind.Verification);
        q.Should().StartWith("&redirectUrl=");
        Uri.UnescapeDataString(q["&redirectUrl=".Length..]).Should().Be(Authorize);
        AuthEmailController.ReturnQuery(Authorize, AuthEmailKind.MagicLink).Should().Be(q);
    }

    [Fact]
    public void A_password_reset_link_carries_none() =>
        AuthEmailController.ReturnQuery(Authorize, AuthEmailKind.PasswordReset).Should().BeEmpty();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/x")]
    [InlineData("//evil.example/x")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/ok\nSet-Cookie: x")]
    public void Anything_but_a_same_site_path_is_ignored(string? path) =>
        AuthEmailController.ReturnQuery(path, AuthEmailKind.Verification).Should().BeEmpty();
}
