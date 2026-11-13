using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// The bot's #34 retest (13 Nov 2026): the editor's Validate now reads /api/latex/{id}/validate, not the
/// document check, so the empty-fraction warning has to be there too, on the equation's block.
/// </summary>
public class EmptyFractionValidationTests : IntegrationTestBase
{
    private readonly string _userId = $"frac-{Guid.NewGuid():N}"[..28];

    public EmptyFractionValidationTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task Validate_names_the_equation_with_an_empty_fraction_part()
    {
        var doc = await SeedDocumentAsync(_userId, "Empty fraction");
        await SeedBlockAsync(doc.Id, "paragraph", JsonSerializer.Serialize(new { text = "Before." }), 0);
        var eq = await SeedBlockAsync(doc.Id, "equation", JsonSerializer.Serialize(new { latex = @"\frac{a}{}", displayMode = true }), 1);
        await SeedBlockAsync(doc.Id, "paragraph", JsonSerializer.Serialize(new { text = "After." }), 2);

        var resp = await CreateClientAs(_userId).PostAsync($"/api/latex/{doc.Id}/validate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = json.RootElement;

        root.GetProperty("valid").GetBoolean().Should().BeTrue("an empty denominator compiles");
        root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Contain(w => w!.Contains("empty part"));
        root.GetProperty("blocksWithWarnings").EnumerateArray().Select(b => b.GetString()).Should().Contain(eq.Id.ToString());
    }
}
