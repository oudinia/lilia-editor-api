using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP hardening T2 (cloud session's handoff, 12 Oct): content given to Lilia that never reached
/// the PDF. One region per issue; each repro is the campaign's own input.
/// </summary>
public class ContentLossTests
{
    private readonly RenderService _render = new(null!, NullLogger<RenderService>.Instance);
    private readonly LmlTextParser _parser = new();

    private static Block B(string type, object content, int order = 0) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type, SortOrder = order,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private string Render(string type, object content) => _render.RenderBlockToLatex(B(type, content));

    private string RenderDoc(string documentClass, params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = documentClass,
            Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList(),
            BibliographyEntries = new List<BibliographyEntry>(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        foreach (var b in doc.Blocks) b.Document = doc;
        return _render.RenderToLatex(doc);
    }

    private static string Json(object? o) => JsonSerializer.Serialize(o, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // ── MCP-11 ──────────────────────────────────────────────────────────
    [Fact]
    public void Bracket_maths_is_protected_like_dollar_maths()
    {
        var latex = Render("paragraph", new { text = @"Euler: \(e^{i\pi}+1=0\) and \(a^2+b^2\). Display: \[ a_1 = x^2 \]" });
        latex.Should().Contain(@"\(e^{i\pi}+1=0\)").And.Contain(@"\(a^2+b^2\)").And.Contain(@"\[ a_1 = x^2 \]");
        latex.Should().NotContain("textsuperscript").And.NotContain(@"a\_1").And.NotContain("textasciicircum");
    }

    // ── MCP-10 ──────────────────────────────────────────────────────────
    [Fact]
    public void An_equation_keeps_its_lines_so_a_comment_does_not_swallow_the_rest()
    {
        var r = _parser.Parse("@equation[mode=display]\n  x = 1 + 2 % note\n  + 3");
        Json(r.Blocks[0].Content).Should().Contain(@"x = 1 + 2 % note\n+ 3");
    }

    [Fact]
    public void A_verse_keeps_its_lines()
    {
        var r = _parser.Parse("@quote[variant=verse]\n  Line one,\n  line two,\n  line three.");
        Json(r.Blocks[0].Content).Should().Contain(@"Line one,\nline two,\nline three.");
    }

    [Fact]
    public void Prose_still_joins_soft_wraps()
    {
        var r = _parser.Parse("@paragraph\n  One phrase\n  per line.");
        Json(r.Blocks[0].Content).Should().Contain("One phrase per line.");
    }

    // ── MCP-12 ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData(@"| norm | $\|x\|$ |", new[] { "norm", @"$\|x\|$" })]
    [InlineData(@"| a \| b | c |", new[] { "a | b", "c" })]
    [InlineData("| `x|y` | z |", new[] { "`x|y`", "z" })]
    [InlineData("| a | b | c |", new[] { "a", "b", "c" })]
    public void A_bar_splits_cells_only_outside_maths_and_code(string row, string[] cells) =>
        LmlTextParser.SplitTableCells(row).Should().Equal(cells);

    // ── MCP-13 ──────────────────────────────────────────────────────────
    [Fact]
    public void A_backslash_in_a_quoted_value_is_kept()
    {
        var r = _parser.Parse("@figure[caption=\"Significance at $\\alpha = 0.05$\", src=\"a.png\"]");
        Json(r.Blocks[0].Content).Should().Contain(@"$\\alpha = 0.05$");
    }

    // ── MCP-59 ──────────────────────────────────────────────────────────
    [Fact]
    public void A_bracket_inside_a_quoted_title_does_not_end_the_header()
    {
        var r = _parser.Parse("@theorem[title=\"Completeness of $C[0,1]$\", label=thm:c]\n  The space is complete.");
        r.Blocks.Should().ContainSingle();
        r.Blocks[0].Type.Should().Be("theorem");
        Json(r.Blocks[0].Content).Should().Contain("Completeness of $C[0,1]$").And.Contain("thm:c");
    }

    [Fact]
    public void A_header_that_cannot_be_read_says_so()
    {
        var r = _parser.Parse("@theorem[title=\"unclosed, label=x\n  Body.");
        r.Warnings.Should().Contain(w => w.Contains("couldn't read the attributes"));
    }

    // ── MCP-04 ──────────────────────────────────────────────────────────
    [Fact]
    public void References_written_in_a_bibliography_block_are_printed_and_cited()
    {
        var latex = RenderDoc("article",
            B("paragraph", new { text = "As in @cite{knuth1984}." }),
            B("bibliography", new { text = "@cite[knuth1984] Knuth, D. — The TeXbook\n@cite[lamport94] Lamport, L. — LaTeX" }));
        latex.Should().Contain(@"\cite{knuth1984}").And.Contain(@"\bibitem{knuth1984}");
        // Uncited, so left out, as BibTeX leaves it out of the export (MCP-90).
        latex.Should().NotContain(@"\bibitem{lamport94}");
        latex.Should().Contain("The TeXbook");
        new EmittedLatexAuditProbe(latex).MissingCitations.Should().BeEmpty();
    }

    // ── MCP-40 ──────────────────────────────────────────────────────────
    [Fact]
    public void A_float_inside_balanced_columns_is_kept_in_place()
    {
        RenderService.InMulticols("\\begin{table*}[t]\n\\centering x\n\\end{table*}")
            .Should().Be("\\begin{table}[H]\n\\centering x\n\\end{table}");
        RenderService.InMulticols("\\begin{figure}\nx\n\\end{figure}").Should().StartWith(@"\begin{figure}[H]");
    }

    // ── MCP-41 ──────────────────────────────────────────────────────────
    [Fact]
    public void A_books_front_and_back_matter_print_with_its_divisions()
    {
        var latex = RenderDoc("book",
            B("frontMatter", new { subType = "preface", text = "Why this book." }),
            B("heading", new { text = "Start", level = 1 }),
            B("paragraph", new { text = "Body." }),
            B("backMatter", new { subType = "afterword", text = "Thanks for reading." }));
        latex.Should().Contain(@"\frontmatter").And.Contain(@"\chapter*{Preface}").And.Contain("Why this book.");
        latex.Should().Contain(@"\mainmatter").And.Contain(@"\backmatter").And.Contain(@"\chapter*{Afterword}").And.Contain("Thanks for reading.");
        latex.IndexOf(@"\frontmatter").Should().BeLessThan(latex.IndexOf(@"\mainmatter"));
        latex.IndexOf(@"\mainmatter").Should().BeLessThan(latex.IndexOf(@"\backmatter"));
        latex.Should().NotContain("Unknown block type");
    }

    [Fact]
    public void In_an_article_front_matter_is_a_starred_section()
    {
        Render("frontMatter", new { subType = "acknowledgements", text = "Thanks." })
            .Should().Contain(@"\section*{Acknowledgements}").And.Contain("Thanks.");
    }

    /// <summary>The audit, from the honest-check work, when it is on main; a local stand-in otherwise.</summary>
    private sealed class EmittedLatexAuditProbe(string source)
    {
        public IEnumerable<string> MissingCitations
        {
            get
            {
                var cites = System.Text.RegularExpressions.Regex.Matches(source, @"\\cite\{([^}]+)\}").SelectMany(m => m.Groups[1].Value.Split(',')).Select(k => k.Trim());
                var items = System.Text.RegularExpressions.Regex.Matches(source, @"\\bibitem(?:\[[^\]]*\])?\{([^}]+)\}").Select(m => m.Groups[1].Value).ToHashSet();
                return cites.Where(c => !items.Contains(c));
            }
        }
    }
}
