using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The LML conversions had no test (2 Nov 2026, the coverage round). They are anonymous by design
/// (plain text in, LaTeX, HTML or Markdown out, no document involved) and refuse empty content.
/// </summary>
public class LmlControllerTests : IntegrationTestBase
{
    public LmlControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private const string Lml = "# Results\n\nThe bound is **tight**.";

    [Theory]
    [InlineData("to-latex", "latex", "\\section")]
    [InlineData("to-html", "html", "<h1")]
    [InlineData("to-markdown", "markdown", "# Results")]
    public async Task Anyone_converts_lml(string route, string format, string expected)
    {
        using var anon = CreateAnonymousClient();
        var r = await anon.PostAsJsonAsync($"/api/lml/{route}", new { content = Lml });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!["format"].Should().Be(format);
        body["output"].Should().Contain(expected).And.Contain("tight");
    }

    [Fact]
    public async Task Convert_gives_all_three_and_empty_content_is_refused()
    {
        using var anon = CreateAnonymousClient();
        var all = await anon.PostAsJsonAsync("/api/lml/convert", new { content = Lml });
        var body = await all.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        body!.Keys.Should().BeEquivalentTo("latex", "html", "markdown");
        foreach (var route in new[] { "to-latex", "to-html", "to-markdown", "convert" })
            (await anon.PostAsJsonAsync($"/api/lml/{route}", new { content = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
