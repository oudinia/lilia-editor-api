using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// DOCX corpus tests — mirrors LatexCorpusTests for the .docx side.
/// Each fixture under <c>Fixtures/docx-corpus/</c> is parsed via
/// DocxParser and asserted against:
///   1. Parse succeeds (no thrown exception).
///   2. No raw OOXML/style-name/field-code leakage in block text.
///   3. At least one block produced (sanity).
///
/// "Leak" surface for DOCX is different from LaTeX: there are no
/// \cmd tokens. Instead we look for known OOXML/Word artefacts that
/// indicate the parser failed to convert structured Word content into
/// block content — e.g. raw style names like "Heading1" appearing in
/// the body, OOXML namespace prefixes (w:p, w:r), Word field codes
/// (HYPERLINK, REF, PAGEREF), or stray bullet glyphs (•, ▪) that
/// should have triggered list detection.
///
/// Per-fixture <c>EXPECTED-LEAKS:</c> markers don't apply here (no
/// per-file metadata channel in .docx); instead the leak set is a
/// global allowlist that's empty by design.
///
/// Generated fixtures: <c>scripts/generate.sh</c> uses pandoc to turn
/// inline markdown templates into .docx. Hand-crafted fixtures live
/// under <c>curated/</c> for cases pandoc can't produce (e.g. manual
/// bullet lists where the bullet is a literal character, not a
/// numbered list style).
/// </summary>
public class DocxCorpusTests
{
    // Patterns that indicate a real leak — Word-specific artefacts
    // that should never appear in clean block text.
    private static readonly (string Label, Regex Pattern)[] LeakPatterns =
    [
        // OOXML namespace prefixes
        ("ooxml-tag", new Regex(@"<w:[a-zA-Z]+", RegexOptions.Compiled)),
        // Word field codes (HYPERLINK "url", PAGEREF, REF, TOC, …)
        ("field-code", new Regex(@"\bHYPERLINK\s+""|\bPAGEREF\s+|\bREF\s+\w|\bTOC\s+\\", RegexOptions.Compiled)),
        // Stray bullet glyphs in paragraph text — should have triggered
        // list-item detection. Allow inside actual list-item blocks.
        ("manual-bullet", new Regex(@"^\s*[•▪‣◦●■]\s+\S", RegexOptions.Compiled | RegexOptions.Multiline)),
        // Raw style name leakage (rare but possible if formatting
        // extraction fails)
        ("style-name", new Regex(@"\bw:val=""[^""]+""", RegexOptions.Compiled)),
    ];

    public static IEnumerable<object[]> Fixtures()
    {
        var root = ResolveFixtureRoot();
        if (!Directory.Exists(root)) yield break;
        foreach (var path in Directory.EnumerateFiles(root, "*.docx", SearchOption.AllDirectories).OrderBy(p => p))
        {
            var rel = Path.GetRelativePath(root, path);
            yield return new object[] { rel };
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Parse_produces_clean_blocks(string fixtureRel)
    {
        var root = ResolveFixtureRoot();
        var path = Path.Combine(root, fixtureRel);

        var parser = new DocxParser();
        ImportDocument doc;
        try
        {
            doc = await parser.ParseAsync(path);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"Parser threw on {fixtureRel}: {ex.GetType().Name}: {ex.Message}");
        }

        doc.Should().NotBeNull();
        doc.Elements.Should().NotBeEmpty($"{fixtureRel} should produce at least one block");

        var leaks = new List<(string Block, string Label, string Snippet)>();
        foreach (var el in doc.Elements)
        {
            foreach (var (blockLabel, blockText, isListItem) in EnumerateTextSurfaces(el))
            {
                if (string.IsNullOrEmpty(blockText)) continue;
                foreach (var (label, pattern) in LeakPatterns)
                {
                    // Manual bullet glyphs are legitimate inside list-item blocks.
                    if (label == "manual-bullet" && isListItem) continue;
                    var m = pattern.Match(blockText);
                    if (m.Success)
                    {
                        leaks.Add((blockLabel, label, Excerpt(blockText, m.Index)));
                    }
                }
            }
        }

        AppendReportLine(fixtureRel, doc.Elements.Count, leaks);

        leaks.Should().BeEmpty(
            $"no Word-specific artefacts should leak into block text for {fixtureRel}. " +
            $"First leaks: {string.Join(" ; ", leaks.Take(5).Select(l => $"[{l.Block}/{l.Label}] near \"{l.Snippet}\""))}");
    }

    private static IEnumerable<(string Label, string Text, bool IsListItem)> EnumerateTextSurfaces(ImportElement el)
    {
        switch (el)
        {
            case ImportHeading h:
                yield return ("heading", h.Text, false);
                break;
            case ImportParagraph p:
                yield return ("paragraph", p.Text, false);
                break;
            case ImportListItem li:
                yield return ("list-item", li.Text, true);
                break;
            case ImportTable t:
                foreach (var row in t.Rows)
                    foreach (var cell in row)
                        yield return ("table-cell", cell.Text, false);
                break;
            case ImportImage img:
                if (!string.IsNullOrEmpty(img.AltText)) yield return ("figure-caption", img.AltText, false);
                break;
            case ImportBlockquote bq:
                yield return ("blockquote", bq.Text, false);
                break;
            case ImportAbstract ab:
                yield return ("abstract", ab.Text, false);
                break;
            case ImportCodeBlock:
                // Code blocks are passthrough — Word artefacts inside
                // are user content, not leaks.
                yield break;
        }
    }

    private static string ResolveFixtureRoot()
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "docx-corpus");
    }

    private static string Excerpt(string text, int index)
    {
        var start = Math.Max(0, index - 20);
        var end = Math.Min(text.Length, index + 60);
        return text.Substring(start, end - start).Replace('\n', ' ').Replace('\r', ' ');
    }

    private static readonly object ReportLock = new();
    private static bool _reportHeaderWritten;

    private static void AppendReportLine(string fixture, int blockCount, List<(string, string, string)> leaks)
    {
        lock (ReportLock)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "docx-corpus-report.md");
            if (!_reportHeaderWritten)
            {
                File.WriteAllText(path,
                    "# DOCX corpus report\n\n" +
                    $"Generated {DateTimeOffset.UtcNow:u}\n\n" +
                    "| Fixture | Blocks | Leak labels |\n" +
                    "|---------|--------|-------------|\n");
                _reportHeaderWritten = true;
            }
            var leakSummary = leaks.Count == 0
                ? "—"
                : string.Join(", ", leaks.Select(l => l.Item2).Distinct().Take(8));
            File.AppendAllText(path, $"| `{fixture}` | {blockCount} | {leakSummary} |\n");
        }
    }
}
