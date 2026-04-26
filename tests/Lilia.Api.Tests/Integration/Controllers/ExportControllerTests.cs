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

    // ─────────────────────────────────────────────────────────────────
    // ?mode= variants — added 2026-04-26 to support direct .tex
    // download + inline preview alongside the existing zip default.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportLatex_ModeZip_explicit_equivalent_to_default()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Hi."}""", 0);

        var defaultResp = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex");
        var explicitResp = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?mode=zip");

        defaultResp.StatusCode.Should().Be(HttpStatusCode.OK);
        explicitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        defaultResp.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        explicitResp.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var a = await defaultResp.Content.ReadAsByteArrayAsync();
        var b = await explicitResp.Content.ReadAsByteArrayAsync();
        // Allow ±300 bytes for ZIP timestamp metadata jitter
        Math.Abs(a.Length - b.Length).Should().BeLessThan(300);
    }

    [Fact]
    public async Task ExportLatex_ModeTex_returns_single_file_attachment()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Section A","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body text under section A."}""", 1);

        var resp = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?mode=tex");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/x-tex");
        resp.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        resp.Content.Headers.ContentDisposition.FileName.Should().EndWith(".tex");

        var text = await resp.Content.ReadAsStringAsync();
        text.Should().Contain(@"\documentclass");
        text.Should().Contain(@"\begin{document}");
        text.Should().Contain(@"\end{document}");
        text.Should().Contain("Section A");
        text.Should().Contain("Body text under section A");
    }

    [Fact]
    public async Task ExportLatex_ModePreview_returns_text_inline_no_disposition()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Preview body."}""", 0);

        var resp = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?mode=preview");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        resp.Content.Headers.ContentType!.CharSet?.ToLowerInvariant().Should().Be("utf-8");

        // No Content-Disposition: browser should display inline.
        resp.Content.Headers.ContentDisposition.Should().BeNull();

        var text = await resp.Content.ReadAsStringAsync();
        text.Should().Contain(@"\documentclass");
        text.Should().Contain("Preview body");
    }

    [Fact]
    public async Task ExportLatex_ModeInvalid_returns_400_with_explanation()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"x"}""", 0);

        var resp = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?mode=docx");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Unknown mode");
        body.Should().Contain("zip");
        body.Should().Contain("tex");
        body.Should().Contain("preview");
    }

    [Fact]
    public async Task ExportLatex_ModeTex_filename_sanitised()
    {
        await SeedUserAsync(UserId, "test@lilia.test", "Test User");
        var doc = await SeedDocumentAsync(UserId, "Weird/Title:With?Chars*.tex");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"x"}""", 0);

        var resp = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?mode=tex");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fileName = resp.Content.Headers.ContentDisposition!.FileName ?? "";
        fileName.Should().NotContain("/");
        fileName.Should().NotContain("?");
        fileName.Should().NotContain("*");
        fileName.Should().NotContain(":");
        fileName.Should().EndWith(".tex");
    }

    [Fact]
    public async Task ExportLatex_ModeTex_empty_doc_returns_well_formed_skeleton()
    {
        await SeedUserAsync(UserId, "test@lilia.test", "Test User");
        var doc = await SeedDocumentAsync(UserId, "Empty doc");

        var resp = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?mode=tex");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await resp.Content.ReadAsStringAsync();
        text.Should().Contain(@"\documentclass");
        text.Should().Contain(@"\begin{document}");
        text.Should().Contain(@"\end{document}");
    }

    [Fact]
    public async Task ExportLatex_ModeTex_missing_doc_returns_404()
    {
        var resp = await Client.GetAsync($"/api/documents/{Guid.NewGuid()}/export/latex?mode=tex");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExportLatex_ModePreview_missing_doc_returns_404()
    {
        var resp = await Client.GetAsync($"/api/documents/{Guid.NewGuid()}/export/latex?mode=preview");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Round-trip parity: tex mode content === main.tex inside the zip
    // for the same doc + options. Locks in the contract that "?mode=tex"
    // is just "the main.tex of the zip without the wrapping".
    [Fact]
    public async Task ExportLatex_ModeTex_matches_zip_main_tex()
    {
        var doc = await SeedDocWithBlocks();
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Title","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 1);

        var zipResp = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex");
        var (archive, ms) = await GetZipResponse(zipResp);
        using var _ = archive;
        using var __ = ms;
        var zipMainTex = ReadEntry(archive, "main.tex");

        var texResp = await Client.GetAsync($"/api/documents/{doc.Id}/export/latex?mode=tex");
        var texContent = await texResp.Content.ReadAsStringAsync();

        // Whitespace / line-ending normalisation — single-file content
        // should equal the main.tex from the zip.
        zipMainTex.Trim().Should().Be(texContent.Trim());
    }
}
