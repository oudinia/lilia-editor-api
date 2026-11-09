using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Features.References;

/// <summary>
/// References written in a bibliography block count as the document's entries everywhere, not only in the PDF
/// (9 Nov 2026, the launch bot's #49: a draft wrote "@cite[sleep1] …" lines; the PDF listed them, but the editor marked
/// each citation missing, the bibliography panel showed none and the Markdown export left the list empty).
/// </summary>
public class BibliographyFromBlocksTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";
    private const string Lines = "@cite[sleep1] Author, A. — A paper on sleep. (Placeholder.)\n@cite[sleep2] Author, B. — Another paper.";
    private const string BibTeX = "@article{walker2017, title={Why We Sleep}, author={Walker, Matthew}, year={2017}}";

    public BibliographyFromBlocksTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static Block BibBlock(string text) => new()
    {
        Id = Guid.NewGuid(), Type = "bibliography",
        Content = JsonDocument.Parse(JsonSerializer.Serialize(new { text, style = "apa", title = "References" })),
    };

    [Fact]
    public void Text_and_BibTeX_in_blocks_become_entries_the_stored_ones_win()
    {
        var doc = Guid.NewGuid();
        var blocks = new[] { BibBlock(Lines), BibBlock(BibTeX + "\n@cite[sleep2] Duplicate.") };

        var entries = BibliographyBlocks.Unstored(doc, blocks, ["sleep2"]);

        entries.Select(e => e.CiteKey).Should().Equal("walker2017", "sleep1");
        entries[1].FormattedText.Should().Be("Author, A. — A paper on sleep. (Placeholder.)");
        entries.Should().OnlyContain(e => e.DocumentId == doc);
        BibliographyBlocks.Unstored(doc, blocks, ["sleep2"]).Select(e => e.Id).Should().Equal(entries.Select(e => e.Id), "an id fixed by document and key");
    }

    [Fact]
    public async Task The_bibliography_endpoint_and_the_document_list_them_marked_in_block()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Sleep paper");
        await SeedBlockAsync(doc.Id, "bibliography", JsonSerializer.Serialize(new { text = Lines, title = "References" }), 3);
        await SeedBibliographyEntryAsync(doc.Id, "stored2024", "article");

        var list = await Client.GetFromJsonAsync<List<BibliographyEntryDto>>($"/api/documents/{doc.Id}/bibliography");
        list!.Select(e => (e.CiteKey, e.InBlock)).Should().BeEquivalentTo([("stored2024", false), ("sleep1", true), ("sleep2", true)]);

        var res = await Client.GetAsync($"/api/documents/{doc.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<DocumentDto>();
        dto!.Bibliography.Select(e => (e.CiteKey, e.InBlock)).Should().BeEquivalentTo([("stored2024", false), ("sleep1", true), ("sleep2", true)]);
        dto.Bibliography.Single(e => e.CiteKey == "sleep1").FormattedText.Should().StartWith("Author, A.");
    }

    [Fact]
    public async Task Markdown_lists_them_resolves_Kind_label_and_natbib_citations()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Sleep");
        await SeedBlockAsync(doc.Id, "paragraph",
            JsonSerializer.Serialize(new { text = "As Table~(tab:sleep-recall) shows \\citep{sleep1} and \\citet{sleep2}." }), 0);
        await SeedBlockAsync(doc.Id, "bibliography", JsonSerializer.Serialize(new { text = Lines, title = "References" }), 1);
        await using var db = CreateDbContext();

        var md = await new RenderService(db, NullLogger<RenderService>.Instance).RenderToMarkdownAsync(doc.Id);

        md.Should().Contain("As Table [tab:sleep-recall](#tab:sleep-recall) shows [@sleep1] and [@sleep2].")
            .And.Contain("[sleep1]: Author, A. — A paper on sleep. (Placeholder.)")
            .And.Contain("[sleep2]: Author, B. — Another paper.")
            .And.NotContain("Table~(");
    }
}
