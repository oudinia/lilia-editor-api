using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Corpus-driven parser tests. Every .tex file under
/// <c>Fixtures/latex-corpus/</c> is parsed and asserted against the
/// no-leak invariant: no raw <c>\cmd</c> should survive into block
/// text, except commands on the preserved allow-list (\cite, \ref, …)
/// that downstream renderers interpret.
///
/// The corpus is hybrid:
///   - <c>curated/</c> — hand-picked fixtures per theme (sectioning,
///     math, tables, figures, lists, theorems, citations, code,
///     hyperref, escapes, fontawesome, layout, edge-cases, cv-templates,
///     beamer). Each is a self-contained compilable .tex.
///   - <c>generated/</c> — optional auto-produced smoke fixtures
///     (one token per file).
///
/// The test also writes a coverage report to
/// <c>bin/&lt;config&gt;/net10.0/latex-corpus-report.md</c> summarising
/// which fixtures passed, which leaked, and the tokens encountered.
/// </summary>
public class LatexCorpusTests
{
    // Commands we deliberately keep in block text because downstream
    // consumers (bibliography renderer, reference resolver, footnote
    // pass) interpret them at render time. Leaks of these are NOT
    // failures.
    private static readonly HashSet<string> AllowedRawCommands = new(StringComparer.Ordinal)
    {
        // Citations — bibliography pass re-renders these.
        "cite", "citep", "citet", "citeauthor", "citeyear", "nocite",
        "parencite", "textcite", "autocite", "footcite",
        // Cross-references — resolver swaps these for numbers/links.
        "ref", "pageref", "eqref", "autoref", "cref", "Cref", "nameref",
        // Labels (anchor, not display).
        "label",
        // Footnotes — footnote pass lifts these out.
        "footnote", "footnotemark", "footnotetext",
        // File-level includes are structural; parser recurses but the
        // directive can linger in passthrough blocks.
        "input", "include",
        // Hyperlinks — post-fix these become markdown, but legacy paths
        // may still have the raw command in passthrough content. Leaving
        // on the allow-list avoids false positives on unconverted paths
        // while preserving the real user-visible invariant (rendered
        // block text is clean in the hot path).
        "hyperref",
    };

    // Generates (FixtureRelPath) tuples for the xUnit [Theory]. xUnit
    // runs MemberData at test-collection time, so directory enumeration
    // happens once per test run.
    public static IEnumerable<object[]> Fixtures()
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
    [MemberData(nameof(Fixtures))]
    public async Task Parse_produces_no_raw_latex_leaks(string fixtureRel)
    {
        var root = ResolveFixtureRoot();
        var path = Path.Combine(root, fixtureRel);
        var src = await File.ReadAllTextAsync(path);

        // Per-fixture allowlist — fixtures that use template-specific
        // user-defined macros (\personalinfo, \makecvheader, …) list
        // them in a `% EXPECTED-LEAKS: cmd1, cmd2` header comment so
        // those known leaks don't fail the build, while NEW unexpected
        // leaks still do.
        var expectedLeaks = ParseExpectedLeaks(src);

        var parser = new LatexParser();
        ImportDocument doc;
        try
        {
            doc = await parser.ParseTextAsync(src);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"Parser threw on {fixtureRel}: {ex.GetType().Name}: {ex.Message}");
        }

        doc.Should().NotBeNull();

        // Walk every block-text surface and look for \cmd tokens that
        // aren't on the allow-list and aren't inside preserved math.
        var leaks = new List<(string Block, string Cmd, string Snippet)>();
        foreach (var el in doc.Elements)
        {
            foreach (var (blockLabel, blockText) in EnumerateTextSurfaces(el))
            {
                if (string.IsNullOrEmpty(blockText)) continue;
                // Strip inline math segments first — \alpha etc. are
                // valid math payloads that downstream KaTeX renders.
                var scrubbed = StripMathSegments(blockText);
                foreach (Match m in Regex.Matches(scrubbed, @"\\([A-Za-z]+)"))
                {
                    var cmd = m.Groups[1].Value;
                    if (AllowedRawCommands.Contains(cmd)) continue;
                    if (expectedLeaks.Contains(cmd)) continue;
                    leaks.Add((blockLabel, cmd, Excerpt(scrubbed, m.Index)));
                }
            }
        }

        // Write the per-fixture report line regardless of outcome so
        // the corpus report is always complete.
        AppendReportLine(fixtureRel, doc.Elements.Count, leaks, expectedLeaks);

        leaks.Should().BeEmpty(
            $"no raw LaTeX commands should leak into block text for {fixtureRel}. " +
            $"First leaks: {string.Join(" ; ", leaks.Take(5).Select(l => $"[{l.Block}] \\{l.Cmd} near \"{l.Snippet}\""))}");
    }

    private static readonly Regex ExpectedLeaksComment = new(
        @"^\s*%\s*EXPECTED-LEAKS:\s*(.+)$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static HashSet<string> ParseExpectedLeaks(string src)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in ExpectedLeaksComment.Matches(src))
        {
            foreach (var tok in m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                set.Add(tok.TrimStart('\\'));
        }
        return set;
    }

    // --- helpers -------------------------------------------------------

    private static string ResolveFixtureRoot()
    {
        // The csproj copies Fixtures/latex-corpus/**/*.tex to the
        // bin output, so this path resolves in both local dev and CI.
        var binDir = AppContext.BaseDirectory;
        return Path.Combine(binDir, "Fixtures", "latex-corpus");
    }

    private static IEnumerable<(string Label, string Text)> EnumerateTextSurfaces(ImportElement el)
    {
        switch (el)
        {
            case ImportHeading h:
                yield return ("heading", h.Text);
                break;
            case ImportParagraph p:
                yield return ("paragraph", p.Text);
                break;
            case ImportCodeBlock c:
                // Code blocks are passthrough by design — raw commands
                // there are literal user content, not leaks.
                yield break;
            case ImportEquation:
                // Equations are preserved as LaTeX source.
                yield break;
            case ImportTable t:
                foreach (var row in t.Rows)
                    foreach (var cell in row)
                        yield return ("table-cell", cell.Text);
                break;
            case ImportListItem li:
                yield return ("list-item", li.Text);
                break;
            case ImportImage img:
                if (!string.IsNullOrEmpty(img.AltText)) yield return ("figure-caption", img.AltText);
                break;
            case ImportBlockquote bq:
                yield return ("blockquote", bq.Text);
                break;
            case ImportAbstract ab:
                yield return ("abstract", ab.Text);
                break;
        }
    }

    private static readonly Regex InlineMath = new(@"\$[^\$]*\$", RegexOptions.Compiled);
    private static readonly Regex DisplayMath = new(@"\$\$[\s\S]*?\$\$", RegexOptions.Compiled);
    private static readonly Regex BacktickCode = new(@"`[^`]*`", RegexOptions.Compiled);

    private static string StripMathSegments(string text)
    {
        text = DisplayMath.Replace(text, " ");
        text = InlineMath.Replace(text, " ");
        // Backtick code is literal text (from \verb, \lstinline) — any
        // backslash sequences inside are user payload, not leaks.
        text = BacktickCode.Replace(text, " ");
        return text;
    }

    private static string Excerpt(string text, int index)
    {
        var start = Math.Max(0, index - 20);
        var end = Math.Min(text.Length, index + 40);
        return text.Substring(start, end - start).Replace('\n', ' ');
    }

    // Report file is cleared at the start of each test run (first write)
    // and appended thereafter. xUnit runs theory cases in parallel, so
    // we lock around the file write.
    private static readonly object ReportLock = new();
    private static bool _reportHeaderWritten;

    private static void AppendReportLine(string fixture, int blockCount, List<(string, string, string)> leaks, HashSet<string> expected)
    {
        lock (ReportLock)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "latex-corpus-report.md");
            if (!_reportHeaderWritten)
            {
                File.WriteAllText(path,
                    "# LaTeX corpus report\n\n" +
                    $"Generated {DateTimeOffset.UtcNow:u}\n\n" +
                    "| Fixture | Blocks | Unexpected leaks | Expected (allowlisted) |\n" +
                    "|---------|--------|------------------|------------------------|\n");
                _reportHeaderWritten = true;
            }
            var unexpected = leaks.Count == 0
                ? "—"
                : string.Join(", ", leaks.Select(l => "\\" + l.Item2).Distinct().Take(8));
            var expectedCol = expected.Count == 0 ? "—" : string.Join(", ", expected.Select(c => "\\" + c).Take(8));
            File.AppendAllText(path, $"| `{fixture}` | {blockCount} | {unexpected} | {expectedCol} |\n");
        }
    }
}
