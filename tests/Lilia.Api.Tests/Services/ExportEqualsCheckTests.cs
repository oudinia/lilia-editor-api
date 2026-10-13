using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Run 2's finding (MCP-76): fixes landed in the check emitter, and the export kept its own block
/// renderers, so a clean check could ship a PDF with "Listing ??" and [?]. The export now renders
/// every body block with the check emitter; this holds it there, block type by block type, with the
/// run-2 documents' own contents.
/// </summary>
public class ExportEqualsCheckTests
{
    private readonly RenderService _render = new(null!, NullLogger<RenderService>.Instance);

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Document Doc(string cls, params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = cls,
            Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList(),
            BibliographyEntries = new List<BibliographyEntry>(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        foreach (var b in doc.Blocks) { b.Document = doc; b.DocumentId = doc.Id; }
        return doc;
    }

    private static string Export(Document doc) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions());

    public static TheoryData<string, string> Cases() => new()
    {
        { "paragraph", """{"text":"Mixed **bold**, $x^2$, \\emph{em}, @cite{knuth84} and Theorem~(thm:a)."}""" },
        { "heading", """{"text":"1. Results {#sec:res}","level":1}""" },
        { "equation", """{"latex":"\\begin{align} a &= b \\\\ c &= d \\end{align}","equationMode":"display","label":"eq:sys"}""" },
        { "equation", """{"latex":"x = 1 \\label{eq:x}","label":"eq:x","mode":"display"}""" },
        { "code", """{"code":"print('Zürich')","language":"python","caption":"Listing with $x$","label":"lst:py"}""" },
        { "list", """{"items":["one $a$","two **b**"],"ordered":true}""" },
        { "blockquote", """{"text":"The engine weaves.","attribution":"Ada Lovelace"}""" },
        { "theorem", """{"theoremType":"lemma","title":"Bound on $C[0,1]$","text":"Holds.","numbered":false}""" },
        { "table", """{"caption":"Yields","label":"tab:y","headers":["Compound","Yield"],"rows":[["\\ce{C7H6O3}","**82**"],["$\\alpha$","5"]]}""" },
        { "algorithm", """{"caption":"Sum","label":"alg:sum","lines":[{"kind":"require","text":"$n \\ge 0$"},{"kind":"for","text":"$i = 1$ to $n$"},{"kind":"statement","text":"$s \\gets s + i$"},{"kind":"endfor","text":""},{"kind":"return","text":"$s$"}]}""" },
        { "algorithm", """{"code":"\\STATE $x \\gets 0$\n\\WHILE{$x < n$}\n\\STATE $x \\gets x + 1$\n\\ENDWHILE"}""" },
        { "embed", """{"code":"\\begin{center}Raw \\LaTeX\\end{center}"}""" },
        { "callout", """{"variant":"note","title":"Note","text":"Mind $x$."}""" },
        { "footnote", """{"text":"A note."}""" },
        { "frontMatter", """{"subType":"dedication","text":"To my parents."}""" },
        { "backMatter", """{"subType":"appendix","title":"Derivations","text":"Proof of $x$."}""" },
        { "columnBreak", "{}" },
        { "pageBreak", "{}" },
        { "tableOfContents", "{}" },
        { "personalInfo", """{"name":"Ada","email":"ada@example.org"}""" },
        { "cvEntry", """{"title":"Engineer","organization":"Analytical Engines","startDate":"1843"}""" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_export_prints_what_the_check_prints(string type, string json)
    {
        foreach (var cls in new[] { "article", "book" })
        {
            var block = B(type, JsonSerializer.Deserialize<JsonElement>(json));
            var doc = Doc(cls, block);
            var check = _render.RenderBlockToLatex(block).Trim();
            check.Should().NotStartWith("% Unknown block type");
            Export(doc).Should().Contain(check, $"the export of a {type} block in a {cls} must print what the check prints");
        }
    }

    // MCP-76 / MCP-04: references written as text in the bibliography block reach the export's .bib.
    [Fact]
    public void Text_references_reach_the_bib()
    {
        var bib = B("bibliography", new { text = "@cite[knuth84] Knuth, D. — The TeXbook\n@cite[lamport94] Lamport, L. — LaTeX" });
        var entries = LaTeXExportService.WithTextEntries([bib], []);
        entries.Select(e => e.CiteKey).Should().Equal("knuth84", "lamport94");
        entries[0].EntryType.Should().Be("misc");
        entries[0].Data.RootElement.GetProperty("note").GetString().Should().Contain("The TeXbook");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_run2_blocks_compile_on_export()
    {
        var blocks = Cases().Select(c => B((string)c[0], JsonSerializer.Deserialize<JsonElement>((string)c[1])))
            .Where(b => b.Type is not ("personalInfo" or "cvEntry"))
            .Prepend(B("theorem", new { theoremType = "theorem", text = "A.", label = "thm:a" }))
            .ToArray();
        var tex = Export(Doc("article", blocks));
        new EmittedLatexAudit(tex).DanglingReferences.Should().BeEmpty("every reference in these blocks has its label on export too");
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);
        pdf.Should().NotBeEmpty();
    }

    // R1 / MCP-81: BibTeX in the bibliography block, as the help teaches, prints and resolves.
    private const string Bibtex = """
        @article{vaswani2017attention,
          author = {Vaswani, Ashish and others},
          title  = {Attention Is All You Need},
          year   = {2017}
        }
        """;

    [Fact]
    public void Bibtex_in_the_bibliography_block_prints_on_both()
    {
        var doc = Doc("article",
            B("paragraph", new { text = @"Transformers \citep{vaswani2017attention} changed things; see also @cite[knuth84]." }),
            B("bibliography", new { text = Bibtex }));
        var check = _render.RenderToLatex(doc);
        check.Should().Contain(@"\bibitem").And.Contain("vaswani2017attention").And.Contain("Attention Is All You Need");
        check.Should().Contain(@"\cite{knuth84}");
        new EmittedLatexAudit(check).MissingCitations.Select(c => c.Key).Should().Equal("knuth84");

        var entries = LaTeXExportService.WithTextEntries(doc.Blocks, []);
        entries.Should().ContainSingle(e => e.CiteKey == "vaswani2017attention" && e.EntryType == "article");
    }

    [Fact]
    public void The_old_bracket_forms_are_read() =>
        _render.RenderBlockToLatex(B("paragraph", new { text = "See @ref[sec:a] and @cite[knuth84,lamport94]." }))
            .Should().Contain(@"\ref{sec:a}").And.Contain(@"\cite{knuth84,lamport94}");

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task Bibtex_in_the_block_compiles_with_natbib()
    {
        var doc = Doc("article",
            B("paragraph", new { text = @"Transformers \citep{vaswani2017attention} and \citet{vaswani2017attention}." }),
            B("bibliography", new { text = Bibtex }));
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfAsync(_render.RenderToLatex(doc), "pdflatex", timeout: 180);
        pdf.Should().NotBeEmpty();
    }

    // MCP-41 (run 2 re-test, LTX-83): the appendix block printed only \appendix.
    [Fact]
    public void The_appendix_prints_its_title_and_text()
    {
        var book = Doc("book", B("backMatter", new { subType = "appendix", title = "Derivations", text = "Proof of the bound." }));
        foreach (var tex in new[] { _render.RenderToLatex(book), Export(book) })
            tex.Should().Contain("\\appendix\n\\chapter{Derivations}").And.Contain("Proof of the bound.");
    }
}
