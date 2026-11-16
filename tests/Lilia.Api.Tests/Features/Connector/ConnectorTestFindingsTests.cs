using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Features.Export.Publishers;
using Lilia.Core.Entities;
using Lilia.Engines.Publishers;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// What the connector test of 17 Nov 2026 found (a paper written, revised, formatted for IEEE and exported through the
/// Lilia connector): a label on an align's second row moved to its first, a TikZ figure reported as "has no image",
/// and a Markdown file's own heading taken for section 1 with the file's name as the title.
/// </summary>
public class ConnectorTestFindingsTests
{
    [Fact]
    public async Task A_label_on_a_later_row_of_an_align_stays_on_its_row()
    {
        var doc = await new LatexParser().ParseTextAsync("""
            \begin{align}
              x &= 1 \\
              y &= 2 \label{eq:second}
            \end{align}
            """);
        var eq = doc.Elements.OfType<ImportEquation>().Single();
        eq.Label.Should().BeNull("the label numbers the second row, not the block");
        eq.LatexContent.Should().Contain(@"y &= 2 \label{eq:second}");
    }

    [Fact]
    public async Task A_label_on_an_aligns_first_row_or_a_single_equation_is_the_blocks()
    {
        var align = (await new LatexParser().ParseTextAsync("\\begin{align}\n  a &= b \\label{eq:a}\\\\\n  c &= d\n\\end{align}"))
            .Elements.OfType<ImportEquation>().Single();
        align.Label.Should().Be("eq:a");
        var single = (await new LatexParser().ParseTextAsync("\\begin{equation}\n  E = mc^2 \\label{eq:e}\n\\end{equation}"))
            .Elements.OfType<ImportEquation>().Single();
        single.Label.Should().Be("eq:e");
    }

    [Fact]
    public void A_TikZ_figure_is_not_reported_as_missing_its_image()
    {
        var p = PublisherProfiles.Find("ieee-conference")!;
        var doc = PublisherClassTests.Doc(p.DocumentClass, p.ClassOptions, "Ada Lovelace", new[] { "LaTeX" });
        doc.PublisherProfile = p.Id;
        var tikz = PublisherClassTests.B("figure", new { kind = "tikz", source = @"\begin{tikzpicture}\draw (0,0) -- (1,1);\end{tikzpicture}", caption = "A line", label = "fig:line" });
        var empty = PublisherClassTests.B("figure", new { caption = "No image yet" });
        foreach (var b in new[] { tikz, empty }) { b.SortOrder = doc.Blocks.Count; b.Document = doc; b.DocumentId = doc.Id; doc.Blocks.Add(b); }
        var latex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());

        var missing = PublisherCompliance.Check(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), latex).Findings
            .Where(f => f.Code == "figure_missing").Select(f => f.BlockId).ToList();
        missing.Should().Equal(empty.Id.ToString());
    }

    [Fact]
    public async Task A_markdown_file_opening_with_its_only_level_1_heading_takes_it_as_the_title()
    {
        var pasted = PastedText.WithLeadingH1AsTitle(await PastedText.ParseAsync(
            "# My paper\n\nSome text.\n\n## Method\n\nMore.", "MARKDOWN", new LatexParser(), null!, true, (_, _) => { }));
        pasted.Title.Should().Be("My paper");
        pasted.TitleBlock.Should().NotBeNull();
        JsonSerializer.Serialize(pasted.TitleBlock!.Content).Should().Contain("My paper");
        pasted.Blocks.Should().NotContain(b => b.Type == "heading" && JsonSerializer.Serialize(b.Content).Contains("My paper"));
        pasted.Blocks.Should().Contain(b => b.Type == "heading" && JsonSerializer.Serialize(b.Content).Contains("Method"));
    }

    [Theory]
    [InlineData("Intro text first.\n\n# Section A\n\nText.")]
    [InlineData("# Part one\n\nText.\n\n# Part two\n\nText.")]
    public async Task A_markdown_file_whose_level_1_headings_are_sections_keeps_them(string md)
    {
        var parsed = await PastedText.ParseAsync(md, "MARKDOWN", new LatexParser(), null!, true, (_, _) => { });
        PastedText.WithLeadingH1AsTitle(parsed).Should().BeSameAs(parsed);
    }
}
