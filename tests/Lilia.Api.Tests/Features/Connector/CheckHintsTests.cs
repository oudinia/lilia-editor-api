using FluentAssertions;
using Lilia.Api.Mcp;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Features.AskLilia.AskLiliaHarness;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// The connector test of 17 Nov 2026, items E and F. The check didn't say how long the PDF is, though venues limit
/// pages; TikZ's "Unknown function `of'" took a round trip to read as "load the positioning library"; and an
/// @footnote{…} left in a paragraph printed as typed while the check said it printed correctly. The check now gives
/// the page count, a plain hint for the errors whose cause is common, and a warning on the block with the mark. The
/// log lines below are TeX's own (pdfTeX 2025), wrapped at 79 columns as TeX writes them.
/// </summary>
public class CheckHintsTests : IDisposable
{
    private readonly string _memoDir = Directory.CreateTempSubdirectory("lilia-hints-").FullName;

    public void Dispose() => Directory.Delete(_memoDir, true);

    // ── E. pages ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Output written on document.pdf (3 pages, 41234 bytes).", 3)]
    [InlineData("Output written on document.pdf (1 page, 12011 bytes).", 1)]
    [InlineData("Output written on document.pdf (12 pages).", 12)] // XeTeX
    // The temporary directory makes the line longer than 79 columns: TeX wraps it.
    [InlineData("Output written on /tmp/lilia-latex-0123456789abcdef0123456789abcdef/document.pdf (2\n pages, 30311 bytes).", 2)]
    public void The_page_count_is_read_from_the_log(string tail, int pages)
    {
        CheckHints.Pages("This is pdfTeX, Version 3.141592653\n(./document.tex\n)\n" + tail + "\nTranscript written on document.log.").Should().Be(pages);
    }

    [Fact]
    public void A_compile_that_wrote_no_PDF_has_no_page_count()
    {
        CheckHints.Pages("!  ==> Fatal error occurred, no output PDF file produced!").Should().BeNull();
        CheckHints.Pages("").Should().BeNull();
    }

    [Fact]
    public void The_count_says_when_the_exported_PDF_can_be_longer()
    {
        CheckHints.PagesNote("\\begin{document}\nText.\n\\end{document}").Should().BeNull();
        CheckHints.PagesNote("\\IfFileExists{figures/a.png}{%\n").Should().Contain("images");
        CheckHints.PagesNote("\\tableofcontents\n").Should().Contain("table of contents");
    }

    // ── F. hints for TeX's errors ────────────────────────────────────────

    private static string Log(string error) =>
        "(/usr/share/texlive/texmf-dist/tex/generic/pgf/frontendlayer/tikz/tikz.sty)\n" + error
        + "\n\nSee the pgf package documentation for explanation.\nType  H <return>  for immediate help.\n ...\n\nl.9 \\end{tikzpicture}\n";

    [Theory]
    [InlineData("! Package PGF Math Error: Unknown function `of' (in 'of a').", "positioning")]
    [InlineData("! Package PGF Math Error: Unknown operator `o' or `of' (in '1cm of a').", "positioning")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/on grid' and I am going t\no ignore it. Perhaps you misspelled it.", "positioning")]
    [InlineData("! Package pgf Error: Unknown arrow tip kind 'Stealth'.", "arrows.meta")]
    [InlineData("! Package pgf Error: Unknown arrow tip kind 'Latex'.", "arrows.meta")]
    [InlineData("! Package pgf Error: Unknown arrow tip kind 'triangle 45'.", "arrows")]
    [InlineData("! Package tikz Error: You need to say \\usetikzlibrary{calc} for coordinate calc\nulation.", "calc")]
    [InlineData("! Package tikz Error: You need to say \\usetikzlibrary{patterns}.", "patterns")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/ellipse' and I am going t\no ignore it. Perhaps you misspelled it.", "shapes.geometric")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/regular polygon sides', t\no which you passed '5', and I am going to ignore it. Perhaps you misspelled it.", "shapes.geometric")]
    [InlineData("! Package tikz Error: Unknown shape ``diamond.'' Using ``rectangle'' instead.", "shapes.geometric")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/rectangle split' and I am\n going to ignore it. Perhaps you misspelled it.", "shapes.multipart")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/fit', to which you passed\n '(a)', and I am going to ignore it. Perhaps you misspelled it.", "fit")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/matrix of nodes' and I am\n going to ignore it. Perhaps you misspelled it.", "matrix")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/drop shadow' and I am goi\nng to ignore it. Perhaps you misspelled it.", "shadows")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/on background layer' and \nI am going to ignore it. Perhaps you misspelled it.", "backgrounds")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/\"x\" above' and I am going\n to ignore it. Perhaps you misspelled it.", "quotes")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/name path', to which you \npassed 'a', and I am going to ignore it. Perhaps you misspelled it.", "intersections")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/state' and I am going to \nignore it. Perhaps you misspelled it.", "automata")]
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/decoration', to which you\n passed 'brace,amplitude=5pt', and I am going to ignore it. Perhaps you misspel\nled it.", "decorations.pathreplacing")]
    public void A_missing_TikZ_library_is_named_with_where_it_goes(string error, string library)
    {
        var hint = CheckHints.ForError(Log(error));
        hint.Should().Contain($"TikZ library {library}.");
        hint.Should().Contain($"\\usetikzlibrary{{{library}}}").And.Contain("custom preamble").And.Contain("set_document_settings");
    }

    [Fact]
    public void A_decoration_is_named_from_the_line_TeX_quotes()
    {
        var log = "! Package tikz Error: You need to load a decoration library.\n\nSee the tikz package documentation for explanation.\n"
            + "Type  H <return>  for immediate help.\n ...                                              \n\nl.2 ...picture}\\draw[decorate,decoration={zigzag}]\n";
        CheckHints.ForError(log).Should().Contain("decorations.pathmorphing");
        CheckHints.ForError("! Package tikz Error: You need to load a decoration library.\n").Should()
            .Contain("decorations.pathreplacing (brace").And.Contain("decorations.pathmorphing (zigzag");
    }

    [Theory]
    // A key no library defines is more likely misspelt: no hint rather than a wrong one.
    [InlineData("! Package pgfkeys Error: I do not know the key '/tikz/colr' and I am going to i\ngnore it. Perhaps you misspelled it.")]
    [InlineData("! Package pgf Error: Unknown arrow tip kind 'Stelth'.")]
    [InlineData("! Undefined control sequence.\nl.7 \\badcommand")]
    [InlineData("! Missing $ inserted.")]
    public void An_error_with_no_reliable_cause_gets_no_hint(string error)
    {
        CheckHints.ForError(Log(error)).Should().BeNull();
    }

    // ── F. Lilia marks printed as typed ──────────────────────────────────

    [Fact]
    public void A_mark_left_in_the_text_is_found_on_its_line_with_what_to_write()
    {
        var source = string.Join("\n",
            @"\documentclass{article}",
            @"\begin{document}",
            @"Results hold.@footnote{Measured in 2025.} See @citep{knuth} and @ref(fig:a).",
            @"@heading[level=2] Methods",
            @"\end{document}");
        var marks = CheckHints.LiteralMarks(source);
        marks.Select(m => (m.Line, m.Mark)).Should().Equal((3, "@footnote"), (3, "@citep"), (3, "@ref"), (4, "@heading"));
        marks[0].Message.Should().Contain(@"\footnote{").And.Contain("footnote block");
        marks[1].Message.Should().Contain("@cite{key}");
        marks[2].Message.Should().Contain("@ref{label}");
        marks[3].Message.Should().Contain("its own block");
    }

    [Fact]
    public void Code_comments_addresses_and_commands_are_not_marks()
    {
        var source = string.Join("\n",
            @"\documentclass{article}",
            @"\newcommand{\x}{@footnote{not body}}",
            @"\begin{document}",
            @"\begin{lstlisting}",
            @"/** Use {@code @footnote{x}} here. */",
            @"\end{lstlisting}",
            @"Write \verb|@footnote{x}| or \lstinline{@cite[k]}; mail ada@footnote.org.",
            @"\makeatletter\@footnotetext{x}\makeatother % @footnote{in a comment}",
            @"Escaped: @cite\{key\}.",
            @"\end{document}");
        CheckHints.LiteralMarks(source).Should().BeEmpty();
    }

    // ── C. what add_figure_image accepts ─────────────────────────────────

    [Fact]
    public void A_figure_image_is_PNG_JPEG_or_PDF_by_its_own_bytes()
    {
        LiliaMcpWriteTools.FigureImageKind(Convert.FromBase64String(OnePixelPng)).Should().Be(("image/png", ".png", (string?)null));
        LiliaMcpWriteTools.FigureImageKind([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10]).Mime.Should().Be("image/jpeg");
        LiliaMcpWriteTools.FigureImageKind("%PDF-1.5\n"u8.ToArray()).Mime.Should().Be("application/pdf");
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"/>", "an SVG drawing")]
    [InlineData("GIF89a....", "a GIF")]
    [InlineData("RIFF\0\0\0\0WEBPVP8 ", "a WebP image")]
    [InlineData("%!PS-Adobe-3.0 EPSF-3.0", "an EPS file")]
    [InlineData("hello", null)]
    public void Other_files_are_refused_and_named(string head, string? other)
    {
        var kind = LiliaMcpWriteTools.FigureImageKind(System.Text.Encoding.Latin1.GetBytes(head));
        kind.Mime.Should().BeNull();
        kind.Other.Should().Be(other);
    }

    [Theory]
    [InlineData("results-plot.png", ".png", "results-plot.png")]
    [InlineData("photo.JPEG", ".jpg", "photo.jpeg")]
    [InlineData("plot.png", ".pdf", "plot.pdf")] // named .png, but it is a PDF
    [InlineData("../../etc/passwd", ".png", "passwd.png")]
    [InlineData("", ".png", "figure.png")]
    public void The_file_keeps_its_name_with_the_extension_of_what_it_is(string given, string extension, string kept)
    {
        LiliaMcpWriteTools.FigureImageFileName(given, extension).Should().Be(kept);
    }

    internal const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    // ── for real ─────────────────────────────────────────────────────────

    private DocumentCheckService Checker(DocumentDto doc, string source)
    {
        var render = new Mock<Lilia.Engines.IRenderService>();
        render.Setup(r => r.RenderToLatexAsync(doc.Id)).ReturnsAsync(source);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:CheckMemoDir"] = _memoDir }).Build();
        var memos = new CheckMemoStore(new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().Object, config, NullLogger<CheckMemoStore>.Instance);
        return new DocumentCheckService(render.Object, new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance), memos, config, NullLogger<DocumentCheckService>.Instance);
    }

    private static System.Text.Json.JsonElement Report(DocumentCheck c) => System.Text.Json.JsonSerializer.SerializeToElement(c.Report);

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_the_check_counts_the_pages_and_names_the_TikZ_library()
    {
        var block = Guid.NewGuid();
        var doc = Doc(Block(block));
        var paragraphs = string.Join("\n\n", Enumerable.Range(1, 120).Select(i => $"Paragraph {i} of the long document, with enough words to fill a line or two of the page."));
        var clean = await Checker(doc, $"\\documentclass{{article}}\n\\begin{{document}}\n% block:{block}\n{paragraphs}\n\\end{{document}}\n").CheckAsync(doc);
        var report = Report(clean);
        report.GetProperty("compiles").GetBoolean().Should().BeTrue(report.ToString());
        report.GetProperty("pages").GetInt32().Should().BeGreaterThan(1);

        var tikz = Doc(Block(block));
        var broken = await Checker(tikz, $$"""
            \documentclass{article}
            \usepackage{tikz}
            \begin{document}
            % block:{{block}}
            \begin{tikzpicture}
            \node (a) {A};
            \node[below=of a] (b) {B};
            \end{tikzpicture}
            \end{document}
            """).CheckAsync(tikz);
        var r = Report(broken);
        r.GetProperty("compiles").GetBoolean().Should().BeFalse();
        r.GetProperty("hint").GetString().Should().Contain(@"\usetikzlibrary{positioning}");
        r.GetProperty("pages").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "no PDF was written");
        broken.Problems.Should().Contain(p => p.Severity == "error" && p.BlockId == block.ToString() && p.Message.Contains("positioning"));
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_an_LML_mark_in_a_paragraph_compiles_but_does_not_print_correctly()
    {
        var good = Guid.NewGuid();
        var bad = Guid.NewGuid();
        var doc = Doc(Block(good), Block(bad));
        var check = await Checker(doc, $"\\documentclass{{article}}\n\\begin{{document}}\n% block:{good}\nFine text.\n\n% block:{bad}\nResults hold.@footnote{{Measured in 2025.}}\n\\end{{document}}\n").CheckAsync(doc);

        check.Compiles.Should().BeTrue();
        check.PrintsCorrectly.Should().BeFalse("the PDF prints \"@footnote\" in the text");
        check.Problems.Should().ContainSingle(p => p.BlockId == bad.ToString() && p.Line == 7 && p.Message.Contains(@"\footnote{"));
        Report(check).GetProperty("note").GetString().Should().Contain("printed as typed");
    }
}
