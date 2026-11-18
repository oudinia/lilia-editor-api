using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Converters;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The launch bot's Word import test, second half (18 Nov 2026, #189, #190, #193, #194, #195), on the bot's own files.
/// </summary>
public class WordImportBatch9bTests
{
    private static Task<ImportDocument> Parse(string name) =>
        new DocxParser(new OmmlToLatexConverter())
            .ParseAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "docx-corpus", "launch-bot", name + ".docx"));

    // #189: "FEMNIST" spans two columns and "Method" two rows; the cells under them aren't written.
    [Fact]
    public async Task Merged_cells_keep_their_spans()
    {
        var table = (await Parse("r3-merged")).Elements.OfType<ImportTable>().Single();
        var json = JsonSerializer.Serialize(JobService.ConvertTableToEditorFormat(table));
        using var doc = JsonDocument.Parse(json);
        var headers = doc.RootElement.GetProperty("headers");
        headers[0].GetProperty("content").GetString().Should().Be("Method");
        headers[0].GetProperty("rowspan").GetInt32().Should().Be(2);
        headers[1].GetProperty("content").GetString().Should().Be("FEMNIST");
        headers[1].GetProperty("colspan").GetInt32().Should().Be(2);
        headers.GetArrayLength().Should().Be(2);
        // The row under the header loses the cell "Method" covers.
        doc.RootElement.GetProperty("rows")[0].EnumerateArray().Select(c => c.GetString()).Should().Equal("k=1%", "k=10%");
    }

    [Fact]
    public void A_bold_cell_and_a_caption_are_kept()
    {
        var table = new ImportTable
        {
            HasHeaderRow = true,
            Caption = "Accuracy",
            Rows =
            [
                [new ImportTableCell { Text = "Method" }, new ImportTableCell { Text = "Acc" }],
                [new ImportTableCell { Text = "Ours" }, new ImportTableCell { Text = "82.9", Formatting = [new FormattingSpan(0, 4, FormattingType.Bold)] }],
            ],
        };
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(JobService.ConvertTableToEditorFormat(table)));
        doc.RootElement.GetProperty("rows")[0][1].GetString().Should().Be("**82.9**");
        doc.RootElement.GetProperty("caption").GetString().Should().Be("Accuracy");
    }

    // #190: the image's caption paragraph is its caption, once.
    [Fact]
    public async Task An_image_caption_is_said_once()
    {
        var doc = await Parse("r4-image");
        doc.Elements.OfType<ImportImage>().Single().Caption.Should().Be("A test curve.");
        doc.Elements.OfType<ImportParagraph>().Should().NotContain(p => p.Text.Contains("A test curve"));
    }

    [Fact]
    public void A_table_caption_paragraph_before_the_table_becomes_its_caption()
    {
        var elements = new List<ImportElement>
        {
            new ImportParagraph { Text = "Table 1. Top-1 accuracy." },
            new ImportTable(),
            new ImportParagraph { Text = "Body." },
        };
        DocxParser.PairCaptions(elements);
        elements.OfType<ImportTable>().Single().Caption.Should().Be("Top-1 accuracy.");
        elements.OfType<ImportParagraph>().Select(p => p.Text).Should().Equal("Body.");
    }

    // #193: the entries under "References" must survive the import.
    [Fact]
    public async Task References_entries_are_kept()
    {
        var doc = await Parse("r7-references");
        var texts = doc.Elements.Select(e => e switch
        {
            ImportParagraph p => p.Text,
            ImportBibliographyEntry b => b.Text,
            _ => null,
        }).Where(t => t is not null).ToList();
        texts.Should().Contain(t => t!.Contains("McMahan")).And.Contain(t => t!.Contains("Alistarh"));
    }

    // #194: level-1 items are children of the item before them.
    [Fact]
    public async Task Nested_list_items_keep_their_level()
    {
        var items = (await Parse("r8-nested-list")).Elements.OfType<ImportListItem>().ToList();
        var json = JsonSerializer.Serialize(JobService.NestListItems(items));
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(2);
        doc.RootElement[0].GetProperty("text").GetString().Should().Be("outer one");
        doc.RootElement[0].GetProperty("children").EnumerateArray().Select(c => c.GetString()).Should().Equal("inner a", "inner b");
        doc.RootElement[1].GetString().Should().Be("outer two");
    }

    // #195: Author and Date go to the title block; the "Abstract" label isn't a section.
    [Fact]
    public async Task Author_and_date_make_the_title_block_and_the_abstract_label_goes()
    {
        var doc = await Parse("r9-title");
        doc.Metadata.SourceAuthor.Should().Be("Amina Rahman");
        doc.Metadata.SourceDate.Should().Be("March 2026");
        doc.Elements.OfType<ImportParagraph>().Should().NotContain(p => p.Text.Contains("Amina") || p.Text.Contains("March 2026"));
        doc.Elements.OfType<ImportHeading>().Should().NotContain(h => h.Text.Trim() == "Abstract");

        var blocks = JobService.WithWordTitleBlock([], "A Title", doc.Metadata);
        var title = blocks.Single();
        title.Type.Should().Be("title");
        title.Content.GetProperty("author").GetString().Should().Be("Amina Rahman");
        title.Content.GetProperty("date").GetString().Should().Be("March 2026");
    }

    [Fact]
    public void A_word_file_without_author_or_date_gets_no_title_block()
        => JobService.WithWordTitleBlock([], "T", new ImportMetadata()).Should().BeEmpty();
}
