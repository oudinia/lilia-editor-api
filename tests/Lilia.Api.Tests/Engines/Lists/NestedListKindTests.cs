using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Lists;

/// <summary>
/// A bullet list under a numbered point stays bullets (API #483, 11 Nov 2026).
///
/// <para>A list block had one <c>ordered</c> for every level, so a pasted answer's
/// bullet sub-points printed as a numbered list, and a LaTeX <c>itemize</c> inside an
/// <c>enumerate</c> came back as two enumerates. The item now carries
/// <c>childrenOrdered</c> when its sub-list is of the other kind; the importers write it
/// and every export reads it.</para>
/// </summary>
public class NestedListKindTests
{
    private static RenderService Render() => new(null!, NullLogger<RenderService>.Instance);

    private static TypstRenderService Typst() =>
        new(new LiliaDbContext(new DbContextOptionsBuilder<LiliaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
            NullLogger<TypstRenderService>.Instance);

    private static Block List(object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "list",
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    /// <summary>1. First step (• a detail, • another detail) 2. Second step.</summary>
    private static Block BulletsUnderNumbers() => List(new
    {
        ordered = true,
        items = new object[]
        {
            new { text = "First step", children = new[] { "a detail", "another detail" }, childrenOrdered = false },
            "Second step",
        },
    });

    private static string Squash(string s) => string.Join("\n", s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    [Fact]
    public void LaTeX_prints_an_itemize_inside_the_enumerate()
    {
        Squash(Render().RenderBlockToLatex(BulletsUnderNumbers())).Should().Contain(
            "\\begin{enumerate}\n\\item First step\n\\begin{itemize}\n\\item a detail\n\\item another detail\n\\end{itemize}\n\\item Second step\n\\end{enumerate}");
    }

    [Fact]
    public void LaTeX_without_childrenOrdered_keeps_the_parent_kind()
    {
        var latex = Render().RenderBlockToLatex(List(new { ordered = true, items = new object[] { new { text = "One", children = new[] { "one.a" } } } }));
        latex.Should().NotContain("itemize");
        Regex(latex, @"\\begin\{enumerate\}").Should().Be(2);
    }

    [Fact]
    public void LaTeX_prints_numbers_under_bullets()
    {
        var latex = Squash(Render().RenderBlockToLatex(List(new
        {
            ordered = false,
            items = new object[] { new { text = "Point", children = new[] { "step one" }, childrenOrdered = true } },
        })));
        latex.Should().Contain("\\begin{itemize}\n\\item Point\n\\begin{enumerate}\n\\item step one\n\\end{enumerate}\n\\end{itemize}");
    }

    [Fact]
    public void A_description_list_keeps_description_for_its_sub_list_unless_the_item_says()
    {
        var latex = Render().RenderBlockToLatex(List(new
        {
            kind = "description",
            items = new object[] { new { text = "Term", description = "Body", children = new[] { "sub" }, childrenOrdered = false } },
        }));
        latex.Should().Contain("\\begin{itemize}");
    }

    [Fact]
    public void HTML_nests_a_ul_in_the_ol()
    {
        var html = Render().RenderBlockToHtml(BulletsUnderNumbers());
        html.Should().Contain("<ol").And.Contain("<li>First step<ul><li>a detail</li><li>another detail</li></ul></li>");
    }

    [Fact]
    public void Markdown_writes_bullets_under_the_numbers()
    {
        var md = Render().RenderBlockToMarkdown(BulletsUnderNumbers());
        md.Should().Contain("1. First step\n  - a detail\n  - another detail\n2. Second step");
    }

    [Fact]
    public void Lilia_text_writes_bullets_under_the_numbers()
    {
        var lml = Render().RenderBlockToLml(BulletsUnderNumbers());
        lml.Should().Contain("1. First step").And.Contain("  - a detail").And.Contain("2. Second step");
    }

    [Fact]
    public void Typst_writes_bullets_under_the_numbers()
    {
        var typ = Typst().RenderBlockToTypst(BulletsUnderNumbers());
        typ.Should().Contain("+ First step\n  - a detail\n  - another detail\n+ Second step");
    }

    [Fact]
    public void The_LaTeX_export_prints_an_itemize_inside_the_enumerate()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Lists", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
            LatexDocumentClass = "article",
        };
        var tex = Squash(new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, [BulletsUnderNumbers()], [], new LaTeXExportOptions()));
        tex.Should().Contain("\\item First step\n\\begin{itemize}\n\\item a detail");
    }

    [Fact]
    public async Task Word_numbers_the_points_and_bullets_the_sub_points()
    {
        var block = new ExportBlock
        {
            Type = "list",
            Content = new ExportBlockContent
            {
                ListType = "ordered",
                Items =
                [
                    new ExportListItem { Text = "First step", ChildrenOrdered = false, Children = [new ExportListItem { Text = "a detail" }] },
                    new ExportListItem { Text = "Second step" },
                ],
            },
        };
        var bytes = await new DocxExportService().ExportAsync(new ExportDocument { Title = "T", Blocks = [block] });

        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var numIds = XDocument.Parse(reader.ReadToEnd()).Descendants(w + "p")
            .Select(p => (Text: string.Concat(p.Descendants(w + "t").Select(t => t.Value)),
                          NumId: p.Descendants(w + "numId").Select(n => n.Attribute(w + "val")!.Value).FirstOrDefault()))
            .Where(p => p.NumId != null)
            .ToDictionary(p => p.Text, p => p.NumId);

        numIds["a detail"].Should().Be("1", "numbering 1 is the bullets");
        numIds["First step"].Should().NotBe("1").And.Be(numIds["Second step"]);
    }

    [Fact]
    public async Task A_LaTeX_import_keeps_the_itemize_inside_the_enumerate()
    {
        var doc = await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n\\begin{document}\n\\begin{enumerate}\n\\item First step\n\\begin{itemize}\n\\item a detail\n\\end{itemize}\n\\item Second step\n\\end{enumerate}\n\\end{document}\n");
        var list = JsonSerializer.SerializeToElement(LatexImportJobExecutor.MapElements(doc.Elements).Single(b => b.type == "list").content);

        list.GetProperty("ordered").GetBoolean().Should().BeTrue();
        var first = list.GetProperty("items")[0];
        first.GetProperty("childrenOrdered").GetBoolean().Should().BeFalse();
        first.GetProperty("children")[0].GetString().Should().Be("a detail");
    }

    [Fact]
    public void A_pasted_Markdown_answer_keeps_its_bullet_sub_points()
    {
        var elements = MarkdownTextParser.Parse("1. First step\n   - a detail\n   - another detail\n2. Second step\n");
        var list = JsonSerializer.SerializeToElement(LatexImportJobExecutor.MapElements(elements).Single(b => b.type == "list").content);

        list.GetProperty("items")[0].GetProperty("childrenOrdered").GetBoolean().Should().BeFalse();
        // The same kind writes nothing: the shape stays as it was.
        var same = JsonSerializer.SerializeToElement(LatexImportJobExecutor.MapElements(MarkdownTextParser.Parse("- a\n  - b\n")).Single().content);
        same.GetProperty("items")[0].TryGetProperty("childrenOrdered", out _).Should().BeFalse();
    }

    private static int Regex(string s, string pattern) => System.Text.RegularExpressions.Regex.Matches(s, pattern).Count;
}
