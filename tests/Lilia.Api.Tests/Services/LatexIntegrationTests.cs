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

    public LatexIntegrationTests()
    {
        var logger = new Mock<ILogger<RenderService>>();
        _renderService = new RenderService(null!, logger.Object);
    }

    public async Task InitializeAsync()
    {
        // Check if docker is available
        try
        {
            var psi = new ProcessStartInfo("docker", "version") { RedirectStandardOutput = true, UseShellExecute = false };
            var proc = Process.Start(psi);
            await proc!.WaitForExitAsync();
            _dockerAvailable = proc.ExitCode == 0;
        }
        catch
        {
            _dockerAvailable = false;
        }

        Directory.CreateDirectory(TmpBase);
    }

    public Task DisposeAsync()
    {
        // Clean up with docker to handle root-owned files
        try
        {
            Process.Start(new ProcessStartInfo("/bin/bash", $"-c \"docker run --rm -v {TmpBase}:/cleanup alpine rm -rf /cleanup/*\"")
            { UseShellExecute = false })?.WaitForExit(5000);
            Directory.Delete(TmpBase, true);
        }
        catch { }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates LaTeX by writing to temp file and running pdflatex in Docker.
    /// </summary>
    private async Task<(bool Valid, string? Error)> ValidateWithDocker(string latex)
    {
        if (!_dockerAvailable)
            throw new SkipException("Docker not available — skipping LaTeX integration test");

        var testDir = Path.Combine(TmpBase, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        var texPath = Path.Combine(testDir, "test.tex");
        await File.WriteAllTextAsync(texPath, latex);

        // Run as current user to avoid root-owned files in temp dir
        var uid = Environment.GetEnvironmentVariable("UID") ?? "1000";
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"docker run --rm --user {uid} -v {testDir}:/work -w /work texlive/texlive:latest pdflatex -interaction=nonstopmode -halt-on-error --no-shell-escape test.tex\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

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
public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
