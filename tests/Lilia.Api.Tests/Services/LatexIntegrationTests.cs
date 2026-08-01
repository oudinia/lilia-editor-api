using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Integration tests that validate generated LaTeX actually compiles via pdflatex.
/// Uses Docker with texlive to run pdflatex locally.
///
/// Prerequisites: Docker must be running. First run pulls the image (~600MB).
/// Run: dotnet test --filter "Category=LatexIntegration"
/// </summary>
[Trait("Category", "LatexIntegration")]
public class LatexIntegrationTests : IAsyncLifetime
{
    private readonly RenderService _renderService;
    private static bool _dockerAvailable;
    private static readonly string TmpBase = Path.Combine(Path.GetTempPath(), "lilia-latex-tests");

    /// <summary>
    /// The TeX image these tests compile against.
    /// </summary>
    /// <remarks>
    /// <para>It has to be a <b>full</b> TeX Live. The preamble these tests
    /// compile loads siunitx, mathtools, tcolorbox, cleveref and more, and
    /// production installs them via <c>texlive-science</c>,
    /// <c>texlive-latex-extra</c> and friends.</para>
    ///
    /// <para><c>lilia-texlive:bookworm</c> and <c>:trixie</c> look like the
    /// obvious choice — the project pins them and measures its font facts
    /// against them — but <b>neither contains siunitx</b>, so every one of these
    /// tests fails on <c>File 'siunitx.sty' not found</c>. They are the
    /// latex-service's measurement toolchain, not this API's runtime.</para>
    ///
    /// <para>Override with <c>LILIA_TEX_IMAGE</c>. The most faithful target
    /// would be an image built from this repository's own Dockerfile, since
    /// that is what production actually has; <c>texlive/texlive:latest</c> is a
    /// superset of it and is what CI has always used.</para>
    /// </remarks>
    /// <summary>
    /// Images to try, in order, when <c>LILIA_TEX_IMAGE</c> is not set.
    /// </summary>
    /// <remarks>
    /// The small derived image first because it is closer to production and
    /// builds in a minute; the full TeX Live second because that is what CI has
    /// always used and is present there. Whichever is found is reported, so a
    /// result is never attributed to the wrong toolchain.
    /// </remarks>
    private static readonly string[] CandidateImages =
    [
        "lilia-texlive:test",
        "texlive/texlive:latest",
    ];

    private static string TexImage = "";

    public LatexIntegrationTests()
    {
        var logger = new Mock<ILogger<RenderService>>();
        _renderService = new RenderService(null!, logger.Object);
    }

    public async Task InitializeAsync()
    {
        // Docker AND an image. Checking only the daemon meant a machine with
        // docker but without the image failed 55 tests with a pdflatex error,
        // rather than reporting the one thing actually missing.
        try
        {
            var candidates = Environment.GetEnvironmentVariable("LILIA_TEX_IMAGE") is { Length: > 0 } configured
                ? [configured]
                : CandidateImages;

            foreach (var image in candidates)
            {
                var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, UseShellExecute = false };
                psi.ArgumentList.Add("image");
                psi.ArgumentList.Add("inspect");
                psi.ArgumentList.Add(image);

                var probe = Process.Start(psi);
                await probe!.WaitForExitAsync();

                if (probe.ExitCode == 0)
                {
                    TexImage = image;
                    _dockerAvailable = true;
                    break;
                }
            }
        }
        catch
        {
            _dockerAvailable = false;
        }

        Directory.CreateDirectory(TmpBase);
    }

    public Task DisposeAsync()
    {
        try
        {
            // On Linux the container may have written root-owned files that the
            // test user cannot remove, so a throwaway container does the
            // deleting. On Windows a bind mount leaves ordinary files and
            // pulling an extra image to delete them would be absurd.
            if (!OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo("docker") { UseShellExecute = false };
                foreach (var argument in new[]
                         {
                             "run", "--rm", "-v", $"{TmpBase}:/cleanup",
                             "alpine", "rm", "-rf", "/cleanup/.",
                         })
                {
                    psi.ArgumentList.Add(argument);
                }

                Process.Start(psi)?.WaitForExit(5000);
            }

            Directory.Delete(TmpBase, true);
        }
        catch { /* best-effort cleanup of a temp directory */ }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates LaTeX by writing to temp file and running pdflatex in Docker.
    /// </summary>
    private async Task<(bool Valid, string? Error)> ValidateWithDocker(string latex)
    {
        if (!_dockerAvailable)
        {
            // This does NOT skip, and used to claim it did.
            //
            // SkipException is a plain exception defined at the bottom of this
            // file with no xUnit integration — xUnit 2.9 has no runtime skip —
            // so throwing it fails the test with the word "skipping" in the
            // message. A test that says it was skipped while being counted as a
            // failure is the same shape as everything else this suite exists to
            // catch: plausible output, something wrong, nobody told.
            //
            // Named for what it is until the project takes a real skip
            // mechanism (Xunit.SkippableFact, or xUnit v3's Assert.Skip).
            throw new TestEnvironmentUnavailableException(
                "FAILED, not skipped: no TeX image available, so this test could not run. Tried " +
                string.Join(" and ", CandidateImages) + ". Build the small one with " +
                "`docker build -f tests/Lilia.Api.Tests/Fixtures/texlive-test.Dockerfile -t lilia-texlive:test .`, " +
                "or set LILIA_TEX_IMAGE to an image you have.");
        }

        var testDir = Path.Combine(TmpBase, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        var texPath = Path.Combine(testDir, "test.tex");
        await File.WriteAllTextAsync(texPath, latex);

        // docker is invoked directly rather than through `/bin/bash -c`.
        //
        // These 55 tests are the only thing that compiles every block type with
        // a real TeX engine, and on Windows they did not run at all: /bin/bash
        // does not resolve, so every one failed with "cannot find the file
        // specified" — a message about a shell, for a test about LaTeX. They
        // reported as ordinary failures alongside genuine ones, which made the
        // whole suite easy to wave through.
        //
        // ArgumentList also removes the quoting entirely. The old form nested a
        // quoted command inside a quoted argument, which breaks on any path
        // containing a space — and the default Windows temp path contains one.
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--rm");

        // Only where it means something. On Linux this stops the container
        // leaving root-owned files in the temp directory; on Windows bind
        // mounts have no such problem, and forcing a uid the image does not
        // know about only breaks it.
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("UID") is { Length: > 0 } uid)
        {
            psi.ArgumentList.Add("--user");
            psi.ArgumentList.Add(uid);
        }

        // The pinned images declare an entrypoint of their own, so the engine
        // has to be named explicitly rather than passed as an argument to
        // whatever the image already runs.
        psi.ArgumentList.Add("--entrypoint");
        psi.ArgumentList.Add("pdflatex");

        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add($"{testDir}:/work");
        psi.ArgumentList.Add("-w");
        psi.ArgumentList.Add("/work");
        psi.ArgumentList.Add(TexImage);

        psi.ArgumentList.Add("-interaction=nonstopmode");
        psi.ArgumentList.Add("-halt-on-error");
        psi.ArgumentList.Add("--no-shell-escape");
        psi.ArgumentList.Add("test.tex");

        var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode == 0)
            return (true, null);

        var errors = stdout.Split('\n')
            .Where(l => l.StartsWith("!") || l.Contains("Error"))
            .Take(5)
            .ToArray();

        var errorMsg = errors.Length > 0
            ? string.Join("\n", errors)
            : $"Exit code {proc.ExitCode}: {stderr[..Math.Min(500, stderr.Length)]}";

        try { Directory.Delete(testDir, true); } catch { }

        return (false, errorMsg);
    }

    /// <summary>
    /// Create block → render to LaTeX → wrap in preamble → compile with pdflatex → assert success.
    /// </summary>
    private async Task AssertBlockCompiles(string type, string contentJson, string? description = null)
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = type,
            Content = JsonDocument.Parse(contentJson),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var latex = _renderService.RenderBlockToLatex(block);
        var fullDoc = LaTeXPreamble.WrapForValidation(latex);
        var (valid, error) = await ValidateWithDocker(fullDoc);

        valid.Should().BeTrue(
            $"Block '{type}'{(description != null ? $" ({description})" : "")} should compile.\n" +
            $"Error: {error}\n" +
            $"LaTeX fragment:\n{latex}");
    }

    // ── Core block types ──────────────────────────────────────────

    [Fact] public async Task Paragraph_PlainText() => await AssertBlockCompiles("paragraph", """{"text":"Hello world."}""");
    [Fact] public async Task Paragraph_BoldItalic() => await AssertBlockCompiles("paragraph", """{"text":"**bold** *italic* __underline__ ~~strike~~ `code`"}""", "inline formatting");
    [Fact] public async Task Paragraph_InlineMath() => await AssertBlockCompiles("paragraph", """{"text":"$E = mc^2$ and $\\int_0^1 x dx$"}""");
    [Fact] public async Task Paragraph_SpecialChars() => await AssertBlockCompiles("paragraph", """{"text":"100% success & profit #1"}""", "escaped chars");
    [Fact] public async Task Paragraph_CiteRefUrl() => await AssertBlockCompiles("paragraph", """{"text":"\\cite{key} \\ref{lbl} \\url{https://x.com}"}""");

    // ── Equations ──────────────────────────────────────────────────

    [Fact] public async Task Equation_Display() => await AssertBlockCompiles("equation", """{"latex":"E = mc^2","displayMode":true}""");
    [Fact] public async Task Equation_Inline() => await AssertBlockCompiles("equation", """{"latex":"x^2","displayMode":false}""");
    [Fact] public async Task Equation_Align() => await AssertBlockCompiles("equation", """{"latex":"\\begin{align}a &= b \\\\ c &= d\\end{align}","displayMode":false}""", "align");
    [Fact] public async Task Equation_Gather() => await AssertBlockCompiles("equation", """{"latex":"\\begin{gather}a \\\\ b\\end{gather}","displayMode":false}""", "gather");
    [Fact] public async Task Equation_Multline() => await AssertBlockCompiles("equation", """{"latex":"\\begin{multline}a + b \\\\ = c + d\\end{multline}","displayMode":false}""", "multline");
    [Fact] public async Task Equation_Cases() => await AssertBlockCompiles("equation", """{"latex":"f(x) = \\begin{cases} 1 & x > 0 \\\\ 0 & x \\leq 0 \\end{cases}","displayMode":true}""", "cases");
    [Fact] public async Task Equation_Pmatrix() => await AssertBlockCompiles("equation", """{"latex":"\\begin{pmatrix} a & b \\\\ c & d \\end{pmatrix}","displayMode":true}""", "pmatrix");
    [Fact] public async Task Equation_Bmatrix() => await AssertBlockCompiles("equation", """{"latex":"\\begin{bmatrix} 1 & 0 \\\\ 0 & 1 \\end{bmatrix}","displayMode":true}""", "bmatrix");
    [Fact] public async Task Equation_Vmatrix() => await AssertBlockCompiles("equation", """{"latex":"\\begin{vmatrix} a & b \\\\ c & d \\end{vmatrix}","displayMode":true}""", "determinant");
    [Fact] public async Task Equation_Cancel() => await AssertBlockCompiles("equation", """{"latex":"\\cancel{x^2} + 1","displayMode":true}""", "cancel pkg");
    [Fact] public async Task Equation_Mathscr() => await AssertBlockCompiles("equation", """{"latex":"\\mathscr{L}(f)","displayMode":true}""", "mathrsfs pkg");
    [Fact] public async Task Equation_Siunitx() => await AssertBlockCompiles("equation", """{"latex":"\\SI{9.8}{\\meter\\per\\second\\squared}","displayMode":true}""", "siunitx pkg");
    [Fact] public async Task Equation_Mathtools() => await AssertBlockCompiles("equation", """{"latex":"A \\coloneqq B","displayMode":true}""", "mathtools pkg");
    [Fact] public async Task Equation_NestedFractions() => await AssertBlockCompiles("equation", """{"latex":"\\frac{\\frac{a}{b}}{\\frac{c}{d}}","displayMode":true}""");
    [Fact] public async Task Equation_Placeholder() => await AssertBlockCompiles("equation", """{"latex":"x + \\placeholder{} = y","displayMode":true}""", "MathLive artifact");
    [Fact] public async Task Equation_Split() => await AssertBlockCompiles("equation", """{"latex":"\\begin{equation}\\begin{split}a &= b + c \\\\ &= d + e\\end{split}\\end{equation}","displayMode":false}""", "split env");
    [Fact] public async Task Equation_Dcases() => await AssertBlockCompiles("equation", """{"latex":"f(x) = \\begin{dcases} x & x \\geq 0 \\\\ -x & x < 0 \\end{dcases}","displayMode":true}""", "dcases (mathtools)");
    [Fact] public async Task Equation_Smallmatrix() => await AssertBlockCompiles("equation", """{"latex":"\\bigl(\\begin{smallmatrix} a & b \\\\ c & d \\end{smallmatrix}\\bigr)","displayMode":true}""", "smallmatrix");

    // ── Theorems ──────────────────────────────────────────────────

    [Theory]
    [InlineData("theorem")]
    [InlineData("definition")]
    [InlineData("lemma")]
    [InlineData("corollary")]
    [InlineData("proposition")]
    [InlineData("remark")]
    [InlineData("example")]
    [InlineData("proof")]
    public async Task Theorem_AllSubtypes(string theoremType) =>
        await AssertBlockCompiles("theorem", $"{{\"theoremType\":\"{theoremType}\",\"title\":\"Test\",\"text\":\"Statement.\"}}", theoremType);

    [Fact] public async Task Theorem_WithLabel() => await AssertBlockCompiles("theorem", """{"theoremType":"theorem","title":"Main","text":"Result.","label":"thm:main"}""", "with label");
    [Fact] public async Task Theorem_WithMath() => await AssertBlockCompiles("theorem", """{"theoremType":"definition","title":"","text":"$(G, \\cdot)$ is a group."}""", "inline math");
    [Fact] public async Task Theorem_Unnumbered() => await AssertBlockCompiles("theorem", """{"theoremType":"theorem","title":"","text":"Statement.","numbered":false}""", "unnumbered *");

    // ── Other blocks ──────────────────────────────────────────────

    [Fact] public async Task Code_Python() => await AssertBlockCompiles("code", """{"code":"print('hello')","language":"python"}""");
    [Fact] public async Task Code_WithCaption() => await AssertBlockCompiles("code", """{"code":"x=1","language":"","caption":"Example","lineNumbers":true}""");
    [Fact] public async Task Table_Basic() => await AssertBlockCompiles("table", """{"rows":[["A","B"],["1","2"]]}""");
    [Fact] public async Task List_Ordered() => await AssertBlockCompiles("list", """{"items":["a","b","c"],"listType":"ordered"}""");
    [Fact] public async Task List_Unordered() => await AssertBlockCompiles("list", """{"items":["x","y"],"listType":"unordered"}""");
    [Fact] public async Task List_StartAt5() => await AssertBlockCompiles("list", """{"items":["fifth","sixth"],"listType":"ordered","start":5}""", "start=5");
    [Fact] public async Task Blockquote() => await AssertBlockCompiles("blockquote", """{"text":"A quote."}""");
    [Fact] public async Task Abstract() => await AssertBlockCompiles("abstract", """{"text":"Abstract text."}""");
    [Fact] public async Task Algorithm() => await AssertBlockCompiles("algorithm", """{"title":"Sort","code":"sort(arr)","caption":"Algo"}""");
    [Fact] public async Task Callout() => await AssertBlockCompiles("callout", """{"variant":"note","title":"Note","text":"Info."}""");
    [Fact] public async Task Figure() => await AssertBlockCompiles("figure", """{"src":"img.png","caption":"Fig","alt":""}""");
    [Fact] public async Task PageBreak() => await AssertBlockCompiles("pageBreak", "{}");
    [Fact] public async Task TableOfContents() => await AssertBlockCompiles("tableOfContents", "{}");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Heading_Level(int level) => await AssertBlockCompiles("heading", $"{{\"text\":\"Heading\",\"level\":{level}}}");

    // ── Edge cases ────────────────────────────────────────────────

    [Fact] public async Task Empty_Paragraph() => await AssertBlockCompiles("paragraph", """{"text":""}""");
    [Fact] public async Task Empty_Equation() => await AssertBlockCompiles("equation", """{"latex":"","displayMode":true}""");
    [Fact] public async Task Empty_Theorem() => await AssertBlockCompiles("theorem", """{"theoremType":"theorem","title":"","text":""}""");
}

// xUnit skip helper
/// <summary>
/// Thrown when a test cannot run because its environment is missing.
/// </summary>
/// <remarks>
/// <para>Was <c>SkipException</c>, which was a lie: it has no xUnit
/// integration, so throwing it fails the test — with a message saying the test
/// was skipped. On any machine without the image that produced 55 failures
/// describing themselves as skips.</para>
/// <para>Renamed rather than made to work, because a real runtime skip needs
/// either <c>Xunit.SkippableFact</c> or xUnit v3, and adding a dependency is a
/// decision worth taking on its own rather than smuggling into a fix. Until
/// then the name and the message say what actually happens.</para>
/// </remarks>
public class TestEnvironmentUnavailableException(string message) : Exception(message);
