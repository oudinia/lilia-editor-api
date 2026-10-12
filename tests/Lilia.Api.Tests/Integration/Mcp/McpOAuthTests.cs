using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Mcp;

/// <summary>
/// Sign-in for outside apps (MCP step 2): the 401 at /api/mcp points to the resource metadata, the
/// metadata names Stytch as the authorization server, and a connected app's token works at
/// /api/mcp and nowhere else.
/// </summary>
[Collection("Integration")]
public class McpOAuthTests : IntegrationTestBase
{
    public McpOAuthTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static HttpRequestMessage ToolsList() =>
        new(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
            Headers = { Accept = { new MediaTypeWithQualityHeaderValue("application/json"), new MediaTypeWithQualityHeaderValue("text/event-stream") } },
        };

    private HttpClient AppClient(string userId)
    {
        var client = CreateClientAs(userId);
        client.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        return client;
    }

    [Fact]
    public async Task The_401_names_the_resource_metadata()
    {
        var res = await CreateAnonymousClient().SendAsync(ToolsList());
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.WwwAuthenticate.ToString()
            .Should().Contain("resource_metadata=\"").And.Contain("/.well-known/oauth-protected-resource/api/mcp\"");
    }

    [Fact]
    public async Task The_metadata_names_Stytch_as_the_authorization_server()
    {
        var res = await CreateAnonymousClient().GetAsync("/.well-known/oauth-protected-resource/api/mcp");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("resource").GetString().Should().EndWith("/api/mcp");
        doc.GetProperty("authorization_servers")[0].GetString().Should().StartWith("https://").And.Contain("stytch");
    }

    [Fact]
    public void The_resource_is_the_public_address_not_the_machines()
    {
        // Production's Email:BaseUrl was the machine's address (12 Oct); apps compare the
        // resource with the URL they connected to.
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:BaseUrl"] = "http://vmi3078948.contaboserver.net:3001",
                ["Mcp:PublicOrigin"] = "https://editor.liliaeditor.com",
            })
            .Build();
        var json = JsonSerializer.Serialize(Lilia.Api.Mcp.McpOAuth.Metadata(config));
        JsonDocument.Parse(json).RootElement.GetProperty("resource").GetString()
            .Should().Be("https://editor.liliaeditor.com/api/mcp");
    }

    [Fact]
    public async Task A_connected_app_reaches_the_mcp_tools()
    {
        await SeedUserAsync("mcp_app_user");
        var res = await AppClient("mcp_app_user").SendAsync(ToolsList());
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/documents")]
    [InlineData("/api/preferences")]
    public async Task A_connected_app_token_is_refused_by_the_rest_of_the_api(string path)
    {
        await SeedUserAsync("mcp_app_boundary");
        var res = await AppClient("mcp_app_boundary").GetAsync(path);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CreateClientAs("mcp_app_boundary").GetAsync(path)).StatusCode
            .Should().Be(HttpStatusCode.OK, "the same person signed in to Lilia itself is fine");
    }
}
