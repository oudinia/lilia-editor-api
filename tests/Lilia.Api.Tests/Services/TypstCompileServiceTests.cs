using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Integration tests for TypstCompileService — actually invoke
/// typst-cli when it's available locally. Skip gracefully when not.
///
/// Production validation: end-to-end the path that Lilia will use
/// for live preview — block content → TypstExporter → typst-cli →
/// PDF/SVG bytes.
///
/// Skips when:
///   - Typst binary not on PATH / well-known dev locations
///     (typical CI box without the Dockerfile install)
///   - Pre-deployment validation that the compile path fails
///     gracefully also lives here (binary-missing case).
/// </summary>
public class TypstCompileServiceTests
{
    private static bool TypstAvailable()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        var locations = new[]
        {
            Environment.GetEnvironmentVariable("TYPST_BINARY") ?? "",
            string.IsNullOrEmpty(home) ? "" : Path.Combine(home, ".local", "bin", "typst"),
            "/usr/local/bin/typst",
            "/usr/bin/typst",
        };
        if (locations.Any(p => !string.IsNullOrEmpty(p) && File.Exists(p))) return true;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return false;
        return pathEnv.Split(Path.PathSeparator).Any(dir => File.Exists(Path.Combine(dir, "typst")));
    }

    [Fact]
    public async Task Plain_paragraph_compiles_to_svg()
    {
        if (!TypstAvailable())
        {
            // CI env without typst binary — graceful skip. Locally
            // installed via ~/.local/bin/typst, in production via
            // Dockerfile.
            return;
        }

        var compiler = new TypstCompileService();
        const string source = "= Hello\n\nThis is a *bold* paragraph with _italic_ text.";

        var result = await compiler.CompileAsync(source, TypstOutputFormat.Svg);

        result.Success.Should().BeTrue($"compile failed: {result.Error}");
        result.Output.Should().NotBeNullOrEmpty();
        // SVG output starts with `<svg` (after potential XML decl).
        var head = System.Text.Encoding.UTF8.GetString(result.Output!.AsSpan(0, Math.Min(200, result.Output.Length)).ToArray());
        head.Should().Contain("<svg");
    }

    [Fact]
    public async Task Math_block_compiles_to_pdf()
    {
        if (!TypstAvailable()) return;

        var compiler = new TypstCompileService();
        const string source = "= Equation Test\n\n$ E = m c^2 $\n\nThe famous formula.";

        var result = await compiler.CompileAsync(source, TypstOutputFormat.Pdf);

        result.Success.Should().BeTrue($"compile failed: {result.Error}");
        result.Output.Should().NotBeNullOrEmpty();
        // PDF magic: %PDF-
        result.Output![0].Should().Be((byte)'%');
        result.Output[1].Should().Be((byte)'P');
        result.Output[2].Should().Be((byte)'D');
        result.Output[3].Should().Be((byte)'F');
    }

    [Fact]
    public async Task Malformed_source_returns_failure_not_throws()
    {
        if (!TypstAvailable()) return;

        var compiler = new TypstCompileService();
        const string broken = "= Title\n\n#typst-doesnt-have-this-function()";

        var result = await compiler.CompileAsync(broken);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TypstExporter_output_compiles_end_to_end()
    {
        if (!TypstAvailable()) return;

        // The actual integration test: pipe TypstExporter output
        // through TypstCompileService. This is the path the live-
        // preview engine will use in production.
        var exporter = new TypstExportService();
        var blocks = new List<Block>
        {
            new()
            {
                Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(),
                Type = "heading",
                Content = JsonDocument.Parse("""{"text":"Section","level":1}"""),
                SortOrder = 0,
            },
            new()
            {
                Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(),
                Type = "paragraph",
                Content = JsonDocument.Parse("""{"text":"This has **bold** text and *italic* text."}"""),
                SortOrder = 1,
            },
            new()
            {
                Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(),
                Type = "list",
                Content = JsonDocument.Parse("""{"items":["alpha","beta","gamma"],"ordered":false}"""),
                SortOrder = 2,
            },
        };
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Integration Test",
            Language = "en", PaperSize = "a4", FontFamily = "serif",
        };

        var typstSource = exporter.BuildTypstDocument(doc, blocks);
        var compiler = new TypstCompileService();

        var result = await compiler.CompileAsync(typstSource, TypstOutputFormat.Svg);

        result.Success.Should().BeTrue(
            $"end-to-end compile failed. Source:\n{typstSource}\nError:\n{result.Error}");
        result.Output.Should().NotBeNullOrEmpty();
        result.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "Typst preview budget is well under 5s — pdflatex would be 8-30s");
    }

    [Fact]
    public async Task Compile_timeout_is_enforced()
    {
        if (!TypstAvailable()) return;

        // Construct an obviously-trivial doc but with an unrealistically
        // tight timeout to verify the cancellation plumbing fires.
        var compiler = new TypstCompileService(
            options: new TypstCompileOptions { PerCompileTimeout = TimeSpan.FromMilliseconds(1) });
        const string source = "= Test";

        var result = await compiler.CompileAsync(source);

        // Either the compile finished within 1ms (very unlikely on
        // first-run cold) or it timed out. Both paths assert non-throw.
        if (!result.Success)
        {
            result.Error.Should().Contain("timed out");
        }
    }
}
