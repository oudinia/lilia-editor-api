using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Api.Tests.Themes;

namespace Lilia.Api.Tests.Integration.LatexValidation;

/// <summary>
/// The validation route names a deck's frame that doesn't fit and links it to its slide block
/// (Olivia, 6 Oct 2026): the document pass lists it with the block it belongs to, and the slide
/// block's own validation reports it with the same frame number.
/// </summary>
[Collection("Integration")]
public class FrameOverflowValidationTests : IntegrationTestBase
{
    private readonly string _userId = $"frames-{Guid.NewGuid():N}"[..28];

    public FrameOverflowValidationTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    private const string Message =
        "Frame 3 doesn't fit at this theme's size. Split it, or move a block to the next frame.";

    /// <summary>A Cerulean deck: the title frame (1), "Fits" (2), the third block (3), "Last" (4).</summary>
    private async Task<(Guid DocId, Guid Third)> SeedDeck(bool overflow)
    {
        var doc = await SeedDocumentAsync(_userId, "Frame overflow deck");
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(doc.Id);
            d!.LatexDocumentClass = "beamer";
            d.Look = JsonSerializer.Serialize(new { theme = "cerulean" });
            await db.SaveChangesAsync();
        }
        await SeedBlockAsync(doc.Id, "heading", JsonSerializer.Serialize(new { text = "Opening", level = 1 }), 0);
        await SeedBlockAsync(doc.Id, "slide", JsonSerializer.Serialize(new { title = "Fits", content = "Short." }), 1);
        var third = await SeedBlockAsync(doc.Id, "slide", JsonSerializer.Serialize(overflow
            ? new { title = "Too long", content = BeamerFrameOverflowTests.TooLong }
            : new { title = "Also short", content = "Two lines.\n\nNo more." }), 2);
        await SeedBlockAsync(doc.Id, "slide", JsonSerializer.Serialize(new { title = "Last", content = "Short too." }), 3);
        return (doc.Id, third.Id);
    }

    private static string[] Strings(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(x => x.GetString() ?? "").ToArray();

    [Fact]
    public async Task The_document_pass_lists_the_frame_on_its_slide_block_and_so_does_the_block()
    {
        var (docId, third) = await SeedDeck(overflow: true);
        var client = CreateClientAs(_userId);

        var resp = await client.PostAsync($"/api/latex/{docId}/validate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("valid").GetBoolean().Should().BeTrue();
        Strings(root, "warnings").Should().Contain(Message);
        Strings(root, "blocksWithWarnings").Should().Equal(third.ToString());
        var issue = root.GetProperty("blockIssues").EnumerateArray().Should().ContainSingle().Subject;
        issue.GetProperty("blockId").GetString().Should().Be(third.ToString());
        issue.GetProperty("frame").GetInt32().Should().Be(3);
        issue.GetProperty("message").GetString().Should().Be(Message);

        // The slide block's own validation, compiled on its own, keeps its frame number.
        var block = await client.PostAsync($"/api/latex/block/{third}/validate", null);
        block.StatusCode.Should().Be(HttpStatusCode.OK);
        using var blockJson = JsonDocument.Parse(await block.Content.ReadAsStringAsync());
        Strings(blockJson.RootElement, "warnings").Should().Contain(Message)
            .And.NotContain(w => w.StartsWith("Content is too tall for the page", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_deck_that_fits_reports_no_frame()
    {
        var (docId, third) = await SeedDeck(overflow: false);
        var client = CreateClientAs(_userId);

        var resp = await client.PostAsync($"/api/latex/{docId}/validate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = json.RootElement;
        root.GetProperty("valid").GetBoolean().Should().BeTrue();
        Strings(root, "warnings").Should().NotContain(w => w.Contains("doesn't fit", StringComparison.Ordinal));
        root.GetProperty("blockIssues").EnumerateArray().Should().BeEmpty();

        var block = await client.PostAsync($"/api/latex/block/{third}/validate", null);
        using var blockJson = JsonDocument.Parse(await block.Content.ReadAsStringAsync());
        Strings(blockJson.RootElement, "warnings").Should().NotContain(w => w.Contains("doesn't fit", StringComparison.Ordinal));
    }
}
