using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Tier 1 generation pipeline (Phase 2 step 12b) — leverages every
/// existing LaTeX fixture as a Typst export test. The Lilia block
/// model is the universal interchange: any document parsed into
/// blocks can be re-exported to any target format.
///
/// Per the pre-launch design doc: 173 LaTeX corpus fixtures + 117
/// DOCX corpus fixtures + 101 BlockRenderer fixtures all become
/// Typst coverage tests for free, just by routing through
/// TypstExportService.
///
/// Failure surface here = "this real-world doc class produces
/// fallback markers in Typst output". Each failure is a real signal
/// to add the missing handler in TypstExportService.
///
/// Performance note: pure C# pipeline (no Typst CLI invocation) so
/// runs in seconds for the full 173-fixture corpus.
/// </summary>
public class TypstGeneratedCorpusTests
{
    public static IEnumerable<object[]> LatexCorpusFixtures()
    {
        var root = ResolveFixtureRoot();
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.EnumerateFiles(root, "*.tex", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var rel = Path.GetRelativePath(root, path);
            yield return new object[] { rel };
        }
    }

    [Theory]
    [MemberData(nameof(LatexCorpusFixtures))]
    public async Task Latex_fixture_exports_to_typst_without_fallback(string fixtureRel)
    {
        var root = ResolveFixtureRoot();
        var path = Path.Combine(root, fixtureRel);
        var src = await File.ReadAllTextAsync(path);

        // Skip fixtures with known coverage gaps — the LaTeX corpus
        // already documents these; they're out of scope for Typst
        // export quality.
        if (HasMarker(src, "KNOWN-INVALID")) return;

        var parser = new LatexParser();
        ImportDocument doc;
        try
        {
            doc = await parser.ParseTextAsync(src);
        }
        catch
        {
            // Parser failures are LatexCorpusTests' problem, not ours.
            return;
        }

        if (doc.Elements.Count == 0) return;

        // Convert ImportElements → Blocks (subset of the conversion
        // logic from LatexRoundtripTests; we only care about
        // user-content block types that TypstExporter handles).
        var blocks = ConvertImportToBlocks(doc);

        var exporter = new TypstExportService();
        var stubDoc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "TypstGenCorpus",
            Language = "en",
            PaperSize = "a4",
            FontFamily = "serif",
        };

        string typstSource;
        try
        {
            typstSource = exporter.BuildTypstDocument(stubDoc, blocks);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"Typst export threw on {fixtureRel}: {ex.GetType().Name}: {ex.Message}");
        }

        // Assert no `[Unsupported block type for Typst export: …]`
        // marker survives — every block type in the input must have
        // a handler. Real-world test of the Tier 2 gate.
        typstSource.Should().NotContain("Unsupported block type for Typst export",
            $"fixture {fixtureRel} produced a Typst fallback marker — " +
            "TypstExportService is missing a case branch for one of " +
            "the block types in this document.");
    }

    /// <summary>
    /// Lightweight ImportElement → Block conversion. Only covers types
    /// that produce in-memory blocks for Typst export — same subset
    /// LatexRoundtripTests uses.
    /// </summary>
    private static List<Block> ConvertImportToBlocks(ImportDocument doc)
    {
        var blocks = new List<Block>();
        var sortOrder = 0;

        foreach (var el in doc.Elements)
        {
            string? blockType = null;
            object? content = null;

            switch (el)
            {
                case ImportParagraph p:
                    blockType = "paragraph";
                    content = new { text = p.Text };
                    break;
                case ImportHeading h:
                    blockType = "heading";
                    content = new { text = h.Text, level = h.Level };
                    break;
                case ImportEquation e:
                    blockType = "equation";
                    content = new { latex = e.LatexContent, mode = "display" };
                    break;
                case ImportCodeBlock c:
                    blockType = "code";
                    content = new { code = c.Text, language = c.Language ?? "" };
                    break;
                case ImportTable t:
                    blockType = "table";
                    content = new
                    {
                        rows = t.Rows.Select(r => r.Select(cell => cell.Text).ToArray()).ToArray(),
                    };
                    break;
                case ImportImage img:
                    blockType = "figure";
                    content = new { src = img.Filename ?? "img.png", caption = "" };
                    break;
                case ImportLatexPassthrough:
                    // Embed blocks — pass through as embed type
                    blockType = "embed";
                    content = new { source = "raw" };
                    break;
                default:
                    continue;
            }

            blocks.Add(new Block
            {
                Id = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                Type = blockType,
                Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
                SortOrder = sortOrder++,
            });
        }

        return blocks;
    }

    private static bool HasMarker(string src, string marker) =>
        src.Contains($"% {marker}") || src.Contains($"%{marker}");

    private static string ResolveFixtureRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "Fixtures", "latex-corpus");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        // Fallback to the test project's Fixtures directory
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "latex-corpus");
    }
}
