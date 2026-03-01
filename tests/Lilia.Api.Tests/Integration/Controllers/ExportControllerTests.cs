using System.IO.Compression;
using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class ExportControllerTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";
    private const string OtherUserId = "test_user_002";

    public ExportControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Lilia.Core.Entities.Document> SeedDocWithBlocks()
    {
        await SeedUserAsync(UserId, "test@lilia.test", "Test User");
        var doc = await SeedDocumentAsync(UserId, "My Research Paper");
        return doc;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<(ZipArchive Archive, MemoryStream Stream)> GetZipResponse(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var ms = new MemoryStream(bytes);
        var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        return (archive, ms);
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull($"ZIP should contain {path}");
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }

    // ── Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportLatex_ReturnsZip_SingleFile()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Introduction","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"This is some content."}""", 1);

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?structure=single");

        var (archive, ms) = await GetZipResponse(response);
        using var _ = archive;
        using var __ = ms;

        var mainTex = ReadEntry(archive, "main.tex");
        mainTex.Should().Contain(@"\documentclass");
        mainTex.Should().Contain(@"\begin{document}");
        mainTex.Should().Contain("Introduction");
        mainTex.Should().Contain("This is some content.");
        mainTex.Should().Contain(@"\end{document}");

        archive.GetEntry("README.txt").Should().NotBeNull();
    }

    [Fact]
    public async Task ExportLatex_ReturnsZip_MultiFlatStructure()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Chapter One","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"First paragraph."}""", 1);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Chapter Two","level":1}""", 2);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Second paragraph."}""", 3);

        var response = await Client.GetAsync(
            $"/api/documents/{doc.Id}/export/latex?structure=multi&multiFileLayout=flat");

        var (archive, ms) = await GetZipResponse(response);
        using var _ = archive;
        using var __ = ms;

        archive.GetEntry("main.tex").Should().NotBeNull();
        archive.GetEntry("preamble.tex").Should().NotBeNull();
        archive.GetEntry("chapters/chapter-01.tex").Should().NotBeNull();
        archive.GetEntry("chapters/chapter-02.tex").Should().NotBeNull();

        var mainTex = ReadEntry(archive, "main.tex");
        mainTex.Should().Contain(@"\input{chapters/chapter-01}");
        mainTex.Should().Contain(@"\input{chapters/chapter-02}");

        var chap1 = ReadEntry(archive, "chapters/chapter-01.tex");
        chap1.Should().Contain("Chapter One");
        chap1.Should().Contain("First paragraph.");

        var chap2 = ReadEntry(archive, "chapters/chapter-02.tex");
        chap2.Should().Contain("Chapter Two");
        chap2.Should().Contain("Second paragraph.");
    }

    [Fact]
    public async Task ExportLatex_ReturnsZip_MultiOverleafStructure()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "abstract", """{"text":"This is the abstract."}""", 0);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Introduction","level":1}""", 1);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Some content."}""", 2);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Methods","level":1}""", 3);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Method details."}""", 4);

        var response = await Client.GetAsync(
            $"/api/documents/{doc.Id}/export/latex?structure=multi&multiFileLayout=overleaf");

        var (archive, ms) = await GetZipResponse(response);
        using var _ = archive;
        using var __ = ms;

        archive.GetEntry("main.tex").Should().NotBeNull();
        archive.GetEntry("preamble.tex").Should().NotBeNull();
        archive.GetEntry("frontmatter/abstract.tex").Should().NotBeNull();
        archive.GetEntry("chap1/chapter.tex").Should().NotBeNull();
        archive.GetEntry("chap2/chapter.tex").Should().NotBeNull();

        var mainTex = ReadEntry(archive, "main.tex");
        mainTex.Should().Contain(@"\input{frontmatter/abstract}");
        mainTex.Should().Contain(@"\input{chap1/chapter}");
        mainTex.Should().Contain(@"\input{chap2/chapter}");

        var abstractTex = ReadEntry(archive, "frontmatter/abstract.tex");
        abstractTex.Should().Contain(@"\begin{abstract}");
        abstractTex.Should().Contain("This is the abstract.");
    }

    [Fact]
    public async Task ExportLatex_IncludesBibliography()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"See @cite{smith2024}."}""", 0);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", 1);

        await SeedBibliographyEntryAsync(doc.Id, "smith2024", "article",
            """{"title":"Deep Learning","author":"Smith, John","year":"2024","journal":"Nature"}""");
        await SeedBibliographyEntryAsync(doc.Id, "doe2023", "book",
            """{"title":"Algorithms","author":"Doe, Jane","year":"2023","publisher":"MIT Press"}""");

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?structure=single");

        var (archive, ms) = await GetZipResponse(response);
        using var _ = archive;
        using var __ = ms;

        archive.GetEntry("references.bib").Should().NotBeNull();

        var bib = ReadEntry(archive, "references.bib");
        bib.Should().Contain("@article{smith2024,");
        bib.Should().Contain("author = {Smith, John}");
        bib.Should().Contain("title = {Deep Learning}");
        bib.Should().Contain("journal = {Nature}");
        bib.Should().Contain("@book{doe2023,");
        bib.Should().Contain("publisher = {MIT Press}");

        var mainTex = ReadEntry(archive, "main.tex");
        mainTex.Should().Contain(@"\bibliographystyle{plain}");
        mainTex.Should().Contain(@"\bibliography{references}");
    }

    [Fact]
    public async Task ExportLatex_ReturnsNotFound_WhenDocumentMissing()
    {
        await SeedUserAsync(UserId);
        var fakeId = Guid.NewGuid();

        var response = await Client.GetAsync($"/api/documents/{fakeId}/export/latex");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportLatex_ReturnsNotFound_WhenNotOwner()
    {
        await SeedUserAsync(UserId);
        await SeedUserAsync(OtherUserId);
        var doc = await SeedDocumentAsync(OtherUserId, "Other's Doc");

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex");

        // GetDocumentAsync returns null for non-owner → NotFound
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportLatex_HandlesAllBlockTypes()
    {
        var doc = await SeedDocWithBlocks();
        var sortOrder = 0;

        await SeedBlockAsync(doc.Id, "abstract", """{"text":"Abstract text."}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Section 1","level":1}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Paragraph text."}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "equation", """{"latex":"E = mc^2","mode":"display"}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "figure", """{"src":"test.png","caption":"A figure"}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "table", """{"headers":["A","B"],"rows":[["1","2"],["3","4"]]}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "code", """{"code":"print('hello')","language":"python"}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "list", """{"items":["one","two"],"ordered":true}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "blockquote", """{"text":"A wise quote."}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "theorem", """{"theoremType":"theorem","text":"Some theorem.","title":"Main"}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "tableOfContents", """{}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "pageBreak", """{}""", sortOrder++);
        await SeedBlockAsync(doc.Id, "bibliography", """{}""", sortOrder);

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?structure=single");

        var (archive, ms) = await GetZipResponse(response);
        using var _ = archive;
        using var __ = ms;

        var mainTex = ReadEntry(archive, "main.tex");
        mainTex.Should().Contain(@"\begin{abstract}");
        mainTex.Should().Contain(@"\section{Section 1}");
        mainTex.Should().Contain("Paragraph text.");
        mainTex.Should().Contain(@"\begin{equation}");
        mainTex.Should().Contain("E = mc^2");
        mainTex.Should().Contain(@"\begin{figure}");
        mainTex.Should().Contain(@"\begin{table}");
        mainTex.Should().Contain(@"\begin{lstlisting}");
        mainTex.Should().Contain(@"\begin{enumerate}");
        mainTex.Should().Contain(@"\begin{quote}");
        mainTex.Should().Contain(@"\begin{theorem}[Main]");
        mainTex.Should().Contain(@"\tableofcontents");
        mainTex.Should().Contain(@"\newpage");
    }

    [Fact]
    public async Task ExportLatex_MathNotEscaped()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "equation", """{"latex":"x^2 + y_1 = \\frac{a}{b}","mode":"display"}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Inline math: $x^2 + y_1$ is here."}""", 1);

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?structure=single");

        var (archive, ms) = await GetZipResponse(response);
        using var _ = archive;
        using var __ = ms;

        var mainTex = ReadEntry(archive, "main.tex");
        // Equation block should NOT escape math content
        mainTex.Should().Contain("x^2 + y_1");
        mainTex.Should().NotContain("\\textasciicircum{}");
        // Inline math in paragraph should be preserved
        mainTex.Should().Contain("$x^2 + y_1$");
    }

    [Fact]
    public async Task ExportLatex_CustomOptions()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Hello."}""", 0);

        var response = await Client.GetAsync(
            $"/api/documents/{doc.Id}/export/latex?documentClass=report&fontSize=12pt&paperSize=letterpaper&bibliographyStyle=IEEEtran");

        var (archive, ms) = await GetZipResponse(response);
        using var _ = archive;
        using var __ = ms;

        var mainTex = ReadEntry(archive, "main.tex");
        mainTex.Should().Contain(@"\documentclass[12pt,letterpaper]{report}");
    }
}
