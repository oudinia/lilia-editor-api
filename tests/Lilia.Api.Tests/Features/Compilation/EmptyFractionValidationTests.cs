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

    /// <summary>Retest 2: the full validation list builds its rows from each block's own validation.</summary>
    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_equation_block_warns_on_its_own_and_a_complete_fraction_does_not()
    {
        var doc = await SeedDocumentAsync(_userId, "Empty fraction, block");
        var empty = await SeedBlockAsync(doc.Id, "equation", JsonSerializer.Serialize(new { latex = @"\frac{a}{}", displayMode = true }), 0);
        var whole = await SeedBlockAsync(doc.Id, "equation", JsonSerializer.Serialize(new { latex = @"\frac{a}{b}", displayMode = true }), 1);
        var client = CreateClientAs(_userId);

        async Task<(bool Valid, string[] Warnings)> Validate(Guid id)
        {
            var resp = await client.PostAsync($"/api/latex/block/{id}/validate", null);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = json.RootElement;
            return (root.GetProperty("valid").GetBoolean(),
                root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString() ?? "").ToArray());
        }

        var first = await Validate(empty.Id);
        first.Valid.Should().BeTrue();
        first.Warnings.Should().Contain(w => w.Contains("empty part"));
        // The second call is served from the cache and must say the same.
        (await Validate(empty.Id)).Warnings.Should().Contain(w => w.Contains("empty part"));
        (await Validate(whole.Id)).Warnings.Should().NotContain(w => w.Contains("empty part"));
    }
}
