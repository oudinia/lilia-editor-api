using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Tier 1 of the LaTeX export defence: per-(block, content) fixture
/// assertions on the exported `.tex` source.
///
/// Mirror of BlockRenderer.render.test.tsx on the editor side: each
/// fixture is a single block flowing through the real exporter.
/// Failure surfaces are kept tight — assert expected substrings + no
/// `[Unsupported]` placeholder + no silent fallback.
///
/// Catches regressions like:
///   - "added new block type, exporter falls back to comment"
///   - "heading inline markdown lost on export" (parity with preview)
///   - "list items exported as plain text instead of \item"
/// </summary>
public class LatexExportFixtureTests
{
    public sealed record Fx(
        string Name,
        string BlockType,
        string ContentJson,
        string[]? ExpectIn = null,
        string[]? ExpectNotIn = null);

    public static IEnumerable<object[]> Fixtures() => new[]
    {
        // paragraph
        new Fx("paragraph: plain text",
            "paragraph", """{"text":"hello world"}""",
            ExpectIn: new[] { "hello world" }),
        new Fx("paragraph: bold markdown",
            "paragraph", """{"text":"This is **bold** text"}""",
            ExpectIn: new[] { @"\textbf{bold}" },
            ExpectNotIn: new[] { "**bold**" }),
        new Fx("paragraph: italic markdown",
            "paragraph", """{"text":"This is *italic* text"}""",
            ExpectIn: new[] { @"\textit{italic}" }),

        // heading
        new Fx("heading: level 1",
            "heading", """{"text":"Introduction","level":1}""",
            ExpectIn: new[] { @"\section{Introduction}" }),
        new Fx("heading: level 2",
            "heading", """{"text":"Method","level":2}""",
            ExpectIn: new[] { @"\subsection{Method}" }),
        new Fx("heading: level 3",
            "heading", """{"text":"Variant","level":3}""",
            ExpectIn: new[] { @"\subsubsection{Variant}" }),

        // equation
        new Fx("equation: simple latex",
            "equation", """{"latex":"E = mc^2"}""",
            ExpectIn: new[] { "E = mc^2" }),

        // figure
        new Fx("figure: src + caption",
            "figure", """{"src":"img.png","caption":"A figure"}""",
            ExpectIn: new[] { @"\begin{figure}", "img.png", "A figure", @"\end{figure}" }),

        // table
        new Fx("table: 2x2 rows",
            "table",
            """{"rows":[["A","B"],["1","2"]]}""",
            ExpectIn: new[] { @"\begin{tabular}", "A", "B", "1", "2", @"\end{tabular}" }),

        // code
        new Fx("code: typescript body",
            "code", """{"code":"const x = 1;","language":"typescript"}""",
            ExpectIn: new[] { @"\begin{lstlisting}", "const x = 1;", @"\end{lstlisting}" }),

        // list
        new Fx("list: bullet items",
            "list", """{"items":["alpha","beta","gamma"],"ordered":false}""",
            ExpectIn: new[] { @"\begin{itemize}", @"\item alpha", @"\item beta", @"\end{itemize}" }),
        new Fx("list: numbered items",
            "list", """{"items":["one","two"],"ordered":true}""",
            ExpectIn: new[] { @"\begin{enumerate}", @"\item one", @"\item two", @"\end{enumerate}" }),

        // Nested lists — recursive children walk (parity with the
        // recursive HTML renderer + Typst exporter; LaTeX exporter
        // used to flatten silently and lose the nesting).
        new Fx("list nested 2 levels",
            "list", """{"ordered":false,"items":[{"text":"top","children":[{"text":"sub-1"},{"text":"sub-2"}]},{"text":"top-2"}]}""",
            ExpectIn: new[] {
                @"\item top",
                @"\begin{itemize}",
                @"\item sub-1",
                @"\item sub-2",
                @"\end{itemize}",
                @"\item top-2",
            }),
        new Fx("list nested 3 levels",
            "list", """{"ordered":true,"items":[{"text":"a","children":[{"text":"a.1","children":[{"text":"a.1.i"}]}]}]}""",
            ExpectIn: new[] {
                @"\begin{enumerate}",
                @"\item a",
                @"\item a.1",
                @"\item a.1.i",
            }),

        // labelFormat + start — enumitem options. Parity with
        // RenderService.RenderListToLatex so the PDF (export) and the
        // /preview/latex panel agree on numbering style.
        new Fx("list: labelFormat alpha → (a)",
            "list", """{"ordered":true,"labelFormat":"alpha","items":["x","y"]}""",
            ExpectIn: new[] { @"\begin{enumerate}[label=(\alph*)]", @"\item x", @"\item y" }),
        new Fx("list: labelFormat Alpha → (A)",
            "list", """{"ordered":true,"labelFormat":"Alpha","items":["x"]}""",
            ExpectIn: new[] { @"\begin{enumerate}[label=(\Alph*)]" }),
        new Fx("list: labelFormat roman → (i)",
            "list", """{"ordered":true,"labelFormat":"roman","items":["x"]}""",
            ExpectIn: new[] { @"\begin{enumerate}[label=(\roman*)]" }),
        new Fx("list: labelFormat Roman → (I)",
            "list", """{"ordered":true,"labelFormat":"Roman","items":["x"]}""",
            ExpectIn: new[] { @"\begin{enumerate}[label=(\Roman*)]" }),
        new Fx("list: start=3",
            "list", """{"ordered":true,"start":3,"items":["x","y"]}""",
            ExpectIn: new[] { @"\begin{enumerate}[start=3]" }),
        new Fx("list: labelFormat + start combined",
            "list", """{"ordered":true,"labelFormat":"Alpha","start":3,"items":["x"]}""",
            ExpectIn: new[] { @"\begin{enumerate}[label=(\Alph*), start=3]" }),
        new Fx("list: labelFormat=number emits no enumitem options",
            "list", """{"ordered":true,"labelFormat":"number","items":["x"]}""",
            ExpectIn: new[] { @"\begin{enumerate}", @"\item x" },
            ExpectNotIn: new[] { "label=", "start=" }),
        new Fx("list: unordered ignores labelFormat",
            "list", """{"ordered":false,"labelFormat":"Alpha","start":3,"items":["x"]}""",
            ExpectIn: new[] { @"\begin{itemize}" },
            ExpectNotIn: new[] { "label=", "start=" }),

        // Phase 2 — description lists (`kind: "description"`).
        // Emits \begin{description} \item[<term>] <desc> \end{description}.
        // labelFormat + start are ignored when kind is description.
        new Fx("list: description — basic term/desc",
            "list", """{"kind":"description","items":[{"text":"paralist","description":"compact lists and inline lists"},{"text":"enumitem","description":"control labels and lengths"}]}""",
            ExpectIn: new[] {
                @"\begin{description}",
                @"\item[paralist] compact lists and inline lists",
                @"\item[enumitem] control labels and lengths",
                @"\end{description}",
            },
            ExpectNotIn: new[] { @"\begin{itemize}", @"\begin{enumerate}", "label=", "start=" }),
        new Fx("list: description preserves inline bold in desc",
            "list", """{"kind":"description","items":[{"text":"important","description":"**very** important note"}]}""",
            ExpectIn: new[] {
                @"\item[important]",
                @"\textbf{very}",
            },
            ExpectNotIn: new[] { "**very**" }),
        new Fx("list: description with kind overrides ordered=true",
            "list", """{"kind":"description","ordered":true,"items":[{"text":"a","description":"alpha"}]}""",
            ExpectIn: new[] { @"\begin{description}", @"\item[a] alpha" },
            ExpectNotIn: new[] { @"\begin{enumerate}" }),
        new Fx("list: description missing description field renders empty body",
            "list", """{"kind":"description","items":[{"text":"orphan"}]}""",
            ExpectIn: new[] { @"\begin{description}", @"\item[orphan]" }),

        // blockquote
        new Fx("blockquote: text",
            "blockquote", """{"text":"to be or not to be"}""",
            ExpectIn: new[] { "to be or not to be" }),

        // theorem
        new Fx("theorem: kind + body",
            "theorem", """{"kind":"theorem","title":"Pythagoras","text":"a^2 + b^2 = c^2"}""",
            ExpectIn: new[] { "Pythagoras" }),

        // pageBreak
        new Fx("pageBreak: \\newpage",
            "pageBreak", """{}""",
            ExpectIn: new[] { @"\newpage" }),
        new Fx("pagebreak (lowercase legacy form)",
            "pagebreak", """{}""",
            ExpectIn: new[] { @"\newpage" },
            ExpectNotIn: new[] { "Unsupported block type" }),

        // tableOfContents
        new Fx("tableOfContents: \\tableofcontents",
            "tableOfContents", """{}""",
            ExpectIn: new[] { @"\tableofcontents" }),
        new Fx("tableofcontents (lowercase legacy form)",
            "tableofcontents", """{}""",
            ExpectIn: new[] { @"\tableofcontents" },
            ExpectNotIn: new[] { "Unsupported block type" }),

        // columnBreak — lowercase legacy form
        new Fx("columnbreak (lowercase legacy form)",
            "columnbreak", """{}""",
            ExpectIn: new[] { @"\columnbreak" },
            ExpectNotIn: new[] { "Unsupported block type" }),

        // Native LaTeX commands users type directly — must NOT be
        // escaped by EscapeLatex. Without these placeholder rules,
        // "\cite{X}" turns into "\textbackslash{}cite\{X\}" and renders
        // as literal text in the PDF instead of a resolved citation.
        new Fx("native \\cite preserved",
            "paragraph", """{"text":"As in \\cite{smith2024}."}""",
            ExpectIn: new[] { @"\cite{smith2024}" },
            ExpectNotIn: new[] { @"\textbackslash" }),
        new Fx("native \\ref preserved",
            "paragraph", """{"text":"See Theorem \\ref{thm:x}."}""",
            ExpectIn: new[] { @"\ref{thm:x}" },
            ExpectNotIn: new[] { @"\textbackslash" }),
        new Fx("native \\url preserved",
            "paragraph", """{"text":"At \\url{https://x.com}."}""",
            ExpectIn: new[] { @"\url{https://x.com}" }),
        new Fx("native \\href preserved",
            "paragraph", """{"text":"See \\href{https://x.com}{here}."}""",
            ExpectIn: new[] { @"\href{https://x.com}{here}" }),
        new Fx("native \\footnote preserved",
            "paragraph", """{"text":"X\\footnote{Y}."}""",
            ExpectIn: new[] { @"\footnote{Y}" }),
        new Fx("display math $$ preserved",
            "paragraph", """{"text":"$$a^2+b^2=c^2$$"}""",
            ExpectIn: new[] { "$$a^2+b^2=c^2$$" }),

        // Heading numbering — baked-in legacy prefixes get stripped so
        // LaTeX auto-numbering doesn't double up.
        new Fx("heading strips baked '1. ' prefix",
            "heading", """{"text":"1. Introduction","level":1}""",
            ExpectIn: new[] { @"\section{Introduction}" },
            ExpectNotIn: new[] { "1. Introduction" }),
        new Fx("heading strips '1.2 ' multi-level prefix",
            "heading", """{"text":"1.2 Subsection","level":2}""",
            ExpectIn: new[] { @"\subsection{Subsection}" },
            ExpectNotIn: new[] { "1.2 Subsection" }),
        new Fx("heading strips Roman 'IV. ' prefix",
            "heading", """{"text":"IV. Methods","level":1}""",
            ExpectIn: new[] { @"\section{Methods}" }),
        new Fx("heading does NOT strip non-numeric leading words",
            "heading", """{"text":"Future Work","level":2}""",
            ExpectIn: new[] { @"\subsection{Future Work}" }),

        // === regressions / leak guards ===
        new Fx("REGRESSION: list item with bold markdown survives to \\textbf",
            "list", """{"items":["**important** result","plain"]}""",
            ExpectIn: new[] { @"\textbf{important}", @"\item" },
            ExpectNotIn: new[] { "**important**" }),
        new Fx("REGRESSION: paragraph with newline preserved",
            "paragraph", """{"text":"first line\nsecond line"}""",
            ExpectIn: new[] { "first line", "second line" }),
        new Fx("REGRESSION: heading with bold inline",
            "heading", """{"text":"**Important** Title","level":2}""",
            ExpectIn: new[] { @"\textbf{Important}" },
            ExpectNotIn: new[] { "**Important**" }),
        new Fx("REGRESSION: unknown block type falls to comment marker (not silent)",
            "totally-unknown", """{"text":"ignored"}""",
            ExpectIn: new[] { "Unsupported block type" }),
    }.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Block_exports_to_latex_with_expected_shape(Fx f)
    {
        // BuildSingleFileLatex bypasses the DB + storage, mirroring the
        // pattern in LatexRoundtripTests (in-memory test entry point).
        var service = new LaTeXExportService(
            context: null!,
            storageService: null!,
            logger: NullLogger<LaTeXExportService>.Instance);

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            Language = "en",
            PaperSize = "a4",
            FontFamily = "serif",
        };
        var blocks = new List<Block>
        {
            new()
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                Type = f.BlockType,
                Content = JsonDocument.Parse(f.ContentJson),
                SortOrder = 0,
            }
        };

        var output = service.BuildSingleFileLatex(
            doc, blocks, new List<BibliographyEntry>(),
            new LaTeXExportOptions());

        if (f.ExpectIn != null)
        {
            foreach (var s in f.ExpectIn)
            {
                output.Should().Contain(s,
                    $"[{f.Name}] expected `{s}` in exported .tex");
            }
        }
        if (f.ExpectNotIn != null)
        {
            foreach (var s in f.ExpectNotIn)
            {
                output.Should().NotContain(s,
                    $"[{f.Name}] did not expect `{s}` in exported .tex");
            }
        }
    }

}
