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
            new EngineResolver(new RegexOnlyEngineRequirements()),
            NullLogger<ToolRunnerService>.Instance);
    }

    private static Mock<ICompilationQueueService> CompilerReturning(CompilationResult result)
    {
        var compiler = new Mock<ICompilationQueueService>();
        compiler
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>(), It.IsAny<LatexEngine>()))
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
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>(), It.IsAny<LatexEngine>()))
            .ThrowsAsync(new InvalidOperationException("pdflatex not found"));

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Status.Should().Be("unchecked");
        result.Verdict.Status.Should().NotBe("verified");
    }

    [Fact]
    public async Task A_table_is_judged_under_the_engine_it_asks_for()
    {
        // \setmainfont is a fontspec command: it fails under pdflatex for reasons
        // that say nothing about the table. Validating it as pdflatex would report
        // a perfectly good table as broken.
        var render = new Mock<IRenderService>();
        render.Setup(r => r.RenderBlockToLatex(It.IsAny<Block>()))
              .Returns("\\setmainfont{Charter}\\begin{tabular}{l}A\\end{tabular}");

        var compiler = CompilerReturning(new CompilationResult(true, null, null, [], TimeSpan.Zero));
        var runner = new ToolRunnerService(
            new Mock<IBibliographyService>().Object, render.Object,
            new Mock<IDocxImportService>().Object, compiler.Object,
            new EngineResolver(new RegexOnlyEngineRequirements()),
            NullLogger<ToolRunnerService>.Instance);

        await runner.RunAsync(TableTool, TableInput(), null, default);

        compiler.Verify(c => c.CompileLatexAsync(
            It.IsAny<string>(),
            CompilationType.Validate,
            It.IsAny<int>(),
            It.Is<LatexEngine>(e => e != LatexEngine.Pdflatex)));
    }

    [Fact]
    public async Task An_explicit_engine_overrides_detection()
    {
        // Someone whose journal mandates pdflatex, or whose template runs xelatex,
        // needs the verdict to be about the engine they will actually run. Our
        // guess is not an answer to that question.
        var compiler = CompilerReturning(new CompilationResult(true, null, null, [], TimeSpan.Zero));
        var input = JsonSerializer.Deserialize<JsonElement>(
            """{"headers":["A"],"rows":[["1"]],"caption":"c","engine":"lualatex"}""");

        var result = await BuildRunner(compiler).RunAsync(TableTool, input, null, default);

        result.Verdict!.Engine.Should().Be("lualatex");
        result.Verdict.EngineAuto.Should().BeFalse();
        compiler.Verify(c => c.CompileLatexAsync(
            It.IsAny<string>(), CompilationType.Validate, It.IsAny<int>(), LatexEngine.Lualatex));
    }

    [Fact]
    public async Task An_absent_engine_is_inferred_and_says_so()
    {
        var compiler = CompilerReturning(new CompilationResult(true, null, null, [], TimeSpan.Zero));

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Engine.Should().Be("pdflatex");
        result.Verdict.EngineAuto.Should().BeTrue("an empty engine means infer, not 'pdflatex'");
    }

    [Fact]
    public async Task Plain_content_still_goes_to_pdflatex()
    {
        var compiler = CompilerReturning(new CompilationResult(true, null, null, [], TimeSpan.Zero));

        await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        compiler.Verify(c => c.CompileLatexAsync(
            It.IsAny<string>(), CompilationType.Validate, It.IsAny<int>(), LatexEngine.Pdflatex));
    }

    // ── the compiler overrules the guess ─────────────────────────────────────

    private const string WrongEngineLog =
        "! Fatal Package fontspec Error: The fontspec package requires either XeTeX or LuaTeX.";

    /// <summary>Fails the first engine with a mismatch signature, succeeds on the second.</summary>
    private static Mock<ICompilationQueueService> CompilerRejectingEngine(LatexEngine rejects)
    {
        var compiler = new Mock<ICompilationQueueService>();
        compiler
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>(), rejects))
            .ReturnsAsync(new CompilationResult(false, null, WrongEngineLog, [], TimeSpan.FromMilliseconds(500)));
        compiler
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>(),
                It.Is<LatexEngine>(e => e != rejects)))
            .ReturnsAsync(new CompilationResult(true, null, null, [], TimeSpan.FromMilliseconds(1500)));
        return compiler;
    }

    [Fact]
    public async Task A_mis_detected_engine_is_retried_and_the_document_passes()
    {
        // Detection said pdflatex; TeX said it needs lua/xetex. Believing the
        // compiler turns a false "doesn't compile" into the truth.
        var compiler = CompilerRejectingEngine(LatexEngine.Pdflatex);

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Status.Should().Be("verified");
        result.Verdict.Engine.Should().Be("lualatex", "the verdict names the engine that actually worked");
        result.Verdict.DurationMs.Should().Be(2000, "both attempts are what the author waited for");
    }

    [Fact]
    public async Task A_chosen_engine_is_never_retried_behind_the_authors_back()
    {
        // Someone who selected pdflatex is asking whether it compiles *there*.
        // Silently answering about lualatex would answer a different question.
        var compiler = CompilerRejectingEngine(LatexEngine.Pdflatex);
        var input = JsonSerializer.Deserialize<JsonElement>(
            """{"headers":["A"],"rows":[["1"]],"caption":"c","engine":"pdflatex"}""");

        var result = await BuildRunner(compiler).RunAsync(TableTool, input, null, default);

        result.Verdict!.Status.Should().Be("failed");
        result.Verdict.Engine.Should().Be("pdflatex");
        compiler.Verify(
            c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>(), LatexEngine.Lualatex),
            Times.Never);
    }

    [Fact]
    public async Task An_ordinary_failure_is_not_retried()
    {
        // A genuine LaTeX error must cost one compile, not two. Retrying every
        // failure would double the work for the common case to rescue a rare one.
        var compiler = CompilerReturning(
            new CompilationResult(false, null, "! Undefined control sequence.", [], TimeSpan.Zero));

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Status.Should().Be("failed");
        compiler.Verify(
            c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>(), It.IsAny<LatexEngine>()),
            Times.Once);
    }

    [Fact]
    public async Task A_retry_that_also_fails_reports_the_second_engines_reasons()
    {
        var compiler = new Mock<ICompilationQueueService>();
        compiler
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>(), LatexEngine.Pdflatex))
            .ReturnsAsync(new CompilationResult(false, null, WrongEngineLog, [], TimeSpan.Zero));
        compiler
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>(), LatexEngine.Lualatex))
            .ReturnsAsync(new CompilationResult(false, null, "! Missing $ inserted.", [], TimeSpan.Zero));

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Verdict!.Status.Should().Be("failed");
        result.Verdict.Engine.Should().Be("lualatex");
        result.Verdict.Findings.Should().ContainSingle().Which.Should().Contain("Missing $");
    }

    [Fact]
    public async Task The_table_output_is_still_returned_when_verification_is_unavailable()
    {
        // Losing the compiler must not cost the user their table.
        var compiler = new Mock<ICompilationQueueService>();
        compiler
            .Setup(c => c.CompileLatexAsync(It.IsAny<string>(), It.IsAny<CompilationType>(), It.IsAny<int>(), It.IsAny<LatexEngine>()))
            .ThrowsAsync(new TimeoutException());

        var result = await BuildRunner(compiler).RunAsync(TableTool, TableInput(), null, default);

        result.Output.Should().Be(RenderedTable);
        result.Format.Should().Be("latex");
    }
}
