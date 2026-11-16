using FluentAssertions;
using Lilia.Api.Mcp;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lilia.Api.Tests.Features.Mcp;

/// <summary>
/// 16 Nov 2026: Claude refused the connector because Stytch's authorization response named another issuer than its
/// metadata; the fix moves Stytch to a custom auth domain. Tokens issued under the old address must keep working.
/// </summary>
public class McpIssuersTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void The_session_issuer_the_authorization_server_and_the_old_addresses_are_accepted() =>
        McpOAuth.ValidIssuers("stytch.com/project-live-x", Config(
                ("Mcp:AuthorizationServer", "https://auth.liliaeditor.com/"),
                ("Stytch:AdditionalIssuers", " https://fossil-pig-0846.customers.stytch.com/ , https://auth.liliaeditor.com")))
            .Should().Equal("stytch.com/project-live-x", "https://auth.liliaeditor.com", "https://fossil-pig-0846.customers.stytch.com");

    [Fact]
    public void Without_extra_issuers_nothing_changes() =>
        McpOAuth.ValidIssuers("stytch.com/project-live-x", Config(("Mcp:AuthorizationServer", "https://fossil-pig-0846.customers.stytch.com")))
            .Should().Equal("stytch.com/project-live-x", "https://fossil-pig-0846.customers.stytch.com");
}
