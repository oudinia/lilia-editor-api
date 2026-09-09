using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// The same vocabulary question as the Word suite, asked of the three text
/// formats: markdown, html and lml.
///
/// <para>These take a different road. Word is built by
/// <c>DocumentExportService</c> → <c>DocxExportService</c>, a private mapper
/// and a private renderer — which is exactly why it accumulated nine defects
/// nobody saw. These three call <c>RenderService</c>, the same component that
/// renders LaTeX and backs validation, the PDF and the preview. It is the
/// best-exercised code in the export path, and it shows: none of the Word
/// defects reproduce here.</para>
///
/// <para>That is a reason to expect fewer findings, not a reason to skip the
/// question. "Well-exercised elsewhere" is an argument about the renderer, and
/// what is untested is the <b>vocabulary</b> — whether each block type an
/// article is built from actually reaches each output.</para>
/// </summary>
[Collection("Integration")]
public class TextFormatVocabularyTests : IntegrationTestBase
{
    private readonly string _userId = $"txt-fmt-{Guid.NewGuid():N}"[..28];

    public TextFormatVocabularyTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    /// <summary>Every text format, so one test method asks the question three times.</summary>
    public static TheoryData<string> Formats => new() { "markdown", "html", "lml" };

    private async Task<(HttpStatusCode Status, string Body)> ExportAsync(Guid docId, string format)
    {
        var response = await CreateClientAs(_userId)
            .GetAsync($"/api/documents/{docId}/export/{format}");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>One block of the given type, plus a sentinel after it.</summary>
    private async Task<(HttpStatusCode Status, string Body)> ExportOneAsync(
        string format, string type, string contentJson)
    {
        var doc = await SeedDocumentAsync(_userId, $"Vocabulary {type}");
        await SeedBlockAsync(doc.Id, type, contentJson, 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"SENTINELAFTER"}""", 1);
        return await ExportAsync(doc.Id, format);
    }

    // ── the endpoints themselves ─────────────────────────────────────────

    [Theory, MemberData(nameof(Formats))]
    public async Task ADocumentExports(string format)
    {
        var doc = await SeedDocumentAsync(_userId, "Plain");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body text here."}""");

        var (status, body) = await ExportAsync(doc.Id, format);

        status.Should().Be(HttpStatusCode.OK);
        body.Should().NotBeNullOrWhiteSpace();
        body.Should().Contain("Body text here.");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task AnEmptyDocumentExports(string format)
    {
        var doc = await SeedDocumentAsync(_userId, "Empty");

        var (status, _) = await ExportAsync(doc.Id, format);

        status.Should().Be(HttpStatusCode.OK);
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task AnotherUsersDocumentIsRefused(string format)
    {
        var stranger = $"txt-x-{Guid.NewGuid():N}"[..28];
        await SeedUserAsync(stranger);
        var theirs = await SeedDocumentAsync(stranger, "Private");
        await SeedBlockAsync(theirs.Id, "paragraph", """{"text":"UNPUBLISHEDSECRET"}""");

        var (status, body) = await ExportAsync(theirs.Id, format);

        status.Should().NotBe(HttpStatusCode.OK);
        body.Should().NotContain("UNPUBLISHEDSECRET");
    }

    // ── front matter ─────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Formats))]
    public async Task TitleAndAuthorSurvive(string format)
    {
        // The Word export dropped both for as long as it existed.
        var (status, body) = await ExportOneAsync(format, "title",
            """{"title":"UNIQUETITLEHERE","author":"UNIQUEAUTHORHERE","date":"2026"}""");

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("UNIQUETITLEHERE");
        body.Should().Contain("UNIQUEAUTHORHERE", "a paper without its author is not a paper");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task AbstractSurvives(string format)
    {
        var (_, body) = await ExportOneAsync(format, "abstract", """{"text":"ABSTRACTBODYTEXT"}""");

        body.Should().Contain("ABSTRACTBODYTEXT");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task HeadingsSurvive(string format)
    {
        var doc = await SeedDocumentAsync(_userId, "Headings");
        await SeedBlockAsync(doc.Id, "heading", """{"text":"LEVELONEHEAD","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"LEVELTWOHEAD","level":2}""", 1);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"LEVELTHREEHEAD","level":3}""", 2);

        var (_, body) = await ExportAsync(doc.Id, format);

        body.Should().Contain("LEVELONEHEAD").And.Contain("LEVELTWOHEAD").And.Contain("LEVELTHREEHEAD");
    }

    // ── the body ─────────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Formats))]
    public async Task ParagraphsSurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "paragraph", """{"text":"PARAGRAPHBODY"}""");

        body.Should().Contain("PARAGRAPHBODY");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task InlineMathSurvives(string format)
    {
        // Unlike Word, these formats keep LaTeX maths as LaTeX — Markdown and
        // HTML hand it to MathJax, LML round-trips it. The requirement is that
        // the expression survives, not that it is converted.
        var (_, body) = await ExportOneAsync(format, "paragraph",
            """{"text":"The identity $a^2 + b^2 = c^2$ is standard."}""");

        body.Should().Contain("a^2 + b^2 = c^2");
        body.Should().Contain("is standard.");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task EquationBlocksSurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "equation",
            """{"source":"\\sum_{i=1}^{n} x_i","displayMode":true}""");

        body.Should().Contain("x_i", "the equation must reach the file under the current key");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task EquationBlocksUnderTheLegacyKeySurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "equation",
            """{"latex":"\\int_0^1 f(x)","displayMode":true}""");

        body.Should().Contain("f(x)");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task ListsSurvive(string format)
    {
        // The shape the editor writes: plain strings, and "ordered" rather
        // than "listType". Tests that used the DTO shape here passed against
        // data the application never produces.
        var (_, body) = await ExportOneAsync(format, "list",
            """{"ordered":false,"items":["FIRSTITEM","SECONDITEM"]}""");

        body.Should().Contain("FIRSTITEM").And.Contain("SECONDITEM");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task NestedListItemsSurvive(string format)
    {
        // The object-with-children shape, which arrives from LaTeX import
        // rather than from the editor. Word used to drop the children.
        var (_, body) = await ExportOneAsync(format, "list",
            """{"listType":"bullet","items":[{"text":"OUTERITEM","children":[{"text":"INNERITEM","level":1}]}]}""");

        body.Should().Contain("OUTERITEM");
        body.Should().Contain("INNERITEM", "sub-points of an argument are the argument");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task TablesSurvive(string format)
    {
        // Again the editor's shape: a separate "headers" array, and rows of
        // plain strings.
        var (_, body) = await ExportOneAsync(format, "table",
            """{"headers":["COLHEADER"],"rows":[["CELLCONTENT"]]}""");

        body.Should().Contain("COLHEADER").And.Contain("CELLCONTENT");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task CodeBlocksSurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "code",
            """{"code":"CODELINEONE","language":"python"}""");

        body.Should().Contain("CODELINEONE");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task BlockquotesSurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "blockquote", """{"text":"QUOTEDLINE"}""");

        body.Should().Contain("QUOTEDLINE");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task TheoremsSurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "theorem",
            """{"text":"THEOREMSTATEMENT","theoremType":"theorem"}""");

        body.Should().Contain("THEOREMSTATEMENT");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task CalloutsSurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "callout", """{"text":"CALLOUTBODY"}""");

        body.Should().Contain("CALLOUTBODY");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task AlgorithmsSurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "algorithm",
            """{"title":"ALGONAME","code":"ALGOSTEPS"}""");

        body.Should().Contain("ALGOSTEPS");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task FigureCaptionsSurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "figure",
            """{"src":"","caption":"FIGURECAPTION","alt":""}""");

        body.Should().Contain("FIGURECAPTION");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task FootnotesSurvive(string format)
    {
        var (_, body) = await ExportOneAsync(format, "footnote",
            """{"text":"FOOTNOTEBODY","noteId":1}""");

        body.Should().Contain("FOOTNOTEBODY");
    }

    // ── references ───────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Formats))]
    public async Task BibliographyEntriesSurvive(string format)
    {
        // Word emitted a References heading and a placeholder for as long as
        // the feature existed. These formats read the entries.
        var doc = await SeedDocumentAsync(_userId, "Cited");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"As shown."}""", 0);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", 1);
        await SeedBibliographyEntryAsync(doc.Id, "euclid", "book",
            """{"author":"UNIQUEBIBAUTHOR","title":"UNIQUEBIBTITLE","year":"1957"}""");

        var (status, body) = await ExportAsync(doc.Id, format);

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("UNIQUEBIBTITLE");
        body.Should().Contain("UNIQUEBIBAUTHOR");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task CitationsSurvive(string format)
    {
        var doc = await SeedDocumentAsync(_userId, "Citing");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Proved long ago \\citep{euclid_elements}."}""", 0);
        await SeedBibliographyEntryAsync(doc.Id, "euclid_elements", "book",
            """{"author":"Euclid","title":"Elements","year":"300"}""");

        var (_, body) = await ExportAsync(doc.Id, format);

        body.Should().Contain("euclid_elements", "a citation the reader cannot resolve is a lost claim");
    }

    // ── robustness ───────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Formats))]
    public async Task BlockOrderFollowsSortOrder(string format)
    {
        var doc = await SeedDocumentAsync(_userId, "Ordering");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"ZULUMARKER"}""", 2);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"ALPHAMARKER"}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"MIKEMARKER"}""", 1);

        var (_, body) = await ExportAsync(doc.Id, format);

        body.IndexOf("ALPHAMARKER", StringComparison.Ordinal)
            .Should().BeLessThan(body.IndexOf("MIKEMARKER", StringComparison.Ordinal));
        body.IndexOf("MIKEMARKER", StringComparison.Ordinal)
            .Should().BeLessThan(body.IndexOf("ZULUMARKER", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("markdown", "latex")]
    [InlineData("markdown", "aside")]
    [InlineData("markdown", "some-block-invented-next-year")]
    [InlineData("html", "latex")]
    [InlineData("html", "aside")]
    [InlineData("html", "some-block-invented-next-year")]
    [InlineData("lml", "latex")]
    [InlineData("lml", "aside")]
    [InlineData("lml", "some-block-invented-next-year")]
    public async Task AnUnusualBlockDoesNotCostTheDocument(string format, string type)
    {
        var (status, body) = await ExportOneAsync(format, type, """{"text":"Some content."}""");

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("SENTINELAFTER");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task CharactersThatNeedEscaping(string format)
    {
        var (status, body) = await ExportOneAsync(format, "paragraph",
            """{"text":"Ampersand & angle < brackets > accents é à ü and a \"quote\""}""");

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Ampersand");
        body.Should().Contain("accents");
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task AWholeArticleSurvives(string format)
    {
        var doc = await SeedDocumentAsync(_userId, "Whole article");
        var o = 0;
        await SeedBlockAsync(doc.Id, "title",
            """{"title":"WHOLETITLE","author":"WHOLEAUTHOR","date":"2026"}""", o++);
        await SeedBlockAsync(doc.Id, "abstract", """{"text":"WHOLEABSTRACT"}""", o++);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"WHOLEINTRO","level":1}""", o++);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Prose with $x^2$ maths."}""", o++);
        await SeedBlockAsync(doc.Id, "equation", """{"source":"E = mc^2","displayMode":true}""", o++);
        await SeedBlockAsync(doc.Id, "list",
            """{"ordered":false,"items":["WHOLELIST"]}""", o++);
        await SeedBlockAsync(doc.Id, "table",
            """{"headers":["WHOLEHEADER"],"rows":[["WHOLECELL"]]}""", o++);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", o);
        await SeedBibliographyEntryAsync(doc.Id, "ref1", "book",
            """{"author":"Euclid","title":"WHOLEBIBTITLE","year":"300"}""");

        var (status, body) = await ExportAsync(doc.Id, format);

        status.Should().Be(HttpStatusCode.OK);
        foreach (var marker in new[]
                 {
                     "WHOLETITLE", "WHOLEAUTHOR", "WHOLEABSTRACT", "WHOLEINTRO",
                     "WHOLELIST", "WHOLEHEADER", "WHOLECELL", "WHOLEBIBTITLE",
                 })
        {
            body.Should().Contain(marker, $"'{marker}' must survive an export to {format}");
        }
    }
}
