using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// Removed 3 Nov 2026, the owner's call: <c>/api/lilia/ai/*</c> (math, writing, block classify) and the import
/// review's <c>/ai/*</c> (classify, suggest, fix). No client called them, and they ran the model on Lilia's key
/// without charging credits (only an hourly cap per person). Every AI call now goes through a metered path.
/// </summary>
public class UnmeteredAiRoutesGoneTests : IntegrationTestBase
{
    public UnmeteredAiRoutesGoneTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("/api/lilia/ai/math/generate")]
    [InlineData("/api/lilia/ai/math/fix")]
    [InlineData("/api/lilia/ai/writing/improve")]
    [InlineData("/api/lilia/ai/blocks/classify")]
    [InlineData("/api/lilia/import-review/sessions/00000000-0000-0000-0000-000000000001/ai/classify")]
    [InlineData("/api/lilia/import-review/sessions/00000000-0000-0000-0000-000000000001/ai/suggest/b1")]
    [InlineData("/api/lilia/import-review/sessions/00000000-0000-0000-0000-000000000001/ai/fix/b1")]
    public async Task The_route_is_gone(string url)
    {
        var res = await CreateClientAs("gone-ada").PostAsJsonAsync(url, new { description = "x", content = "x", text = "x", action = "x" });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
