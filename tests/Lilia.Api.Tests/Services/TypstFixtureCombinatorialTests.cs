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

        // LaTeX-literal pass — these appear in real imported CV / paper
        // paragraphs and used to trigger silent_fallback. Each must
        // compile cleanly through Typst now.
        new Fx("LaTeX literal: \\noindent stripped", "paragraph",
            """{"text":"\\noindent Hello after noindent"}"""),
        new Fx("LaTeX literal: \\hfill spring",      "paragraph",
            """{"text":"Left text \\hfill Right text"}"""),
        new Fx("LaTeX literal: \\\\\\\\ line break", "paragraph",
            """{"text":"First line \\\\ Second line"}"""),
        new Fx("LaTeX literal: \\textbf",            "paragraph",
            """{"text":"This is \\textbf{strongly emphasised} content"}"""),
        new Fx("LaTeX literal: \\textit",            "paragraph",
            """{"text":"This is \\textit{italicised} content"}"""),
        new Fx("LaTeX literal: \\emph",              "paragraph",
            """{"text":"This is \\emph{emphasised} content"}"""),
        new Fx("LaTeX literal: \\underline",         "paragraph",
            """{"text":"This is \\underline{underlined} content"}"""),
        new Fx("LaTeX literal: \\textsc smallcaps",  "paragraph",
            """{"text":"Author: \\textsc{Smith} et al."}"""),
        new Fx("LaTeX literal: CV-shape mix",        "paragraph",
            """{"text":"\\noindent\\textbf{Preparatory Classes for Engineering Schools (CPGE)} \\hfill \\textit{2008}\\\\"}"""),
        new Fx("LaTeX literal: \\enquote",           "paragraph",
            """{"text":"\\enquote{Hello, world}"}"""),

        new Fx("heading h1",                 "heading",   """{"text":"Introduction","level":1}"""),
        new Fx("heading strips baked '1. ' prefix", "heading",
            """{"text":"1. Introduction","level":1}"""),
        new Fx("heading strips '1.2.3 ' multi-level prefix", "heading",
            """{"text":"1.2.3 Subsubsection","level":3}"""),
        new Fx("heading strips Roman 'IV. ' prefix", "heading",
            """{"text":"IV. Methods","level":1}"""),
        new Fx("heading h2",                 "heading",   """{"text":"Method","level":2}"""),
        new Fx("heading h3",                 "heading",   """{"text":"Variant","level":3}"""),
        new Fx("heading h6 cap",             "heading",   """{"text":"Deep","level":9}"""),
        new Fx("heading inline bold",        "heading",   """{"text":"**Important** result","level":2}"""),

        // Math fixtures — most originally documented LaTeX→Typst gaps
        // now compile after the LatexMathToTypst expansion. Two-letter
        // variable adjacency ("mc^2", "dx") still trips Typst's
        // single-letter math identifier rule and remains a known gap.
        new Fx("equation display mc^2 (LaTeX→Typst gap)", "equation",  """{"latex":"E = mc^2","mode":"display"}""", ExpectCompile: false),
        new Fx("equation inline plain",      "equation",  """{"latex":"a + b","mode":"inline"}"""),
        new Fx("equation \\frac",            "equation",  """{"latex":"\\frac{a}{b}","mode":"display"}"""),
        new Fx("equation greek bare",        "equation",  """{"latex":"\\alpha + \\beta","mode":"display"}"""),
        new Fx("equation \\sum",             "equation",  """{"latex":"\\sum_{i=1}^n i","mode":"display"}"""),
        new Fx("equation pmatrix",           "equation",  """{"latex":"\\begin{pmatrix} 1 & 0 \\\\ 0 & 1 \\end{pmatrix}","mode":"display"}"""),
        new Fx("equation bmatrix",           "equation",  """{"latex":"\\begin{bmatrix} a & b \\\\ c & d \\end{bmatrix}","mode":"display"}"""),
        new Fx("equation vmatrix det",       "equation",  """{"latex":"\\begin{vmatrix} a & b \\\\ c & d \\end{vmatrix}","mode":"display"}"""),
        new Fx("equation matrix bare",       "equation",  """{"latex":"\\begin{matrix} 1 & 2 \\\\ 3 & 4 \\end{matrix}","mode":"display"}"""),
        new Fx("equation \\quad spacing",    "equation",  """{"latex":"a + b \\quad c + d","mode":"display"}"""),
        new Fx("equation matrix + \\quad",   "equation",  """{"latex":"X = \\begin{pmatrix} 0 & 1 \\\\ 1 & 0 \\end{pmatrix}, \\quad Y = \\begin{pmatrix} 0 & -i \\\\ i & 0 \\end{pmatrix}","mode":"display"}"""),
        new Fx("equation \\frac with nested \\sqrt", "equation", """{"latex":"H = \\frac{1}{\\sqrt{2}}","mode":"display"}"""),
        new Fx("equation nested \\frac",    "equation", """{"latex":"\\frac{\\frac{a}{b}}{c}","mode":"display"}"""),

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
        new Fx("tableofcontents lowercase",  "tableofcontents", """{}"""),
        new Fx("toc alias",                  "toc",       """{}"""),
        new Fx("pageBreak",                  "pageBreak", """{}"""),
        new Fx("pagebreak lowercase",        "pagebreak", """{}"""),
        new Fx("page_break alias",           "page_break","""{}"""),
        new Fx("columnBreak",                "columnBreak","""{}"""),
        new Fx("columnbreak lowercase",      "columnbreak","""{}"""),
        new Fx("divider alias",              "divider",   """{}"""),

        // Bibliography emits #bibliography("references.bib") but the
        // file is only written by the LaTeX export wrapper. Preview
        // compile path falls back to pdflatex, which has the real
        // resolution. Expected fallback for now.
        new Fx("bibliography (no .bib in preview ctx)", "bibliography", """{}""", ExpectCompile: false),

        // figure with external/placeholder URL → Typst can't resolve
        // the file inside the sandbox; we now render a drawn
        // placeholder box so the doc compiles cleanly. Real local
        // assets still flow through #figure(image(...)) (and remain
        // unresolved without the asset write — covered below).
        new Fx("figure (placeholder URL → compile via drawn box)", "figure",
            """{"src":"/api/placeholder/600/350","caption":"Architecture diagram"}"""),
        new Fx("figure (https URL → compile via drawn box)", "figure",
            """{"src":"https://example.com/img.png","caption":"x"}"""),
        new Fx("figure (local path no asset in preview ctx)", "figure",
            """{"src":"img.png","caption":"x"}""", ExpectCompile: false),

        // Math — top LaTeX→Typst translations from telemetry
        new Fx("equation \\mathbb",          "equation",
            """{"latex":"x \\in \\mathbb{R}","mode":"inline"}"""),
        new Fx("paragraph with inline \\mathbb math", "paragraph",
            """{"text":"Let $\\mathbb{R}$ be the real numbers."}"""),
        // Native citation/ref translate to @key — but Typst needs the
        // referenced label to exist in the document for compile to
        // succeed. Standalone-block fixtures don't carry a bib /
        // labelled element, so these are expected fallbacks at the
        // single-block level. PreviewRenderService DOES write
        // references.bib for the full-document path, so production
        // citations resolve correctly (covered by
        // Bibliography_block_compiles_when_references_bib_supplied).
        new Fx("paragraph with native \\cite (no bib in fixture ctx → fallback)", "paragraph",
            """{"text":"As shown in \\cite{smith2024}, the result holds."}""", ExpectCompile: false),
        new Fx("paragraph with native \\ref (no label in fixture ctx → fallback)", "paragraph",
            """{"text":"See Theorem \\ref{thm:main} for the proof."}""", ExpectCompile: false),
        new Fx("paragraph with native \\url",         "paragraph",
            """{"text":"Available at \\url{https://example.com}."}"""),
        new Fx("paragraph with native \\href",        "paragraph",
            """{"text":"See \\href{https://lilia.com}{the docs}."}"""),
        new Fx("paragraph with native \\footnote",    "paragraph",
            """{"text":"This claim\\footnote{See appendix A.} is supported."}"""),
        new Fx("paragraph with display math $$",      "paragraph",
            """{"text":"The identity is: $$a^2 + b^2 = c^2$$ in any right triangle."}"""),
        new Fx("theorem with inline \\mathcal math",  "theorem",
            """{"theoremType":"theorem","content":"For Hilbert space $\\mathcal{H}^2$, ...","numbered":true}"""),
        new Fx("paragraph with inline \\sum math",    "paragraph",
            """{"text":"Compute $\\sum_{i=1}^n a_i$ over the index."}"""),
        // \\int f(x) dx — "dx" is two letters touching; LaTeX parses
        // as `d` + `x`, Typst as identifier `dx`. Real fallback case.
        new Fx("paragraph with inline \\int dx (LaTeX→Typst gap)", "paragraph",
            """{"text":"Compute $\\int f(x) dx$ over the interval."}""", ExpectCompile: false),
        new Fx("equation \\mathcal",         "equation",
            """{"latex":"\\mathcal{H}^2","mode":"inline"}"""),
        new Fx("equation \\mathbf",          "equation",
            """{"latex":"\\mathbf{v} \\cdot \\mathbf{w}","mode":"inline"}"""),
        new Fx("equation \\mathrm differential","equation",
            """{"latex":"\\int f(x) \\mathrm{d}x","mode":"display"}"""),
        new Fx("equation \\text in math",    "equation",
            """{"latex":"x = y \\text{ if and only if } z","mode":"display"}"""),

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

    /// <summary>
    /// Bibliography block compiles cleanly when a references.bib
    /// asset is supplied alongside main.typ. This is the path
    /// PreviewRenderService.TryTypstPdfAsync wires up — load bib
    /// entries from DB, serialize via BibTeXSerializer, hand to
    /// CompileAsync as an asset file. Without the asset the directive
    /// errors with "file not found" (covered by the ExpectCompile=false
    /// fixture below).
    /// </summary>
    [Fact]
    public async Task Bibliography_block_compiles_when_references_bib_supplied()
    {
        if (!TypstAvailable()) return;

        var exporter = new TypstExportService();
        var compiler = new TypstCompileService();

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Fixture-bib",
            OwnerId = "test-user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Type = "bibliography",
            Content = JsonDocument.Parse("""{}"""),
            SortOrder = 0,
        };

        var source = exporter.BuildTypstDocument(doc, new List<Block> { block });

        // Minimal valid BibTeX entry — gets written as references.bib
        // alongside main.typ, which is what the directive resolves to.
        const string bib = """
            @article{smith2024,
              author = {Smith, J.},
              title = {On Things},
              year = {2024},
            }
            """;
        var assets = new Dictionary<string, string> { ["references.bib"] = bib };

        var result = await compiler.CompileAsync(source, TypstOutputFormat.Pdf, assets);

        result.Success.Should().BeTrue($"compile failed: {result.Error}\n--- source ---\n{source}");
        result.Output.Should().NotBeNullOrEmpty();
        var head = System.Text.Encoding.ASCII.GetString(result.Output!.AsSpan(0, 4).ToArray());
        head.Should().StartWith("%PDF");
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
