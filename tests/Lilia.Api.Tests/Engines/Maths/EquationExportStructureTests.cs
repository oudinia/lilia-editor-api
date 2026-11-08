using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Converters;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Engines.Maths;

/// <summary>
/// The launch bot's issue 44 (8 Nov 2026): seven equations made in the maths editor, each exported to
/// PDF, LaTeX, Word, HTML and Markdown. The PDF and LaTeX passed; five cells failed:
/// <list type="bullet">
/// <item>HTML and Markdown: an align block's rows went between <c>$$</c> without their environment
/// (KaTeX showed them as a red error);</item>
/// <item>Word: a ‖ matrix had single bars (the converter lower-cased <c>Vmatrix</c>), cases took a
/// closing brace, and a numbered two-row align printed one number.</item>
/// </list>
/// </summary>
public class EquationExportStructureTests
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly RenderService Render = new(
        new LiliaDbContext(new DbContextOptionsBuilder<LiliaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        NullLogger<RenderService>.Instance);

    private static JsonElement Content(string source, string mode) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["source"] = source, ["equationMode"] = mode });

    private static Block Equation(string source, string mode) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "equation", SortOrder = 0,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(new { source, equationMode = mode })),
    };

    // ── HTML and Markdown: an align keeps its environment ─────────────────────

    [Theory]
    [InlineData("align")]
    [InlineData("align*")]
    [InlineData("gather")]
    [InlineData("multline")]
    public void A_multi_line_mode_is_wrapped_in_its_own_environment_for_html_and_markdown(string mode)
    {
        EquationContent.ReadDisplaySource(Content(@"x &= 1 \\ y &= 2", mode))
            .Should().Be($@"\begin{{{mode}}}x &= 1 \\ y &= 2\end{{{mode}}}");
    }

    [Fact]
    public void The_html_export_gives_katex_an_align_not_bare_rows()
    {
        var html = Render.RenderBlockToHtml(Equation(@"x &= 1 \\ y &= 2", "align"));

        html.Should().Contain(@"$$\begin{align}x &amp;= 1 \\ y &amp;= 2\end{align}$$");
    }

    [Fact]
    public void The_markdown_export_keeps_the_align_environment()
    {
        Render.RenderBlockToMarkdown(Equation(@"x &= 1 \\ y &= 2", "align"))
            .Should().Contain(@"\begin{align}x &= 1 \\ y &= 2\end{align}").And.StartWith("$$");
    }

    [Fact]
    public void Display_source_leaves_matrices_single_lines_and_written_environments_alone()
    {
        EquationContent.ReadDisplaySource(Content(@"1 & 2", "Vmatrix")).Should().Be(@"\begin{Vmatrix}1 & 2\end{Vmatrix}");
        EquationContent.ReadDisplaySource(Content(@"E = mc^2", "display")).Should().Be(@"E = mc^2");
        EquationContent.ReadDisplaySource(Content(@"\begin{align}a &= b\end{align}", "align"))
            .Should().Be(@"\begin{align}a &= b\end{align}");
    }

    // ── Word: delimiters ─────────────────────────────────────────────────────

    private static (string? Beg, string? End) Delimiters(string latex)
    {
        var (omml, ok, _) = new LatexToOmmlConverter().Convert(latex);
        ok.Should().BeTrue();
        var dPr = XElement.Parse($"<r xmlns:m=\"{M}\">{omml}</r>").Descendants(M + "dPr").First();
        return (dPr.Element(M + "begChr")?.Attribute(M + "val")?.Value, dPr.Element(M + "endChr")?.Attribute(M + "val")?.Value);
    }

    [Theory]
    [InlineData("pmatrix", "(", ")")]
    [InlineData("bmatrix", "[", "]")]
    [InlineData("Bmatrix", "{", "}")]
    [InlineData("vmatrix", "|", "|")]
    [InlineData("Vmatrix", "‖", "‖")]
    public void Each_matrix_keeps_its_own_delimiters_in_word(string env, string beg, string end)
    {
        Delimiters($@"\begin{{{env}}}a & b \\ c & d\end{{{env}}}").Should().Be((beg, end));
    }

    [Fact]
    public void Cases_have_a_left_brace_and_no_right_one_in_word()
    {
        Delimiters(@"\begin{cases}x & x \ge 0 \\ -x & x < 0 \\ 0 & \text{never}\end{cases}").Should().Be(("{", ""));
    }

    // ── Word: a number per row ───────────────────────────────────────────────

    [Fact]
    public void Rows_split_at_top_level_breaks_only_and_lose_their_nonumber()
    {
        EquationContent.SplitRows(@"x &= \begin{pmatrix}1 \\ 2\end{pmatrix} \\ y &= 2 \nonumber \\ z &= 3 \\")
            .Should().Equal((@"x &= \begin{pmatrix}1 \\ 2\end{pmatrix}", true), ("y &= 2", false), ("z &= 3", true));
    }

    [Fact]
    public void A_numbered_align_gets_one_number_per_row_for_word()
    {
        var rows = DocumentExportService.NumberedRows(Content(@"x &= 1 \\ y &= 2", "align"), @"x &= 1 \\ y &= 2", "7");

        rows.Should().NotBeNull();
        rows!.Select(r => r.Number).Should().Equal("7", "8");
        rows.Select(r => r.Latex).Should().Equal(@"\begin{aligned}x &= 1\end{aligned}", @"\begin{aligned}y &= 2\end{aligned}");
    }

    [Theory]
    [InlineData("align*", "7")]       // unnumbered
    [InlineData("align", null)]       // no number given
    [InlineData("bmatrix", "7")]      // a matrix's rows are one equation
    public void Other_blocks_stay_one_paragraph(string mode, string? number)
    {
        DocumentExportService.NumberedRows(Content(@"x & 1 \\ y & 2", mode), mode == "bmatrix"
            ? @"\begin{bmatrix}x & 1 \\ y & 2\end{bmatrix}" : @"x & 1 \\ y & 2", number).Should().BeNull();
    }

    [Fact]
    public async Task The_word_file_prints_both_numbers_of_a_two_row_align()
    {
        var block = new ExportBlock
        {
            Type = "equation",
            Content = new ExportBlockContent
            {
                Latex = @"\begin{aligned}x &= 1 \\ y &= 2\end{aligned}", DisplayMode = true, EquationNumber = "7",
                EquationRows =
                [
                    new() { Latex = @"\begin{aligned}x &= 1\end{aligned}", Number = "7" },
                    new() { Latex = @"\begin{aligned}y &= 2\end{aligned}", Number = "8" },
                ],
            },
        };

        var bytes = await new DocxExportService(new LatexToOmmlConverter()).ExportAsync(new ExportDocument { Title = "T", Blocks = [block] });

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var body = XDocument.Parse(reader.ReadToEnd());
        var numbered = body.Descendants(W + "p").Where(p => p.Descendants(M + "oMath").Any()).ToList();
        numbered.Should().HaveCount(2);
        numbered.Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value))).Should().Equal("(7)", "(8)");
    }
}
