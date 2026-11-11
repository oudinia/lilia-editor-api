using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Mcp;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// Paste → LaTeX, phase 2 (11 Nov 2026): the preview shows the blocks a paste becomes, the LaTeX Lilia compiles for
/// them and the verdict, with nothing saved. The emitter and the parsers are real; the compiler is faked here (the
/// verdict's own rules are check_document's, tested with it).
/// </summary>
public class PastePreviewTests : IDisposable
{
    private readonly string _memoDir = Directory.CreateTempSubdirectory("lilia-paste-").FullName;
    public void Dispose() => Directory.Delete(_memoDir, true);

    private string? _compiled;

    private PastePreviewService Service(Func<string, LatexValidationResult> compile)
    {
        var latex = new Mock<ILaTeXRenderService>();
        latex.Setup(l => l.ValidateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string source, string _) => { _compiled = source; return compile(source); });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:CheckMemoDir"] = _memoDir }).Build();
        var memos = new CheckMemoStore(new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().Object, config, NullLogger<CheckMemoStore>.Instance);
        var render = new RenderService(null!, NullLogger<RenderService>.Instance);
        var checker = new DocumentCheckService(render, latex.Object, memos, config, NullLogger<DocumentCheckService>.Instance);
        return new PastePreviewService(render, checker, new LatexParser(), new LmlTextParser(), NullLogger<PastePreviewService>.Instance);
    }

    private static LatexValidationResult Valid() => new(true, null, [], null, 90, "pdflatex", "");

    private static LatexValidationResult BrokenAt(int line) => new(false, "LaTeX compilation failed:\n! Undefined control sequence.",
        [], new LaTeXErrorParser.ParsedLatexError("undefined_control_sequence", @"\nosuchmacro", line, "! Undefined control sequence."), 90, "pdflatex", "");

    [Fact]
    public async Task Markdown_becomes_blocks_and_Lilias_LaTeX_and_the_verdict()
    {
        var preview = await Service(_ => Valid()).PreviewAsync("# Results\n\nThe energy is $E = mc^2$.\n\n- first\n- second\n", "AUTO");

        preview.Format.Should().Be("MARKDOWN");
        preview.Blocks.Select(b => b.Type).Should().Equal("heading", "paragraph", "list");
        preview.Latex.Should().Contain(@"\documentclass").And.Contain("Results").And.Contain("E = mc^2").And.Contain(@"\item");
        _compiled.Should().Be(preview.Latex, "the verdict is on exactly the LaTeX shown");
        preview.Compiles.Should().BeTrue();
        preview.Problems.Should().BeEmpty();
    }

    [Fact]
    public async Task A_compile_error_is_placed_on_the_pasted_block_it_is_in()
    {
        const string lml = "@heading[level=1]\n  Method\n@paragraph\n  We use \\nosuchmacro here.\n";
        // The error on the line after the second block's marker: the paragraph.
        static LatexValidationResult AtSecondBlock(string source)
        {
            var lines = source.Split('\n');
            var markers = lines.Select((l, i) => (l, i)).Where(x => x.l.StartsWith("% block:")).Select(x => x.i).ToList();
            return BrokenAt(markers[1] + 2);
        }
        var preview = await Service(AtSecondBlock).PreviewAsync(lml, "LML");

        preview.Compiles.Should().BeFalse();
        preview.Problems.Should().ContainSingle(p => p.Severity == "error")
            .Which.Should().Match<PastePreviewProblem>(p => p.BlockIndex == 1 && p.BlockType == "paragraph");
    }

    [Fact]
    public async Task A_full_LaTeX_paper_keeps_its_own_macros_in_the_preview()
    {
        const string paper = """
            \documentclass{article}
            \newcommand{\R}{\mathbb{R}}
            \begin{document}
            \section{Setting}
            Let $x \in \R$.
            \end{document}
            """;
        var preview = await Service(_ => Valid()).PreviewAsync(paper, "AUTO");

        preview.Format.Should().Be("LATEX");
        preview.Blocks.Select(b => b.Type).Should().Contain("heading").And.Contain("paragraph");
        preview.Latex.Should().Contain(@"\newcommand{\R}{\mathbb{R}}", "the paper's macro is what makes \\R compile");
    }

    [Fact]
    public async Task Text_that_reads_as_nothing_special_is_one_paragraph_and_says_so()
    {
        var preview = await Service(_ => Valid()).PreviewAsync("{ not json", "LML");

        preview.Blocks.Should().ContainSingle().Which.Type.Should().Be("paragraph");
        preview.Fallback.Should().Be("paragraph");
    }

    [Theory]
    [InlineData("", "AUTO", "Paste some text first.")]
    [InlineData("x", "DOCX", "Unknown format")]
    public async Task The_endpoint_says_what_is_wrong_with_the_request(string content, string format, string said)
    {
        var controller = new PasteController(new Mock<IPastePreviewService>().Object);
        var result = await controller.Preview(new PastePreviewRequest(content, format), default);
        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value!.ToString().Should().Contain(said);
    }
}
