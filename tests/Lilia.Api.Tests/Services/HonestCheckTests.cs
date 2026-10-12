using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP hardening T1 (cloud session's handoff, 12 Oct): a clean check_document must mean a correct PDF.
/// Labels the compile emitter dropped (equations, listings, in-place TikZ), references judged from
/// the emitted LaTeX, and compile errors that say what they are.
/// </summary>
public class HonestCheckTests
{
    private readonly RenderService _render = new(null!, new Mock<ILogger<RenderService>>().Object);

    private string Render(string type, object content) => _render.RenderBlockToLatex(new Block
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    });

    [Theory]
    [InlineData("eq:loss", @"\label{eq:loss}")]
    [InlineData("main", @"\label{eq:main}")] // a bare name gets the exporter's prefix (LabelKey)
    public void A_numbered_equation_prints_its_label(string label, string expected)
    {
        var latex = Render("equation", new { latex = "E = mc^2", equationMode = "display", label });
        latex.Should().Contain(expected).And.Contain(@"\begin{equation}");
    }

    [Fact]
    public void An_unnumbered_equation_has_no_label_and_an_align_gets_it_inside()
    {
        Render("equation", new { latex = "x = 1", equationMode = "display", label = "eq:x", numbered = false })
            .Should().NotContain(@"\label");
        var align = Render("equation", new { latex = @"\begin{align} a &= b \\ c &= d \end{align}", equationMode = "display", label = "eq:sys" });
        align.Should().Contain(@"\begin{align}" + "\n" + @"\label{eq:sys}");
    }

    [Fact]
    public void A_code_listing_prints_its_label()
    {
        Render("code", new { code = "print(1)", language = "python", caption = "Hello", label = "lst:hello" })
            .Should().Contain("label={lst:hello}");
    }

    [Fact]
    public void A_tikz_figure_in_place_keeps_its_caption_and_label()
    {
        var content = JsonDocument.Parse("""{"kind":"tikz","source":"\\begin{tikzpicture}\\draw (0,0)--(1,1);\\end{tikzpicture}","caption":"A line","float":false}""").RootElement;
        var latex = TikzFigure.ToLatex(content, "A line", "fig:line");
        latex.Should().Contain(@"\captionof{figure}{A line}").And.Contain(@"\label{fig:line}").And.NotContain(@"\begin{figure}");
    }

    [Fact]
    public void The_audit_reads_what_the_latex_defines_and_points_at()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var source = string.Join("\n",
            @"\documentclass{article}",
            @"\begin{document}",
            $"% block:{a}",
            @"\begin{equation}\label{eq:a}x\end{equation}",
            $"% block:{b}",
            @"See \eqref{eq:a}, \ref{fig:none} and \cite{knuth, missing}. % \ref{commented}",
            @"\label{dup}\label{dup}",
            @"\begin{thebibliography}{9}\bibitem{knuth} Knuth.\end{thebibliography}",
            @"\end{document}");
        var audit = new EmittedLatexAudit(source);
        audit.DanglingReferences.Select(r => r.Key).Should().Equal("fig:none");
        audit.DanglingReferences.Single().BlockId.Should().Be(b);
        audit.DuplicateLabels.Should().Equal("dup");
        audit.MissingCitations.Select(c => c.Key).Should().Equal("missing");
        audit.BlockAtLine(4).Should().Be(a);
        audit.BlockAtLine(1).Should().BeNull();
    }

    [Theory]
    [InlineData("! Package inputenc Error: Unicode character ⟨ (U+27E8)\n(inputenc)                not set up for use with LaTeX.", "unicode_character", "U+27E8")]
    [InlineData("! Fatal Package fontspec Error: The fontspec package requires either XeTeX or\n(fontspec)                      LuaTeX.", "needs_xelatex", "fontspec")]
    [InlineData("! LaTeX Error: Command \\R already defined.", "already_defined", "\\R")]
    public void Compile_errors_say_what_they_are(string log, string category, string token)
    {
        var parsed = LaTeXErrorParser.Parse(log + "\nl.12 x");
        parsed!.Category.Should().Be(category);
        parsed.Token.Should().Be(token);
    }

    [Theory]
    [InlineData(@"\usepackage{fontspec}", null, "xelatex")]
    [InlineData(@"\usepackage{amsmath}", null, "pdflatex")]
    [InlineData(@"\usepackage{amsmath}", "lualatex", "lualatex")]
    [InlineData(@"\usepackage{fontspec}", "pdflatex", "xelatex")]
    public void The_check_compiles_with_the_engine_the_latex_needs(string preamble, string? documentEngine, string expected) =>
        Lilia.Api.Mcp.LiliaMcpTools.EngineFor($"\\documentclass{{article}}\n{preamble}", documentEngine).Should().Be(expected);
}
