using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Phase 2 step 12c — Tier 1 single-block compile validation.
///
/// TypstExportFixtureTests checks the rendered Typst <i>source</i>
/// shape (cheap, runs everywhere). This file goes one layer deeper:
/// for each fixture, build a one-block document, render through
/// TypstExportService.BuildTypstDocument, then actually invoke the
/// typst CLI via TypstCompileService and assert the compile succeeds.
///
/// Catches regressions per-block-type that 12b's full-document
/// fixtures could mask (a block that emits invalid syntax inside a
/// surrounding doc may be hidden by the doc's other content; here
/// each block is alone).
///
/// Skips gracefully when the typst binary is missing (matches
/// TypstCompileServiceTests skip pattern).
/// </summary>
public class TypstFixtureCombinatorialTests
{
    public sealed record Fx(
        string Name,
        string BlockType,
        string ContentJson,
        bool ExpectCompile = true);

    public static IEnumerable<object[]> Fixtures() => new[]
    {
        // Each canonical block type, single instance, content shape that
        // mirrors what the parser/editor would emit. Compile-success is
        // the gate — the source-shape gate lives in TypstExportFixtureTests.
        new Fx("paragraph plain",            "paragraph", """{"text":"Hello world"}"""),
        new Fx("paragraph bold",             "paragraph", """{"text":"This is **bold** here"}"""),
        new Fx("paragraph italic",           "paragraph", """{"text":"This is *italic* here"}"""),
        new Fx("paragraph mixed inline",     "paragraph", """{"text":"Mix **bold** and *italic* and `code` together"}"""),
        new Fx("paragraph dollar escape",    "paragraph", """{"text":"Cost is $5 and $10"}"""),
        new Fx("paragraph hashtag escape",   "paragraph", """{"text":"Use #hashtag and #another"}"""),

        new Fx("heading h1",                 "heading",   """{"text":"Introduction","level":1}"""),
        new Fx("heading h2",                 "heading",   """{"text":"Method","level":2}"""),
        new Fx("heading h3",                 "heading",   """{"text":"Variant","level":3}"""),
        new Fx("heading h6 cap",             "heading",   """{"text":"Deep","level":9}"""),
        new Fx("heading inline bold",        "heading",   """{"text":"**Important** result","level":2}"""),

        // Math is the design-doc-known LatexToTypstMath gap. Expected
        // to fall back to pdflatex in production. Tests assert the
        // known-fallback shape so we notice when (a) coverage improves
        // or (b) the gap unexpectedly widens.
        new Fx("equation display (LaTeX→Typst gap)", "equation",  """{"latex":"E = mc^2","mode":"display"}""", ExpectCompile: false),
        new Fx("equation inline plain",      "equation",  """{"latex":"a + b","mode":"inline"}"""),
        new Fx("equation \\frac (LaTeX→Typst gap)",  "equation",  """{"latex":"\\frac{a}{b}","mode":"display"}""", ExpectCompile: false),
        new Fx("equation greek (LaTeX→Typst gap)",   "equation",  """{"latex":"\\alpha + \\beta","mode":"display"}""", ExpectCompile: false),
        new Fx("equation \\sum (LaTeX→Typst gap)",   "equation",  """{"latex":"\\sum_{i=1}^n i","mode":"display"}""", ExpectCompile: false),

        new Fx("list ordered",               "list",      """{"ordered":true,"items":["one","two","three"]}"""),
        new Fx("list unordered",             "list",      """{"ordered":false,"items":["alpha","beta"]}"""),
        new Fx("list nested 2 levels",       "list",      """{"ordered":false,"items":[{"text":"top","children":[{"text":"sub-1"},{"text":"sub-2"}]},{"text":"top-2"}]}"""),
        new Fx("list nested 3 levels",       "list",      """{"ordered":true,"items":[{"text":"a","children":[{"text":"a.1","children":[{"text":"a.1.i"}]}]}]}"""),

        new Fx("blockquote",                 "blockquote",""" {"text":"To be or not to be"}"""),
        new Fx("blockquote alias quote",     "quote",     """{"text":"Aliased quote"}"""),

        new Fx("code python",                "code",      """{"language":"python","code":"def hello():\n    print('hi')"}"""),
        new Fx("code no language",           "code",      """{"code":"plain text fragment"}"""),

        new Fx("abstract",                   "abstract",  """{"text":"This paper presents a method for X."}"""),

        new Fx("theorem",                    "theorem",   """{"theoremType":"theorem","content":"For all primes p, p > 1.","numbered":true}"""),

        new Fx("tableOfContents",            "tableOfContents", """{}"""),
        new Fx("toc alias",                  "toc",       """{}"""),
        new Fx("pageBreak",                  "pageBreak", """{}"""),
        new Fx("page_break alias",           "page_break","""{}"""),
        new Fx("columnBreak",                "columnBreak","""{}"""),
        new Fx("divider alias",              "divider",   """{}"""),

        // Bibliography emits #bibliography("references.bib") but the
        // file is only written by the LaTeX export wrapper. Preview
        // compile path falls back to pdflatex, which has the real
        // resolution. Expected fallback for now.
        new Fx("bibliography (no .bib in preview ctx)", "bibliography", """{}""", ExpectCompile: false),

        // figure — Typst expects an actual asset path. For the preview
        // context we have no asset, so this is a known fallback case
        // (the ZIP/export path resolves real images alongside main.typ).
        new Fx("figure (no asset in preview ctx)", "figure", """{"src":"img.png","caption":"x"}""", ExpectCompile: false),

        // table — minimal 2x2 grid; ImportTable shape is rows[][] of cells.
        new Fx("table 2x2",                  "table",     """{"rows":[[{"text":"A"},{"text":"B"}],[{"text":"C"},{"text":"D"}]]}"""),

        // Aliases / unknown — emit a Typst comment, which compiles to
        // an empty document. Still a valid compile.
        new Fx("alias header→heading",       "header",    """{"text":"Hi","level":1}"""),
        new Fx("unknown type fallthrough",   "totally-unknown", """{"text":"ignored"}"""),
    }.Select(f => new object[] { f });

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

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Single_block_document_compiles(Fx f)
    {
        if (!TypstAvailable())
        {
            // CI without typst — graceful skip. Local dev installs via
            // ~/.local/bin/typst; production via Dockerfile.
            return;
        }

        var exporter = new TypstExportService();
        var compiler = new TypstCompileService();

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Fixture",
            OwnerId = "test-user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Type = f.BlockType,
            Content = JsonDocument.Parse(f.ContentJson),
            SortOrder = 0,
        };

        var source = exporter.BuildTypstDocument(doc, new List<Block> { block });
        source.Should().NotBeNullOrWhiteSpace($"[{f.Name}] exporter produced empty source");

        var result = await compiler.CompileAsync(source, TypstOutputFormat.Pdf);

        if (f.ExpectCompile)
        {
            result.Success.Should().BeTrue($"[{f.Name}] compile failed: {result.Error}\n--- source ---\n{source}");
            result.Output.Should().NotBeNullOrEmpty($"[{f.Name}] compile produced no output");
            // PDF magic bytes — compiled output must be a real PDF
            var head = System.Text.Encoding.ASCII.GetString(result.Output!.AsSpan(0, 4).ToArray());
            head.Should().StartWith("%PDF", $"[{f.Name}] output is not a PDF");
        }
        else
        {
            result.Success.Should().BeFalse($"[{f.Name}] expected compile failure but got success");
        }
    }
}
