using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Tier 1 of the Typst export defence: per-(block, content) fixture
/// assertions on the produced .typ source. Mirror of
/// LatexExportFixtureTests.
///
/// Each fixture is a single block routed through TypstExportService.
/// We assert expected substrings + no fallback marker. The fixture
/// shape matches Tier 1 preview render harness, so adding a new edge
/// case is one entry — JSON in, expected substrings out.
/// </summary>
public class TypstExportFixtureTests
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
        new Fx("paragraph: **bold** → *bold* (Typst syntax)",
            "paragraph", """{"text":"This is **bold** text"}""",
            ExpectIn: new[] { "*bold*" },
            ExpectNotIn: new[] { "**bold**" }),
        new Fx("paragraph: *italic* → _italic_",
            "paragraph", """{"text":"This is *italic* text"}""",
            ExpectIn: new[] { "_italic_" }),

        // heading — Typst uses '=' marker (level 1), '==' (level 2), etc.
        new Fx("heading: level 1",
            "heading", """{"text":"Introduction","level":1}""",
            ExpectIn: new[] { "= Introduction" }),
        new Fx("heading: level 2",
            "heading", """{"text":"Method","level":2}""",
            ExpectIn: new[] { "== Method" }),
        new Fx("heading: level 3",
            "heading", """{"text":"Variant","level":3}""",
            ExpectIn: new[] { "=== Variant" }),
        new Fx("heading: level capped at 6",
            "heading", """{"text":"Deep","level":9}""",
            ExpectIn: new[] { "====== Deep" }),
        new Fx("heading: bold inline rendered",
            "heading", """{"text":"**Important** Title","level":2}""",
            ExpectIn: new[] { "*Important*" }),

        // equation
        new Fx("equation: display mode",
            "equation", """{"latex":"E = mc^2","mode":"display"}""",
            ExpectIn: new[] { "$ E = mc^2 $" }),
        new Fx("equation: inline mode",
            "equation", """{"latex":"x = 1","mode":"inline"}""",
            ExpectIn: new[] { "$x = 1$" }),

        // figure
        new Fx("figure: src + caption",
            "figure", """{"src":"img.png","caption":"A figure"}""",
            ExpectIn: new[] { "#figure(image(", "img.png", "A figure" }),
        new Fx("figure: missing src degrades gracefully",
            "figure", """{"caption":"no src"}""",
            ExpectIn: new[] { "Figure without source" }),

        // table
        new Fx("table: 2x2 with header",
            "table",
            """{"rows":[["A","B"],["1","2"]],"hasHeader":true}""",
            ExpectIn: new[] { "#table(", "columns: 2", "table.header(", "[A]", "[B]", "[1]", "[2]" }),
        new Fx("table: empty rows",
            "table", """{"rows":[]}""",
            ExpectIn: new[] { "Empty table" }),

        // code
        new Fx("code: typescript with body",
            "code", """{"code":"const x = 1;","language":"typescript"}""",
            ExpectIn: new[] { "```typescript", "const x = 1;", "```" }),
        new Fx("code: empty body",
            "code", """{"code":"","language":""}""",
            ExpectIn: new[] { "```" }),

        // list
        new Fx("list: bullet items",
            "list", """{"items":["alpha","beta"],"ordered":false}""",
            ExpectIn: new[] { "- alpha", "- beta" }),
        new Fx("list: ordered items",
            "list", """{"items":["one","two"],"ordered":true}""",
            ExpectIn: new[] { "+ one", "+ two" }),
        new Fx("list: bold in item",
            "list", """{"items":["**important** result"]}""",
            ExpectIn: new[] { "*important*", "- " },
            ExpectNotIn: new[] { "**important**" }),

        // blockquote
        new Fx("blockquote: text",
            "blockquote", """{"text":"to be or not to be"}""",
            ExpectIn: new[] { "#quote(block: true)[", "to be or not to be" }),

        // theorem
        new Fx("theorem: kind + title + body",
            "theorem", """{"kind":"theorem","title":"Pythagoras","text":"a^2 + b^2 = c^2"}""",
            ExpectIn: new[] { "#block(", "*Theorem (Pythagoras).*" }),
        new Fx("theorem: lemma kind",
            "theorem", """{"kind":"lemma","text":"X is Y"}""",
            ExpectIn: new[] { "*Lemma.*" }),

        // abstract
        new Fx("abstract: emphasised block",
            "abstract", """{"text":"We propose X"}""",
            ExpectIn: new[] { "#block(", "Abstract" }),

        // bibliography
        new Fx("bibliography: #bibliography call",
            "bibliography", """{}""",
            ExpectIn: new[] { "#bibliography(\"references.bib\")" }),

        // tableOfContents
        new Fx("tableOfContents: #outline()",
            "tableOfContents", """{}""",
            ExpectIn: new[] { "#outline()" }),
        new Fx("toc alias",
            "toc", """{}""",
            ExpectIn: new[] { "#outline()" }),

        // pageBreak
        new Fx("pageBreak: #pagebreak()",
            "pageBreak", """{}""",
            ExpectIn: new[] { "#pagebreak()" }),
        new Fx("page_break alias",
            "page_break", """{}""",
            ExpectIn: new[] { "#pagebreak()" }),

        // columnBreak
        new Fx("columnBreak: #colbreak()",
            "columnBreak", """{}""",
            ExpectIn: new[] { "#colbreak()" }),

        // aliases
        new Fx("alias: header → heading",
            "header", """{"text":"Alias","level":1}""",
            ExpectIn: new[] { "= Alias" }),
        new Fx("alias: image → figure",
            "image", """{"src":"img.png","caption":"x"}""",
            ExpectIn: new[] { "#figure(image(" }),
        new Fx("alias: divider → #line",
            "divider", """{}""",
            ExpectIn: new[] { "#line(length: 100%)" }),

        // unknown block type → fallback marker (Tier 3 telemetry)
        new Fx("REGRESSION: unknown block falls to comment marker",
            "totally-unknown", """{"text":"ignored"}""",
            ExpectIn: new[] { "Unsupported block type for Typst" }),

        // escape edge cases
        new Fx("escape: dollar sign in paragraph escaped",
            "paragraph", """{"text":"Price is $5"}""",
            ExpectIn: new[] { "\\$5" }),
        new Fx("escape: hashtag at start escaped",
            "paragraph", """{"text":"Use #hashtag here"}""",
            ExpectIn: new[] { "\\#hashtag" }),
    }.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Block_exports_to_typst_with_expected_shape(Fx f)
    {
        var service = new TypstExportService();
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = f.BlockType,
            Content = JsonDocument.Parse(f.ContentJson),
            SortOrder = 0,
        };

        var output = service.RenderBlockForTest(block);

        if (f.ExpectIn != null)
        {
            foreach (var s in f.ExpectIn)
                output.Should().Contain(s, $"[{f.Name}] expected `{s}` in Typst");
        }
        if (f.ExpectNotIn != null)
        {
            foreach (var s in f.ExpectNotIn)
                output.Should().NotContain(s, $"[{f.Name}] did not expect `{s}` in Typst");
        }
    }
}
