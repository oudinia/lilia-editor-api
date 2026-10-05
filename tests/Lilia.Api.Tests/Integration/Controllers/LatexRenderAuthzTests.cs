using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// The LaTeX render routes check who is asking (A3 of the authz audit, 3 Oct 2026). They answered
/// a stranger with a rendered PDF or PNG of someone else's document, its validation findings or
/// its block ids, and a render costs CPU, so the check must come before the compile.
/// </summary>
[Collection("Integration")]
public class LatexRenderAuthzTests : FourCallersTestBase
{
    public LatexRenderAuthzTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static string[] DocumentRoutes(Seeded s) =>
    [
        $"POST /api/latex/{s.DocumentId}/pdf",
        $"POST /api/latex/{s.DocumentId}/pdf/auto-fit",
        $"POST /api/latex/{s.DocumentId}/png",
        $"POST /api/latex/{s.DocumentId}/validate",
        $"GET /api/latex/{s.DocumentId}/page-map",
        $"GET /api/latex/{s.DocumentId}/validation-rollup",
        $"GET /api/latex/{s.DocumentId}/validation-errors",
    ];

    private static string[] BlockRoutes(Seeded s) =>
    [
        $"POST /api/latex/block/{s.BlockId}/png",
        $"POST /api/latex/block/{s.BlockId}/validate",
        $"POST /api/latex/block/{s.BlockId}/validate-typst",
    ];

    private static Task<HttpResponseMessage> Call(HttpClient c, string route)
    {
        var parts = route.Split(' ', 2);
        return parts[0] == "GET" ? c.GetAsync(parts[1]) : c.PostAsJsonAsync(parts[1], new { });
    }

    [Fact]
    public async Task A_stranger_is_refused_on_every_document_route()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = As(StrangerId);
        foreach (var route in DocumentRoutes(s))
            (await Call(c, route)).StatusCode.Should().Be(HttpStatusCode.Forbidden, route);
    }

    [Fact]
    public async Task A_stranger_is_refused_on_every_block_route_and_so_is_an_unknown_block()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = As(StrangerId);
        foreach (var route in BlockRoutes(s))
            (await Call(c, route)).StatusCode.Should().Be(HttpStatusCode.Forbidden, route);

        foreach (var route in BlockRoutes(s))
            (await Call(c, route.Replace(s.BlockId.ToString(), Guid.NewGuid().ToString()))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "unknown block: " + route);
    }

    [Theory]
    [InlineData(OwnerId)]
    [InlineData(EditorId)]
    [InlineData(ViewerId)]
    public async Task Anyone_with_read_access_is_not_refused(string user)
    {
        var s = await SeedSharedDocumentAsync();
        using var c = As(user);
        foreach (var route in DocumentRoutes(s).Concat(BlockRoutes(s)))
        {
            var res = await Call(c, route);
            res.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, $"{user} {route}").And.NotBe(HttpStatusCode.Unauthorized, $"{user} {route}");
        }
    }

    [Fact]
    public async Task Without_a_login_it_is_401()
    {
        var s = await SeedSharedDocumentAsync();
        using var anon = CreateAnonymousClient();
        foreach (var route in DocumentRoutes(s).Concat(BlockRoutes(s)))
            (await Call(anon, route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, route);
    }
}
