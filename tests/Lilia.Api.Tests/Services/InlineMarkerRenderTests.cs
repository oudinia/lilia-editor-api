using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Inline-marker rendering tests — companion to the Node smoke harness
/// at <c>scripts/smoke-features.mjs</c>. Where the smoke script
/// black-box-tests the full HTTP path (preview/latex + export/pdf),
/// these tests white-box the render services directly via Moq, no DB
/// or HTTP needed.
///
/// Each marker / variant has its own region. Add a region when you
/// add a new feature; add a row to the smoke recipes for the
/// end-to-end gate. The two layers catch different things:
///
///   smoke  → regressions in the wiring between editor + API + LaTeX.
///   xunit  → regressions in the regex / escape ordering INSIDE the
///            render service. Faster (~ms), no API spin-up.
///
/// Style: lean on `RenderBlockToLatex` for paragraph-shaped tests since
/// it routes through ProcessLatexText — same code path the LaTeX
/// preview pane hits.
/// </summary>
public class InlineMarkerRenderTests
{
    private readonly RenderService _sut;

    public InlineMarkerRenderTests()
    {
        var logger = new Mock<ILogger<RenderService>>();
        _sut = new RenderService(null!, logger.Object);
    }

    private string Render(string text)
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "paragraph",
            Content = JsonDocument.Parse(JsonSerializer.Serialize(new { text })),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        return _sut.RenderBlockToLatex(block);
    }

    private string RenderHeading(string text, int level)
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "heading",
            Content = JsonDocument.Parse(JsonSerializer.Serialize(new { text, level })),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        return _sut.RenderBlockToLatex(block);
    }

    #region Comment marker

    [Fact]
    public void Comment_inline_emits_iffalse_fi()
    {
        var latex = Render("Keep [%muted%] here.");
        latex.Should().Contain(@"\iffalse muted\fi");
        latex.Should().NotContain("[%muted%]");
        latex.Should().NotContain(@"[\%"); // bracket-with-escaped-% bug from 2026-05-14
    }

    [Fact]
    public void Comment_multiline_stays_inline_iffalse_not_begin_comment()
    {
        // \begin{comment}…\end{comment} errors when mid-paragraph
        // (verbatim env requires line-start). \iffalse…\fi works
        // for both inline AND multi-line. Discovered via the
        // smoke "comment marker multi-line" recipe, 2026-05-14.
        var latex = Render("Before [%line1\nline2\nline3%] after");
        latex.Should().Contain(@"\iffalse line1");
        latex.Should().NotContain(@"\begin{comment}");
    }

    [Fact]
    public void Comment_inside_heading_routes_through_ProcessLatexText()
    {
        // Heading rendering was using bare EscapeLatex which turned
        // [%foo%] into [\%foo\%] (brackets visible in PDF). Switched
        // to ProcessLatexText so the marker translation runs before
        // escape. 2026-05-14.
        var latex = RenderHeading("[%hidden%]", level: 2);
        latex.Should().Contain(@"\subsection{\iffalse hidden\fi");
        latex.Should().NotContain(@"[\%");
    }

    #endregion

    #region Smart quotes

    [Fact]
    public void Straight_quote_pair_converts_to_double_back_apostrophe()
    {
        var latex = Render("She said \"hello\" to me.");
        latex.Should().Contain("``hello''");
    }

    [Fact]
    public void Smart_quotes_do_not_fire_inside_inline_code()
    {
        // The 2026-05-14 fix: inline code is extracted to a
        // placeholder BEFORE the smart-quote pass so straight
        // quotes inside `…` reach the output verbatim.
        var latex = Render("Say `console.log(\"hi\")` to debug.");
        latex.Should().Contain("\\texttt{console.log(\"hi\")}");
        latex.Should().NotContain("console.log(``hi");
    }

    [Fact]
    public void Curly_quote_chars_convert_to_LaTeX_form()
    {
        var latex = Render("“hello” and ‘world’");
        latex.Should().Contain("``hello''");
        latex.Should().Contain("`world'");
    }

    #endregion

    #region Inline-styling commands (FormatRail picker)

    [Fact]
    public void Textcolor_command_survives_escape_pass()
    {
        var latex = Render("Plain \\textcolor{red}{redword} text.");
        latex.Should().Contain(@"\textcolor{red}{redword}");
    }

    [Fact]
    public void Hl_colored_form_survives_escape_pass()
    {
        var latex = Render("Marked \\hl[blue]{phrase} here.");
        latex.Should().Contain(@"\hl[blue]{phrase}");
    }

    [Fact]
    public void Size_group_form_survives_escape_pass()
    {
        var latex = Render("Big {\\LARGE word} small.");
        latex.Should().Contain(@"{\LARGE word}");
    }

    #endregion

    #region LML markers (sup/sub/smallcaps/strike)

    [Theory]
    [InlineData("x^2^", @"\textsuperscript{2}")]
    [InlineData("H%%2%%O", @"\textsubscript{2}")]
    [InlineData("^^NASA^^", @"\textsc{NASA}")]
    [InlineData("~~struck~~", @"\st{struck}")]
    [InlineData("==marked==", @"\hl{marked}")]
    public void LML_marker_converts_to_LaTeX(string input, string expected)
    {
        var latex = Render(input);
        latex.Should().Contain(expected);
    }

    [Fact]
    public void Smallcaps_does_not_eat_superscript()
    {
        // `^^NASA^^` MUST match before single-caret `^…^`, otherwise
        // sup steals the leading `^` and produces garbage.
        var latex = Render("Read ^^NASA^^ at x^2^ now.");
        latex.Should().Contain(@"\textsc{NASA}");
        latex.Should().Contain(@"\textsuperscript{2}");
    }

    #endregion

    #region Spacing primitives (PR 2)

    [Theory]
    [InlineData(@"a\hspace{1em}b", @"\hspace{1em}")]
    [InlineData(@"a\hspace{2em}b", @"\hspace{2em}")]
    [InlineData(@"a\hfill{}b", @"\hfill")]
    [InlineData(@"a\vspace{2em}b", @"\vspace{2em}")]
    [InlineData(@"a\smallskip{}b", @"\smallskip")]
    [InlineData(@"a\medskip{}b", @"\medskip")]
    [InlineData(@"a\bigskip{}b", @"\bigskip")]
    [InlineData(@"a\vfill{}b", @"\vfill")]
    public void Spacing_command_passes_through(string input, string expected)
    {
        var latex = Render(input);
        latex.Should().Contain(expected);
    }

    [Fact]
    public void Hfill_with_empty_braces_does_not_swallow_following_word()
    {
        // `\hfill is` would normalise to `\hfillis` in storage and
        // break compile ("undefined control sequence"). The atom-node
        // serialiser uses `\hfill{}` as the safer terminator.
        var latex = Render("Left\\hfill{}Right");
        latex.Should().Contain(@"\hfill");
        latex.Should().NotContain(@"\hfillRight");
    }

    #endregion

    #region Markdown markers (still safe after the inline-code refactor)

    [Theory]
    [InlineData("**bold**", @"\textbf{bold}")]
    [InlineData("*italic*", @"\textit{italic}")]
    [InlineData("__underline__", @"\underline{underline}")]
    public void Markdown_markers_convert(string input, string expected)
    {
        var latex = Render(input);
        latex.Should().Contain(expected);
    }

    #endregion

    #region Quotation variants (PR 3) — exercised via the block path

    [Fact]
    public void Blockquote_simple_emits_quote_env()
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "blockquote",
            Content = JsonDocument.Parse("""{"text":"To be or not to be","variant":"simple"}"""),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\begin{quote}");
        latex.Should().Contain(@"\end{quote}");
        latex.Should().NotContain(@"\epigraph");
        latex.Should().NotContain(@"\begin{verse}");
    }

    [Fact]
    public void Blockquote_epigraph_emits_epigraph_command()
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "blockquote",
            Content = JsonDocument.Parse("""{"text":"Imagination is more important than knowledge.","variant":"epigraph","attribution":"Einstein"}"""),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\epigraph{");
        latex.Should().Contain("Einstein");
        latex.Should().NotContain(@"\begin{quote}");
    }

    [Fact]
    public void Blockquote_verse_preserves_line_breaks_with_double_backslash()
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "blockquote",
            Content = JsonDocument.Parse("""{"text":"Roses are red\nViolets are blue","variant":"verse"}"""),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\begin{verse}");
        latex.Should().Contain(@"\\"); // line-end marker between verse lines
        latex.Should().Contain("Roses are red");
        latex.Should().Contain("Violets are blue");
    }

    #endregion

    #region Callout color override (PR 4)

    [Fact]
    public void Callout_without_color_emits_plain_tcolorbox()
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "callout",
            Content = JsonDocument.Parse("""{"variant":"note","title":"Heads up","text":"Body text"}"""),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\begin{tcolorbox}[title={Heads up}]");
        latex.Should().NotContain("colback="); // no override
    }

    [Fact]
    public void Callout_with_color_emits_colback_colframe()
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "callout",
            Content = JsonDocument.Parse("""{"variant":"note","title":"Heads","text":"Body","color":"ForestGreen"}"""),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("colback=ForestGreen!10!white");
        latex.Should().Contain("colframe=ForestGreen!75!black");
        latex.Should().Contain("coltitle=white");
    }

    #endregion
}
