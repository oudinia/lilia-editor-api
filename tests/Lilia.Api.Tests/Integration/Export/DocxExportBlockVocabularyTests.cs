using System.Net;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// Every block type an article or report is built from, stored the way the
/// application stores it and read back out of the finished .docx.
///
/// <para>Scope is deliberate: articles and reports. The CV, slide and
/// front/back-matter vocabulary exists in the editor but is not what this
/// export is being sold for yet, so those types are held to a weaker promise —
/// they must not break the document — rather than to a rendering.</para>
///
/// <para>The bar for the in-scope types is that <b>content does not silently
/// disappear</b>. Four separate defects found on 2026-09-09 were all of that
/// shape: the title block, the bibliography block, nested list children, and
/// equation blocks saved under the current key each vanished without an error
/// anywhere. Every one of them would have been caught by asserting that what
/// went into the database comes out of the file.</para>
/// </summary>
[Collection("Integration")]
public class DocxExportBlockVocabularyTests : IntegrationTestBase
{
    private readonly string _userId = $"docx-v-{Guid.NewGuid():N}"[..28];

    public DocxExportBlockVocabularyTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    // ── harness ──────────────────────────────────────────────────────────

    private async Task<(HttpStatusCode Status, string Text, string Xml)> ExportAsync(Guid docId)
    {
        var response = await CreateClientAs(_userId).GetAsync($"/api/documents/{docId}/export/docx");
        if (response.StatusCode != HttpStatusCode.OK)
            return (response.StatusCode, "", "");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);
        var xml = word.MainDocumentPart!.Document.OuterXml;
        return (response.StatusCode, Regex.Replace(xml, "<[^>]+>", ""), xml);
    }

    /// <summary>One block of the given type, plus a sentinel paragraph after it.</summary>
    private async Task<(HttpStatusCode Status, string Text, string Xml)> ExportOneAsync(
        string type, string contentJson)
    {
        var doc = await SeedDocumentAsync(_userId, $"Vocabulary: {type}");
        await SeedBlockAsync(doc.Id, type, contentJson, 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"SENTINEL AFTER."}""", 1);
        return await ExportAsync(doc.Id);
    }

    // ── the front matter of a paper ──────────────────────────────────────

    [Fact]
    public async Task TitleBlockCarriesTitleAuthorAndDate()
    {
        var (status, text, _) = await ExportOneAsync("title",
            """{"title":"On Convergence","author":"A. Researcher","date":"March 2026"}""");

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("On Convergence");
        text.Should().Contain("A. Researcher", "a paper without its author is not a paper");
        text.Should().Contain("March 2026");
    }

    [Fact]
    public async Task TodayIsResolvedToAnActualDate()
    {
        var (_, text, _) = await ExportOneAsync("title",
            """{"title":"Dated","author":"Someone","date":"\\today"}""");

        text.Should().NotContain(@"\today", "LaTeX control sequences must not reach the reader");
        text.Should().Contain(DateTime.Now.Year.ToString());
    }

    [Fact]
    public async Task ATitleWithNoAuthorStillExports()
    {
        var (status, text, _) = await ExportOneAsync("title", """{"title":"Anonymous Work"}""");

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("Anonymous Work");
    }

    [Fact]
    public async Task Abstract()
    {
        var (_, text, _) = await ExportOneAsync("abstract",
            """{"text":"We show that the method converges."}""");

        text.Should().Contain("Abstract").And.Contain("We show that the method converges.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task HeadingsAtEveryLevelAPaperUses(int level)
    {
        var (_, text, _) = await ExportOneAsync("heading",
            $$"""{"text":"Section at level {{level}}","level":{{level}}}""");

        text.Should().Contain($"Section at level {level}");
    }

    [Fact]
    public async Task TableOfContents()
    {
        var (status, text, _) = await ExportOneAsync("tableofcontents", """{}""");

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("SENTINEL AFTER.");
    }

    // ── the body ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ParagraphWithEveryInlineMark()
    {
        var (_, text, xml) = await ExportOneAsync("paragraph", """
            {"text":"placeholder","richText":[
              {"text":"bold ","bold":true},
              {"text":"italic ","italic":true},
              {"text":"underlined ","underline":true},
              {"text":"struck ","strikethrough":true},
              {"text":"highlighted ","highlight":"yellow"},
              {"text":"super","superscript":true},
              {"text":"sub","subscript":true}
            ]}
            """);

        text.Should().Contain("bold").And.Contain("italic").And.Contain("underlined");
        text.Should().Contain("struck").And.Contain("highlighted");
        xml.Should().MatchRegex("<w:b ?/>").And.MatchRegex("<w:i ?/>");
        xml.Should().Contain("superscript").And.Contain("subscript");
    }

    [Fact]
    public async Task InlineMathBecomesWordMathNotDollarSigns()
    {
        var (_, text, xml) = await ExportOneAsync("paragraph",
            """{"text":"Given $E = mc^2$ we proceed."}""");

        xml.Should().Contain("oMath");
        text.Should().NotContain("$");
        text.Should().Contain("we proceed.");
    }

    [Fact]
    public async Task DisplayMathInsideAParagraph()
    {
        var (_, text, xml) = await ExportOneAsync("paragraph",
            """{"text":"It follows that $$\\int_0^1 x\\,dx = \\tfrac12$$ exactly."}""");

        xml.Should().Contain("oMath");
        text.Should().NotContain("$$");
        text.Should().Contain("exactly.");
    }

    [Fact]
    public async Task EquationBlockUnderTheCurrentKey()
    {
        var (_, _, xml) = await ExportOneAsync("equation",
            """{"source":"\\sum_{i=1}^{n} x_i","displayMode":true}""");

        xml.Should().Contain("oMath");
    }

    [Fact]
    public async Task EquationBlockUnderTheLegacyKey()
    {
        // Documents written before the rename still export.
        var (_, _, xml) = await ExportOneAsync("equation",
            """{"latex":"\\sum_{i=1}^{n} x_i","displayMode":true}""");

        xml.Should().Contain("oMath");
    }

    [Fact]
    public async Task AnEquationTooComplexToConvertDoesNotLoseTheDocument()
    {
        var (status, text, _) = await ExportOneAsync("equation",
            """{"source":"\\begin{unknownenv} x \\end{unknownenv}","displayMode":true}""");

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("SENTINEL AFTER.");
    }

    [Theory]
    [InlineData("theorem")]
    [InlineData("lemma")]
    [InlineData("corollary")]
    [InlineData("proposition")]
    [InlineData("definition")]
    [InlineData("proof")]
    public async Task TheoremEnvironmentsAPaperUses(string kind)
    {
        var (_, text, _) = await ExportOneAsync("theorem",
            $$"""{"text":"The statement of the {{kind}}.","theoremType":"{{kind}}"}""");

        text.Should().Contain($"The statement of the {kind}.");
    }

    [Fact]
    public async Task CodeBlockKeepsItsContentsVerbatim()
    {
        var (_, text, _) = await ExportOneAsync("code",
            """{"code":"def f(x):\n    return x * 2","language":"python"}""");

        text.Should().Contain("def f(x):").And.Contain("return x * 2");
    }

    [Fact]
    public async Task Blockquote()
    {
        var (_, text, _) = await ExportOneAsync("blockquote",
            """{"text":"All models are wrong, but some are useful."}""");

        text.Should().Contain("All models are wrong");
    }

    [Fact]
    public async Task Callout()
    {
        var (_, text, _) = await ExportOneAsync("callout",
            """{"text":"Note that the constant is dimensionless."}""");

        text.Should().Contain("dimensionless");
    }

    [Fact]
    public async Task AlgorithmUnderItsUsualKey()
    {
        var (_, text, _) = await ExportOneAsync("algorithm",
            """{"title":"Gradient descent","code":"1. Initialise. 2. Iterate until convergence."}""");

        text.Should().Contain("Iterate until convergence");
    }

    [Fact]
    public async Task AlgorithmWrittenWithTextInstead()
    {
        // Documents written by other paths carry the steps under "text".
        var (_, text, _) = await ExportOneAsync("algorithm",
            """{"text":"1. Initialise. 2. Iterate until convergence."}""");

        text.Should().Contain("Iterate until convergence");
    }

    [Fact]
    public async Task Footnote()
    {
        var (status, text, _) = await ExportOneAsync("footnote",
            """{"text":"A clarifying aside.","noteId":1}""");

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("SENTINEL AFTER.");
    }

    [Fact]
    public async Task PageBreak()
    {
        var (status, text, xml) = await ExportOneAsync("pagebreak", """{}""");

        status.Should().Be(HttpStatusCode.OK);
        xml.Should().Contain("w:br");
        text.Should().Contain("SENTINEL AFTER.");
    }

    // ── lists ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("bullet")]
    [InlineData("ordered")]
    [InlineData("numbered")]
    public async Task ListsOfEachKind(string listType)
    {
        var (_, text, _) = await ExportOneAsync("list",
            $$"""{"listType":"{{listType}}","items":[{"text":"Alpha item"},{"text":"Beta item"}]}""");

        text.Should().Contain("Alpha item").And.Contain("Beta item");
    }

    [Fact]
    public async Task NestedListsKeepEveryLevel()
    {
        var (_, text, _) = await ExportOneAsync("list", """
            {"listType":"bullet","items":[
              {"text":"Top level","children":[
                {"text":"Second level","level":1,"children":[
                  {"text":"Third level","level":2}
                ]}
              ]}
            ]}
            """);

        text.Should().Contain("Top level");
        text.Should().Contain("Second level");
        text.Should().Contain("Third level", "sub-points of an argument are the argument");
    }

    [Fact]
    public async Task AListWithManyItems()
    {
        var items = string.Join(",",
            Enumerable.Range(0, 50).Select(i => "{\"text\":\"Item " + i + "\"}"));
        var (_, text, _) = await ExportOneAsync("list", $$"""{"listType":"bullet","items":[{{items}}]}""");

        text.Should().Contain("Item 0").And.Contain("Item 49");
    }

    // ── tables and figures ───────────────────────────────────────────────

    [Fact]
    public async Task TableWithAHeaderRow()
    {
        var (_, text, xml) = await ExportOneAsync("table", """
            {"hasHeader":true,"rows":[
              [{"text":"Method"},{"text":"Error"}],
              [{"text":"Ours"},{"text":"0.01"}],
              [{"text":"Baseline"},{"text":"0.07"}]
            ]}
            """);

        xml.Should().Contain("<w:tbl>");
        text.Should().Contain("Method").And.Contain("Baseline").And.Contain("0.07");
    }

    [Fact]
    public async Task TableCellsWithInlineFormatting()
    {
        var (_, text, _) = await ExportOneAsync("table", """
            {"rows":[[{"text":"emphasis","richText":[{"text":"emphasis","bold":true}]},{"text":"plain"}]]}
            """);

        text.Should().Contain("emphasis").And.Contain("plain");
    }

    [Fact]
    public async Task AnEmptyTableDoesNotBreakTheDocument()
    {
        var (status, text, _) = await ExportOneAsync("table", """{"rows":[]}""");

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("SENTINEL AFTER.");
    }

    [Fact]
    public async Task FigureWithAnEmbeddedImageAndCaption()
    {
        const string onePixelPng =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        var doc = await SeedDocumentAsync(_userId, "With a figure");
        await SeedBlockAsync(doc.Id, "figure",
            "{\"caption\":\"Convergence of the method.\",\"image\":{\"data\":\""
            + onePixelPng + "\",\"mimeType\":\"image/png\"}}");

        var response = await CreateClientAs(_userId).GetAsync($"/api/documents/{doc.Id}/export/docx");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        using var ms = new MemoryStream(bytes);
        using var word = WordprocessingDocument.Open(ms, false);

        word.MainDocumentPart!.ImageParts.Should().NotBeEmpty("the image travels inside the .docx");
        Regex.Replace(word.MainDocumentPart.Document.OuterXml, "<[^>]+>", "")
            .Should().Contain("Convergence of the method.");
    }

    [Fact]
    public async Task AFigureWithNoImageKeepsItsCaption()
    {
        var (_, text, _) = await ExportOneAsync("figure",
            """{"caption":"Figure to be supplied."}""");

        text.Should().Contain("Figure to be supplied.");
    }

    [Fact]
    public async Task AFigureWithUnreadableImageDataDoesNotAbortTheExport()
    {
        var (status, text, _) = await ExportOneAsync("figure",
            """{"caption":"Broken image.","image":{"data":"not-base64!!","mimeType":"image/png"}}""");

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("SENTINEL AFTER.");
    }

    // ── out of scope, but must never break a document ────────────────────

    [Theory]
    [InlineData("latex")]
    [InlineData("aside")]
    [InlineData("sidebar")]
    [InlineData("note")]
    [InlineData("glossary")]
    [InlineData("annotation")]
    [InlineData("verse")]
    [InlineData("embed")]
    [InlineData("columnbreak")]
    [InlineData("chapterBreak")]
    [InlineData("frontMatter")]
    [InlineData("backMatter")]
    [InlineData("label")]
    [InlineData("comment")]
    [InlineData("cvEntry")]
    [InlineData("cvSection")]
    [InlineData("slide")]
    [InlineData("cover")]
    [InlineData("titlePage")]
    [InlineData("personalInfo")]
    [InlineData("photo")]
    [InlineData("some-block-invented-next-year")]
    public async Task ABlockTypeOutsideArticleScopeDoesNotCostTheDocument(string type)
    {
        // These are not promised a rendering. They ARE promised that the paper
        // around them still exports — the failure mode to avoid is one unusual
        // block taking a whole document down.
        var (status, text, _) = await ExportOneAsync(type, """{"text":"Some content."}""");

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("SENTINEL AFTER.");
    }

    // ── a whole paper ────────────────────────────────────────────────────

    [Fact]
    public async Task AnEntireArticleSurvivesInOrder()
    {
        var doc = await SeedDocumentAsync(_userId, "A Complete Article");
        var order = 0;
        await SeedBlockAsync(doc.Id, "title",
            """{"title":"A Complete Article","author":"R. Author","date":"2026"}""", order++);
        await SeedBlockAsync(doc.Id, "abstract", """{"text":"ABSTRACTTEXT"}""", order++);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"INTROHEAD","level":1}""", order++);
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Prose with $x^2$ inline maths."}""", order++);
        await SeedBlockAsync(doc.Id, "equation",
            """{"source":"E = mc^2","displayMode":true}""", order++);
        await SeedBlockAsync(doc.Id, "theorem",
            """{"text":"THEOREMTEXT","theoremType":"theorem"}""", order++);
        await SeedBlockAsync(doc.Id, "list",
            """{"listType":"bullet","items":[{"text":"LISTONE","children":[{"text":"LISTNESTED","level":1}]}]}""", order++);
        await SeedBlockAsync(doc.Id, "table",
            """{"hasHeader":true,"rows":[[{"text":"COLHEAD"}],[{"text":"CELLVALUE"}]]}""", order++);
        await SeedBlockAsync(doc.Id, "code", """{"code":"CODELINE","language":"python"}""", order++);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"CONCLUSIONHEAD","level":1}""", order++);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", order);
        await SeedBibliographyEntryAsync(doc.Id, "ref1", "book",
            """{"author":"Euclid","title":"UNIQUEBOOKTITLE","year":"c. 300 BCE"}""");

        var (status, text, xml) = await ExportAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);

        // Nothing lost.
        foreach (var marker in new[]
                 {
                     "A Complete Article", "R. Author", "ABSTRACTTEXT", "INTROHEAD",
                     "THEOREMTEXT", "LISTONE", "LISTNESTED", "COLHEAD", "CELLVALUE",
                     "CODELINE", "CONCLUSIONHEAD", "References", "UNIQUEBOOKTITLE",
                 })
        {
            text.Should().Contain(marker, $"'{marker}' must survive the export");
        }

        // Nothing leaked.
        text.Should().NotContain("$", "no LaTeX delimiters in a finished Word document");
        xml.Should().Contain("oMath").And.Contain("<w:tbl>");

        // In order.
        text.IndexOf("ABSTRACTTEXT", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("INTROHEAD", StringComparison.Ordinal));
        text.IndexOf("CONCLUSIONHEAD", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("UNIQUEBOOKTITLE", StringComparison.Ordinal));
    }
}
