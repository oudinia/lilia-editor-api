using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Tools.Api.Services;
using Lilia.Core.Entities;
using Lilia.Import.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Lilia.Engines;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// What the standalone table tool is allowed to claim about its own output.
///
/// <para>The tool shipped an output pane that read "✓ compiles" on every result
/// while nothing compiled anything — the claim was a string in the markup. These
/// tests pin the replacement: a verdict is only ever "verified" because a compile
/// returned success, and an unreachable compiler produces "unchecked", never a
/// pass.</para>
///
/// <para>That last case is the one that matters. A dev box without TeX, a
/// saturated queue and a timeout all land in the same catch, and the tempting
/// behaviour — assume it's fine — is exactly the silent failure the product
/// exists to catch.</para>
/// </summary>
public class ToolRunnerVerificationTests
{
    private const string RenderedTable = "\\begin{tabular}{lr}\\toprule A & 1 \\\\\\bottomrule\\end{tabular}";

    private static readonly Tool TableTool = new() { Slug = "latex-table", Engine = "table", Enabled = true };

    private static JsonElement TableInput() =>
        JsonSerializer.Deserialize<JsonElement>("""{"headers":["A"],"rows":[["1"]],"caption":"c"}""");

    private static ToolRunnerService BuildRunner(
        Mock<ICompilationQueueService> compiler)
    {
        var render = new Mock<IRenderService>();
        render.Setup(r => r.RenderBlockToLatex(It.IsAny<Block>())).Returns(RenderedTable);

        return new ToolRunnerService(
            new Mock<IBibliographyService>().Object,
            render.Object,
            new Mock<IDocxImportService>().Object,
            compiler.Object,
            NullLogger<ToolRunnerService>.Instance);
    }

    private static Mock<ICompilationQueueService> CompilerReturning(CompilationResult result)
    {
        var compiler = new Mock<ICompilationQueueService>();
        compiler
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>()))
            .ReturnsAsync(result);
        return compiler;
    }

    [Fact]
    public async Task Verified_only_when_the_compile_actually_succeeded()
    {
        var compiler = CompilerReturning(
            new CompilationResult(true, null, null, [], TimeSpan.FromMilliseconds(420)));

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Status.Should().Be("verified");
        result.Verdict.Findings.Should().BeEmpty();
        result.Verdict.DurationMs.Should().Be(420);
    }

    [Fact]
    public async Task It_validates_rather_than_producing_a_pdf()
    {
        var compiler = CompilerReturning(
            new CompilationResult(true, null, null, [], TimeSpan.Zero));

        await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        // A PDF render would cost far more than a web request should spend, and the
        // question being asked is only "does this compile".
        compiler.Verify(c => c.CompileLatexAsync(
            It.Is<string>(s => s.Contains("\\begin{document}")),
            CompilationType.Validate,
            It.IsAny<int>()));
    }

    [Fact]
    public async Task A_failed_compile_reports_the_latex_error_lines()
    {
        var log = "This is pdfTeX\n! Undefined control sequence.\nl.7 \\toprul\n! Missing $ inserted.\n";
        var compiler = CompilerReturning(
            new CompilationResult(false, null, log, [], TimeSpan.FromMilliseconds(300)));

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Status.Should().Be("failed");
        result.Verdict.Findings.Should().Equal("Undefined control sequence.", "Missing $ inserted.");
    }

    [Fact]
    public async Task A_failure_with_no_error_line_falls_back_to_warnings()
    {
        // An overfull box fails validation without ever emitting a `!` line, so
        // dropping non-`!` output would report a failure with no stated cause.
        var compiler = CompilerReturning(
            new CompilationResult(false, null, "no bang lines here", ["Overfull \\hbox (12.3pt too wide)"], TimeSpan.Zero));

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Status.Should().Be("failed");
        result.Verdict.Findings.Should().ContainSingle().Which.Should().Contain("Overfull");
    }

    [Fact]
    public async Task A_failure_with_nothing_to_report_still_says_something_true()
    {
        var compiler = CompilerReturning(
            new CompilationResult(false, null, null, [], TimeSpan.Zero));

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Status.Should().Be("failed");
        result.Verdict.Findings.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unreachable_compiler_is_unchecked_and_never_a_pass()
    {
        var compiler = new Mock<ICompilationQueueService>();
        compiler
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("pdflatex not found"));

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Status.Should().Be("unchecked");
        result.Verdict.Status.Should().NotBe("verified");
    }

    [Fact]
    public async Task The_table_output_is_still_returned_when_verification_is_unavailable()
    {
        // Losing the compiler must not cost the user their table.
        var compiler = new Mock<ICompilationQueueService>();
        compiler
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>()))
            .ThrowsAsync(new TimeoutException());

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Output.Should().Be(RenderedTable);
        result.Format.Should().Be("latex");
    }
}
