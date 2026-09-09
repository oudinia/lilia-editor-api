using System.Net;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// Word export, end to end: real rows in a real Postgres, through the real
/// HTTP endpoint, opened as a real .docx.
///
/// <para>The unit tests hand <c>DocxExportService</c> an ExportDocument that
/// someone constructed by hand. That is exactly the shape the two 2026-09-09
/// defects hid in: the exporter behaved correctly given its input, and the
/// input never arrived. <c>ConvertBlockAsync</c> mapped a bibliography block to
/// <c>null</c> "handled at document level", so the block was gone before the
/// exporter ran. Only a test that starts from the database catches that.</para>
/// </summary>
[Collection("Integration")]
public class DocxExportIntegrationTests : IntegrationTestBase
{
    private readonly string _userId = $"docx-test-{Guid.NewGuid():N}"[..28];

    public DocxExportIntegrationTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    // ── harness ──────────────────────────────────────────────────────────

    private async Task<(HttpStatusCode Status, byte[] Bytes)> ExportAsync(Guid docId)
    {
        var client = CreateClientAs(_userId);
        var response = await client.GetAsync($"/api/documents/{docId}/export/docx");
        return (response.StatusCode, await response.Content.ReadAsByteArrayAsync());
    }

    private static string TextIn(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var word = WordprocessingDocument.Open(ms, false);
        return Regex.Replace(word.MainDocumentPart!.Document.OuterXml, "<[^>]+>", "");
    }

    private static string XmlIn(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var word = WordprocessingDocument.Open(ms, false);
        return word.MainDocumentPart!.Document.OuterXml;
    }

    // ── the endpoint ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExportsADocumentAsAWordFile()
    {
        var doc = await SeedDocumentAsync(_userId, "Export me");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"A first paragraph."}""");

        var (status, bytes) = await ExportAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);
        bytes.Should().NotBeEmpty();
        bytes[0].Should().Be((byte)'P');
        TextIn(bytes).Should().Contain("A first paragraph.");
    }

    [Fact]
    public async Task ADocumentWithNoBlocksStillExports()
    {
        var doc = await SeedDocumentAsync(_userId, "Empty");

        var (status, bytes) = await ExportAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnotherUsersDocumentIsNotExportable()
    {
        var otherUser = $"docx-other-{Guid.NewGuid():N}"[..28];
        await SeedUserAsync(otherUser);
        var theirDoc = await SeedDocumentAsync(otherUser, "Not yours");
        await SeedBlockAsync(theirDoc.Id, "paragraph", """{"text":"Private."}""");

        var (status, _) = await ExportAsync(theirDoc.Id);

        status.Should().NotBe(HttpStatusCode.OK);
    }

    // ── the bibliography, from the database ──────────────────────────────

    [Fact]
    public async Task EntriesInTheDatabaseReachTheWordFile()
    {
        // The regression, in the form it actually took: the entries were in the
        // database, the block was in the document, and the .docx had no
        // References section at all.
        var doc = await SeedDocumentAsync(_userId, "Cited work");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"As shown previously."}""", 0);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", 1);
        await SeedBibliographyEntryAsync(doc.Id, "euclid", "book",
            """{"author":"Euclid","title":"Elements","year":"c. 300 BCE"}""");

        var (status, bytes) = await ExportAsync(doc.Id);
        var text = TextIn(bytes);

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("References");
        text.Should().Contain("Euclid");
        text.Should().Contain("Elements");
        text.Should().NotContain("appended below");
    }

    [Fact]
    public async Task EveryEntryInTheDatabaseIsEmitted()
    {
        var doc = await SeedDocumentAsync(_userId, "Many refs");
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", 0);
        await SeedBibliographyEntryAsync(doc.Id, "one", "book",
            """{"author":"Author One","title":"Alpha Title","year":"2001"}""");
        await SeedBibliographyEntryAsync(doc.Id, "two", "article",
            """{"author":"Author Two","title":"Beta Title","year":"2002","journal":"A Journal"}""");
        await SeedBibliographyEntryAsync(doc.Id, "three", "book",
            """{"author":"Author Three","title":"Gamma Title","year":"2003"}""");

        var text = TextIn((await ExportAsync(doc.Id)).Bytes);

        text.Should().Contain("Alpha Title").And.Contain("Beta Title").And.Contain("Gamma Title");
        text.Should().Contain("A Journal");
    }

    [Fact]
    public async Task ReferencesSurviveEvenWithNoBibliographyBlock()
    {
        var doc = await SeedDocumentAsync(_userId, "No block");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body text."}""", 0);
        await SeedBibliographyEntryAsync(doc.Id, "solo", "book",
            """{"author":"Lone Author","title":"Only Work","year":"1999"}""");

        var text = TextIn((await ExportAsync(doc.Id)).Bytes);

        text.Should().Contain("References").And.Contain("Only Work");
    }

    [Fact]
    public async Task ADocumentWithNoEntriesGetsNoStrayReferencesSection()
    {
        var doc = await SeedDocumentAsync(_userId, "No refs");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Nothing is cited here."}""");

        var text = TextIn((await ExportAsync(doc.Id)).Bytes);

        text.Should().Contain("Nothing is cited here.");
        text.Should().NotContain("References");
    }

    // ── maths, from the database ─────────────────────────────────────────

    [Fact]
    public async Task InlineMathInAParagraphBecomesWordMath()
    {
        // Stored the way an author writes it: dollars inside the text.
        var doc = await SeedDocumentAsync(_userId, "Inline maths");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"The identity $a^2 + b^2 = c^2$ holds for right triangles."}""");

        var bytes = (await ExportAsync(doc.Id)).Bytes;

        XmlIn(bytes).Should().Contain("oMath");
        TextIn(bytes).Should().NotContain("$", "LaTeX delimiters must not reach the reader");
        TextIn(bytes).Should().Contain("holds for right triangles.");
    }

    [Fact]
    public async Task DisplayMathInAParagraphDoesNotArriveAsDollarSigns()
    {
        // $$...$$ matched neither branch of the inline parser and passed
        // through verbatim.
        var doc = await SeedDocumentAsync(_userId, "Display maths");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"We claim $$a^2 + b^2 = c^2$$ and proceed."}""");

        var bytes = (await ExportAsync(doc.Id)).Bytes;

        TextIn(bytes).Should().NotContain("$$");
        TextIn(bytes).Should().Contain("and proceed.");
        XmlIn(bytes).Should().Contain("oMath");
    }

    [Fact]
    public async Task AnEquationBlockBecomesWordMath()
    {
        var doc = await SeedDocumentAsync(_userId, "Equation block");
        await SeedBlockAsync(doc.Id, "equation",
            """{"source":"\\frac{a}{b}","displayMode":true}""");

        XmlIn((await ExportAsync(doc.Id)).Bytes).Should().Contain("oMath");
    }

    // ── the rest of the block vocabulary, stored as the app stores it ────

    [Fact]
    public async Task HeadingsCodeQuotesAndTheoremsAllSurvive()
    {
        var doc = await SeedDocumentAsync(_userId, "Mixed");
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Introduction","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Opening remarks."}""", 1);
        await SeedBlockAsync(doc.Id, "code", """{"code":"print('hello')","language":"python"}""", 2);
        await SeedBlockAsync(doc.Id, "blockquote", """{"text":"A quotation."}""", 3);
        await SeedBlockAsync(doc.Id, "theorem",
            """{"text":"The square of the hypotenuse.","theoremType":"theorem"}""", 4);

        var text = TextIn((await ExportAsync(doc.Id)).Bytes);

        text.Should().Contain("Introduction");
        text.Should().Contain("Opening remarks.");
        text.Should().Contain("print('hello')");
        text.Should().Contain("A quotation.");
        text.Should().Contain("The square of the hypotenuse.");
    }

    [Fact]
    public async Task AbstractsAndLists()
    {
        var doc = await SeedDocumentAsync(_userId, "Abstract and lists");
        await SeedBlockAsync(doc.Id, "abstract", """{"text":"This paper argues something."}""", 0);
        await SeedBlockAsync(doc.Id, "list",
            """{"listType":"bullet","items":[{"text":"First point"},{"text":"Second point"}]}""", 1);

        var text = TextIn((await ExportAsync(doc.Id)).Bytes);

        text.Should().Contain("Abstract").And.Contain("This paper argues something.");
        text.Should().Contain("First point").And.Contain("Second point");
    }

    [Fact]
    public async Task NestedListItemsSurviveTheRoundTrip()
    {
        // Children were dropped silently: the sub-points of an argument, gone.
        var doc = await SeedDocumentAsync(_userId, "Nested list");
        await SeedBlockAsync(doc.Id, "list",
            """{"listType":"bullet","items":[{"text":"Outer point","children":[{"text":"Inner point","level":1}]}]}""");

        var text = TextIn((await ExportAsync(doc.Id)).Bytes);

        text.Should().Contain("Outer point").And.Contain("Inner point");
    }

    [Fact]
    public async Task Tables()
    {
        var doc = await SeedDocumentAsync(_userId, "Table");
        await SeedBlockAsync(doc.Id, "table",
            """{"hasHeader":true,"rows":[[{"text":"Header A"},{"text":"Header B"}],[{"text":"cell one"},{"text":"cell two"}]]}""");

        var bytes = (await ExportAsync(doc.Id)).Bytes;

        XmlIn(bytes).Should().Contain("<w:tbl>");
        TextIn(bytes).Should().Contain("Header A").And.Contain("cell two");
    }

    [Fact]
    public async Task HighlightedTextDoesNotAbortTheExport()
    {
        // One highlighted word used to throw ArgumentException out of
        // CreateRun and take the entire document with it.
        var doc = await SeedDocumentAsync(_userId, "Highlighted");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Before after.","richText":[{"text":"Before ","highlight":"yellow"},{"text":"after."}]}""");

        var (status, bytes) = await ExportAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);
        TextIn(bytes).Should().Contain("after.");
    }

    [Fact]
    public async Task BlockOrderFollowsSortOrder()
    {
        var doc = await SeedDocumentAsync(_userId, "Ordering");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"ZULU"}""", 2);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"ALPHA"}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"MIKE"}""", 1);

        var text = TextIn((await ExportAsync(doc.Id)).Bytes);

        text.IndexOf("ALPHA", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("MIKE", StringComparison.Ordinal));
        text.IndexOf("MIKE", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("ZULU", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnknownBlockTypeDoesNotStopTheDocument()
    {
        var doc = await SeedDocumentAsync(_userId, "Future block");
        await SeedBlockAsync(doc.Id, "some-future-type", """{"text":"Unknown content"}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"The rest of the paper."}""", 1);

        var (status, bytes) = await ExportAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);
        TextIn(bytes).Should().Contain("The rest of the paper.");
    }

    [Fact]
    public async Task CharactersThatWouldBreakTheXml()
    {
        var doc = await SeedDocumentAsync(_userId, "Escaping");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Ampersand & angle < brackets > and accents: é à ü"}""");

        var (status, bytes) = await ExportAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);
        TextIn(bytes).Should().Contain("Ampersand");
    }

    [Fact]
    public async Task AFullPaperExportsIntact()
    {
        // The shape of the document that started all of this.
        var doc = await SeedDocumentAsync(_userId, "On the Pythagorean Theorem");
        await SeedBlockAsync(doc.Id, "abstract", """{"text":"An old and widely used result."}""", 0);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Introduction","level":1}""", 1);
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"The identity $a^2 + b^2 = c^2$ predates its namesake."}""", 2);
        await SeedBlockAsync(doc.Id, "equation", """{"source":"a^2 + b^2 = c^2","displayMode":true}""", 3);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Conclusion","level":1}""", 4);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", 5);
        await SeedBibliographyEntryAsync(doc.Id, "euclid", "book",
            """{"author":"Euclid","title":"Elements","year":"c. 300 BCE"}""");
        await SeedBibliographyEntryAsync(doc.Id, "neugebauer", "book",
            """{"author":"Otto Neugebauer","title":"The Exact Sciences in Antiquity","publisher":"Brown University Press","year":"1957"}""");

        var (status, bytes) = await ExportAsync(doc.Id);
        var text = TextIn(bytes);

        status.Should().Be(HttpStatusCode.OK);
        text.Should().Contain("An old and widely used result.");
        text.Should().Contain("Introduction").And.Contain("Conclusion");
        text.Should().Contain("predates its namesake.");
        text.Should().Contain("References");
        text.Should().Contain("Euclid").And.Contain("Neugebauer");
        text.Should().NotContain("$", "no LaTeX delimiters anywhere in the finished document");
        XmlIn(bytes).Should().Contain("oMath");
    }
}
