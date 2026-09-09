using System.IO.Compression;
using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// The LaTeX project export, block by block.
///
/// <para>This was the last format without a vocabulary suite, and the one that
/// mattered most: the PDF is compiled from it, so a block that fails to reach
/// main.tex is missing from two outputs, not one. It is also the format where
/// losing content is hardest to notice — a paper with one section quietly
/// absent still compiles perfectly.</para>
///
/// <para>Test data is seeded in the shape the editor writes — plain strings in
/// a list, headings in their own <c>headers</c> array — not the shape the
/// export DTO accepts. Using the DTO shape is how a green suite came to sit on
/// top of a Word export that dropped every table's headings.</para>
/// </summary>
[Collection("Integration")]
public class LatexExportVocabularyTests : IntegrationTestBase
{
    private readonly string _userId = $"tex-voc-{Guid.NewGuid():N}"[..28];

    public LatexExportVocabularyTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    // ── harness ──────────────────────────────────────────────────────────

    private sealed record Project(HttpStatusCode Status, string MainTex, string? Bib, string[] Files);

    private async Task<Project> ExportAsync(Guid docId)
    {
        var response = await CreateClientAs(_userId).GetAsync($"/api/documents/{docId}/export/latex");
        if (response.StatusCode != HttpStatusCode.OK)
            return new Project(response.StatusCode, "", null, []);

        using var ms = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        string? Read(string name)
        {
            var entry = zip.Entries.FirstOrDefault(e => e.Name == name);
            if (entry == null) return null;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }

        return new Project(
            response.StatusCode,
            Read("main.tex") ?? "",
            Read("references.bib"),
            [.. zip.Entries.Select(e => e.FullName)]);
    }

    /// <summary>One block of the given type, plus a sentinel after it.</summary>
    private async Task<Project> ExportOneAsync(string type, string contentJson)
    {
        var doc = await SeedDocumentAsync(_userId, $"LaTeX {type}");
        await SeedBlockAsync(doc.Id, type, contentJson, 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"SENTINELAFTER"}""", 1);
        return await ExportAsync(doc.Id);
    }

    // ── the project ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExportsACompilableProject()
    {
        var doc = await SeedDocumentAsync(_userId, "A paper");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body text."}""");

        var p = await ExportAsync(doc.Id);

        p.Status.Should().Be(HttpStatusCode.OK);
        p.Files.Should().Contain("main.tex");
        p.MainTex.Should().Contain(@"\documentclass");
        p.MainTex.Should().Contain(@"\begin{document}").And.Contain(@"\end{document}");
        p.MainTex.Should().Contain("Body text.");
    }

    [Fact]
    public async Task AnEmptyDocumentStillProducesAProject()
    {
        var doc = await SeedDocumentAsync(_userId, "Nothing yet");

        var p = await ExportAsync(doc.Id);

        p.Status.Should().Be(HttpStatusCode.OK);
        p.MainTex.Should().Contain(@"\end{document}");
    }

    [Fact]
    public async Task AnotherUsersDocumentIsRefused()
    {
        var stranger = $"tex-x-{Guid.NewGuid():N}"[..28];
        await SeedUserAsync(stranger);
        var theirs = await SeedDocumentAsync(stranger, "Private");
        await SeedBlockAsync(theirs.Id, "paragraph", """{"text":"UNPUBLISHEDSECRET"}""");

        var p = await ExportAsync(theirs.Id);

        p.Status.Should().NotBe(HttpStatusCode.OK);
        p.MainTex.Should().NotContain("UNPUBLISHEDSECRET");
    }

    // ── front matter ─────────────────────────────────────────────────────

    [Fact]
    public async Task TitleAuthorAndDate()
    {
        var p = await ExportOneAsync("title",
            """{"title":"UNIQUETITLE","author":"UNIQUEAUTHOR","date":"March 2026"}""");

        p.MainTex.Should().Contain("UNIQUETITLE");
        p.MainTex.Should().Contain("UNIQUEAUTHOR", "a paper without its author is not a paper");
        p.MainTex.Should().Contain("March 2026");
    }

    [Fact]
    public async Task Abstract()
    {
        var p = await ExportOneAsync("abstract", """{"text":"ABSTRACTBODY"}""");

        p.MainTex.Should().Contain("ABSTRACTBODY");
        p.MainTex.Should().Contain("abstract", "it should use the abstract environment");
    }

    [Theory]
    [InlineData(1, "section")]
    [InlineData(2, "subsection")]
    [InlineData(3, "subsubsection")]
    public async Task HeadingsBecomeSectioningCommands(int level, string command)
    {
        var p = await ExportOneAsync("heading", $$"""{"text":"HEADTEXT{{level}}","level":{{level}}}""");

        p.MainTex.Should().Contain($"HEADTEXT{level}");
        p.MainTex.Should().Contain($@"\{command}",
            "a heading that becomes a bare paragraph loses the document's structure");
    }

    // ── body ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Paragraphs()
    {
        (await ExportOneAsync("paragraph", """{"text":"PARAGRAPHBODY"}""")).MainTex
            .Should().Contain("PARAGRAPHBODY");
    }

    [Fact]
    public async Task InlineMathStaysMath()
    {
        var p = await ExportOneAsync("paragraph", """{"text":"Given $E = mc^2$ we proceed."}""");

        p.MainTex.Should().Contain("E = mc^2");
        p.MainTex.Should().Contain("we proceed.");
    }

    [Fact]
    public async Task EquationBlocksUnderTheCurrentKey()
    {
        var p = await ExportOneAsync("equation",
            """{"source":"\\sum_{i=1}^{n} x_i","displayMode":true}""");

        p.MainTex.Should().Contain("x_i", "the current key is 'source'");
    }

    [Fact]
    public async Task EquationBlocksUnderTheLegacyKey()
    {
        var p = await ExportOneAsync("equation",
            """{"latex":"\\int_0^1 f(x)","displayMode":true}""");

        p.MainTex.Should().Contain("f(x)");
    }

    [Theory]
    [InlineData("theorem")]
    [InlineData("lemma")]
    [InlineData("corollary")]
    [InlineData("definition")]
    [InlineData("proof")]
    public async Task TheoremEnvironments(string kind)
    {
        var p = await ExportOneAsync("theorem",
            $$"""{"text":"THEOREMBODY","theoremType":"{{kind}}"}""");

        p.MainTex.Should().Contain("THEOREMBODY");
        p.MainTex.Should().Contain(kind, "the environment must name the kind of claim being made");
    }

    [Fact]
    public async Task CodeBlocks()
    {
        var p = await ExportOneAsync("code", """{"code":"CODELINEONE","language":"python"}""");

        p.MainTex.Should().Contain("CODELINEONE");
    }

    [Fact]
    public async Task Blockquotes() =>
        (await ExportOneAsync("blockquote", """{"text":"QUOTEDLINE"}""")).MainTex
            .Should().Contain("QUOTEDLINE");

    [Fact]
    public async Task Callouts() =>
        (await ExportOneAsync("callout", """{"text":"CALLOUTBODY"}""")).MainTex
            .Should().Contain("CALLOUTBODY");

    [Fact]
    public async Task Algorithms() =>
        (await ExportOneAsync("algorithm", """{"title":"ALGONAME","code":"ALGOSTEPS"}""")).MainTex
            .Should().Contain("ALGOSTEPS");

    [Fact]
    public async Task Footnotes() =>
        (await ExportOneAsync("footnote", """{"text":"FOOTNOTEBODY","noteId":1}""")).MainTex
            .Should().Contain("FOOTNOTEBODY");

    // ── lists and tables, in the shape the editor writes ─────────────────

    [Fact]
    public async Task ListsInTheShapeTheEditorWrites()
    {
        var p = await ExportOneAsync("list", """{"ordered":false,"items":["FIRSTITEM","SECONDITEM"]}""");

        p.MainTex.Should().Contain("FIRSTITEM").And.Contain("SECONDITEM");
        p.MainTex.Should().Contain("itemize");
    }

    [Fact]
    public async Task OrderedLists()
    {
        var p = await ExportOneAsync("list", """{"ordered":true,"items":["STEPONE","STEPTWO"]}""");

        p.MainTex.Should().Contain("STEPONE");
        p.MainTex.Should().Contain("enumerate", "an ordered list must not silently become a bulleted one");
    }

    [Fact]
    public async Task NestedListItemsFromImport()
    {
        // The object-with-children shape, produced by DOCX import.
        var p = await ExportOneAsync("list",
            """{"listType":"bullet","items":[{"text":"OUTERITEM","children":[{"text":"INNERITEM","level":1}]}]}""");

        p.MainTex.Should().Contain("OUTERITEM");
        p.MainTex.Should().Contain("INNERITEM", "sub-points of an argument are the argument");
    }

    [Fact]
    public async Task TablesKeepTheirHeaders()
    {
        // The defect this mirrors: Word read "rows" and never "headers", so
        // every real table exported without its column headings.
        var p = await ExportOneAsync("table",
            """{"headers":["METHODCOL","ERRORCOL"],"rows":[["OURSROW","POINTZEROONE"]]}""");

        p.MainTex.Should().Contain("METHODCOL", "a table without its headings is unlabelled numbers");
        p.MainTex.Should().Contain("ERRORCOL");
        p.MainTex.Should().Contain("OURSROW").And.Contain("POINTZEROONE");
        p.MainTex.Should().Contain("tabular");
    }

    [Fact]
    public async Task FigureCaptions()
    {
        var p = await ExportOneAsync("figure", """{"src":"","caption":"FIGURECAPTION","alt":""}""");

        p.MainTex.Should().Contain("FIGURECAPTION");
    }

    // ── references ───────────────────────────────────────────────────────

    [Fact]
    public async Task BibliographyEntriesReachTheBibFile()
    {
        var doc = await SeedDocumentAsync(_userId, "Cited");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"As shown \\citep{euclid}."}""", 0);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", 1);
        await SeedBibliographyEntryAsync(doc.Id, "euclid", "book",
            """{"author":"UNIQUEBIBAUTHOR","title":"UNIQUEBIBTITLE","year":"300"}""");

        var p = await ExportAsync(doc.Id);

        p.Status.Should().Be(HttpStatusCode.OK);
        p.Files.Should().Contain("references.bib");
        p.Bib.Should().NotBeNull();
        p.Bib!.Should().Contain("UNIQUEBIBTITLE").And.Contain("UNIQUEBIBAUTHOR");
        p.Bib.Should().Contain("@book{euclid");
        p.MainTex.Should().Contain(@"\bibliography{references}");
    }

    [Fact]
    public async Task NatbibIsLoadedForNatbibCitations()
    {
        // The defect that started this week: validation and the PDF disagreed
        // about whether natbib was loaded, and a compilable document was
        // reported broken.
        var doc = await SeedDocumentAsync(_userId, "Natbib");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Shown by \\citet{euclid}."}""", 0);
        await SeedBibliographyEntryAsync(doc.Id, "euclid", "book",
            """{"author":"Euclid","title":"Elements","year":"300"}""");

        var p = await ExportAsync(doc.Id);

        p.MainTex.Should().Contain(@"\usepackage{natbib}");
        p.MainTex.Should().Contain(@"\citet{euclid}");
    }

    [Fact]
    public async Task PlainCiteDoesNotDragInNatbib()
    {
        // Loading natbib for a legacy \cite-only document silently turns its
        // numeric bibliography into author-year.
        var doc = await SeedDocumentAsync(_userId, "Plain cite");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Shown by \\cite{euclid}."}""", 0);
        await SeedBibliographyEntryAsync(doc.Id, "euclid", "book",
            """{"author":"Euclid","title":"Elements","year":"300"}""");

        var p = await ExportAsync(doc.Id);

        p.MainTex.Should().NotContain(@"\usepackage{natbib}");
    }

    [Fact]
    public async Task ADocumentWithNoReferencesGetsNoBibFile()
    {
        var doc = await SeedDocumentAsync(_userId, "Uncited");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Nothing is cited."}""");

        var p = await ExportAsync(doc.Id);

        p.MainTex.Should().NotContain(@"\bibliography{references}");
    }

    // ── robustness ───────────────────────────────────────────────────────

    [Fact]
    public async Task BlockOrderFollowsSortOrder()
    {
        var doc = await SeedDocumentAsync(_userId, "Ordering");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"ZULUMARKER"}""", 2);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"ALPHAMARKER"}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"MIKEMARKER"}""", 1);

        var tex = (await ExportAsync(doc.Id)).MainTex;

        tex.IndexOf("ALPHAMARKER", StringComparison.Ordinal)
            .Should().BeLessThan(tex.IndexOf("MIKEMARKER", StringComparison.Ordinal));
        tex.IndexOf("MIKEMARKER", StringComparison.Ordinal)
            .Should().BeLessThan(tex.IndexOf("ZULUMARKER", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("50% of the sample", "%")]
    [InlineData("Smith & Jones", "&")]
    [InlineData("the value_of_x", "_")]
    [InlineData("cost in $ terms", "$")]
    [InlineData("item #3", "#")]
    public async Task CharactersLaTeXWouldOtherwiseEat(string text, string ch)
    {
        // An unescaped % comments out the rest of the line — a sentence, or the
        // command that closes an environment. These are not cosmetic.
        var p = await ExportOneAsync("paragraph", $$"""{"text":"{{text}}"}""");

        p.Status.Should().Be(HttpStatusCode.OK);
        p.MainTex.Should().Contain($@"\{ch}", $"a bare {ch} changes what LaTeX reads");
        p.MainTex.Should().Contain("SENTINELAFTER", "the rest of the document must survive it");
    }

    [Theory]
    [InlineData("latex")]
    [InlineData("aside")]
    [InlineData("glossary")]
    [InlineData("cvEntry")]
    [InlineData("some-block-invented-next-year")]
    public async Task AnUnusualBlockDoesNotCostTheDocument(string type)
    {
        var p = await ExportOneAsync(type, """{"text":"Some content."}""");

        p.Status.Should().Be(HttpStatusCode.OK);
        p.MainTex.Should().Contain("SENTINELAFTER");
    }

    [Fact]
    public async Task AWholeArticleSurvivesInOrder()
    {
        var doc = await SeedDocumentAsync(_userId, "Complete");
        var o = 0;
        await SeedBlockAsync(doc.Id, "title",
            """{"title":"WHOLETITLE","author":"WHOLEAUTHOR","date":"2026"}""", o++);
        await SeedBlockAsync(doc.Id, "abstract", """{"text":"WHOLEABSTRACT"}""", o++);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"WHOLEINTRO","level":1}""", o++);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Prose with $x^2$ and \\citep{ref1}."}""", o++);
        await SeedBlockAsync(doc.Id, "equation", """{"source":"E = mc^2","displayMode":true}""", o++);
        await SeedBlockAsync(doc.Id, "theorem", """{"text":"WHOLETHEOREM","theoremType":"theorem"}""", o++);
        await SeedBlockAsync(doc.Id, "list", """{"ordered":false,"items":["WHOLELIST"]}""", o++);
        await SeedBlockAsync(doc.Id, "table",
            """{"headers":["WHOLEHEADER"],"rows":[["WHOLECELL"]]}""", o++);
        await SeedBlockAsync(doc.Id, "code", """{"code":"WHOLECODE","language":"python"}""", o++);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"WHOLECONCLUSION","level":1}""", o++);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", o);
        await SeedBibliographyEntryAsync(doc.Id, "ref1", "book",
            """{"author":"Euclid","title":"WHOLEBIBTITLE","year":"300"}""");

        var p = await ExportAsync(doc.Id);

        p.Status.Should().Be(HttpStatusCode.OK);
        foreach (var marker in new[]
                 {
                     "WHOLETITLE", "WHOLEAUTHOR", "WHOLEABSTRACT", "WHOLEINTRO", "WHOLETHEOREM",
                     "WHOLELIST", "WHOLEHEADER", "WHOLECELL", "WHOLECODE", "WHOLECONCLUSION",
                 })
        {
            p.MainTex.Should().Contain(marker, $"'{marker}' must reach main.tex");
        }

        p.Bib.Should().NotBeNull();
        p.Bib!.Should().Contain("WHOLEBIBTITLE");
        p.MainTex.Should().Contain(@"\usepackage{natbib}");

        p.MainTex.IndexOf("WHOLEABSTRACT", StringComparison.Ordinal)
            .Should().BeLessThan(p.MainTex.IndexOf("WHOLEINTRO", StringComparison.Ordinal));
    }
}
