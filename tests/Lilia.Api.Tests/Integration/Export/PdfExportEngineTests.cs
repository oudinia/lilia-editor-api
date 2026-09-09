using System.Net;
using System.Text;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Export;

/// <summary>
/// Which TeX engine actually compiled the PDF.
///
/// <para><c>Document.LatexEngine</c> has always accepted pdflatex, xelatex and
/// lualatex, and the compiler has always supported all three. The export did
/// not: it called a render method with no engine parameter, so every document
/// compiled with pdflatex whatever it asked for. A paper written for lualatex
/// — system fonts, fontspec, anything unicode-heavy — exported as something
/// else entirely, silently.</para>
///
/// <para>These tests read the engine's fingerprint out of the produced file
/// rather than trusting a status code. pdfTeX and LuaTeX write their name into
/// the PDF in the clear; XeTeX compresses its metadata, so it is identified by
/// the absence of the other two — a weaker assertion, and called out as such
/// rather than dressed up.</para>
/// </summary>
[Collection("Integration")]
public class PdfExportEngineTests : IntegrationTestBase
{
    private readonly string _userId = $"pdf-eng-{Guid.NewGuid():N}"[..28];

    public PdfExportEngineTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    /// <summary>A document with something to typeset, set to compile with <paramref name="engine"/>.</summary>
    private async Task<Guid> SeedFor(string engine)
    {
        var doc = await SeedDocumentAsync(_userId, $"Engine {engine}");
        await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"A short paragraph, enough to typeset."}""");

        await using var db = CreateDbContext();
        var row = await db.Documents.FirstAsync(d => d.Id == doc.Id);
        row.LatexEngine = engine;
        await db.SaveChangesAsync();

        return doc.Id;
    }

    private async Task<(HttpStatusCode Status, byte[] Bytes)> ExportPdfAsync(
        Guid docId, string? engineQuery = null)
    {
        var url = $"/api/documents/{docId}/export/pdf"
                  + (engineQuery is null ? "" : $"?engine={engineQuery}");
        var response = await CreateClientAs(_userId).GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsByteArrayAsync());
    }

    private static bool Contains(byte[] pdf, string marker) =>
        Encoding.Latin1.GetString(pdf).Contains(marker, StringComparison.Ordinal);

    private static void ShouldBeAPdf(byte[] bytes)
    {
        bytes.Should().NotBeEmpty();
        Encoding.Latin1.GetString(bytes, 0, Math.Min(5, bytes.Length))
            .Should().StartWith("%PDF");
    }

    // ── the document's own setting ───────────────────────────────────────

    [Fact]
    public async Task ADocumentOnTheDefaultEngineMayBeServedByTypst()
    {
        // Deliberately weak, and worth stating rather than hiding: pdflatex is
        // the default every document is created with, so it carries no signal
        // that the author chose LaTeX. Typst is allowed to serve those, and
        // produces a PDF stamped by neither TeX engine. What is asserted is
        // that a real document comes back.
        var (status, pdf) = await ExportPdfAsync(await SeedFor("pdflatex"));

        status.Should().Be(HttpStatusCode.OK);
        ShouldBeAPdf(pdf);
    }

    [Fact]
    public async Task AskingForPdflatexExplicitlyGetsPdfTeX()
    {
        var (status, pdf) = await ExportPdfAsync(await SeedFor("pdflatex"), "pdflatex");

        status.Should().Be(HttpStatusCode.OK);
        Contains(pdf, "pdfTeX").Should().BeTrue("pdfTeX writes its name into the PDF");
    }

    [Fact]
    public async Task ADocumentSetToLualatexIsCompiledByLuaTeX()
    {
        // The regression: this used to come back stamped pdfTeX.
        // Choosing a non-default engine also suppresses the Typst path: those
        // choices exist to get fontspec and system fonts, which Typst does not
        // reproduce, so serving Typst here would ignore the author twice over.
        var (status, pdf) = await ExportPdfAsync(await SeedFor("lualatex"));

        status.Should().Be(HttpStatusCode.OK);
        ShouldBeAPdf(pdf);
        Contains(pdf, "LuaTeX").Should().BeTrue("the document asked for lualatex");
        Contains(pdf, "pdfTeX").Should().BeFalse("and must not have been given pdflatex instead");
    }

    [Fact]
    public async Task ADocumentSetToXelatexIsNotCompiledByTheOthers()
    {
        // XeTeX's producer string is inside a compressed object, so this is an
        // assertion by elimination: a XeTeX-produced file carries neither of
        // the other two names. Weaker than the tests above, and deliberately
        // not written to look stronger than it is.
        var (status, pdf) = await ExportPdfAsync(await SeedFor("xelatex"));

        status.Should().Be(HttpStatusCode.OK);
        ShouldBeAPdf(pdf);
        Contains(pdf, "pdfTeX").Should().BeFalse();
        Contains(pdf, "LuaTeX").Should().BeFalse();
    }

    // ── the query parameter ──────────────────────────────────────────────

    [Fact]
    public async Task AnExplicitEngineOverridesTheDocumentsSetting()
    {
        var docId = await SeedFor("pdflatex");

        var (status, pdf) = await ExportPdfAsync(docId, "lualatex");

        status.Should().Be(HttpStatusCode.OK);
        Contains(pdf, "LuaTeX").Should().BeTrue("the caller asked for lualatex explicitly");
    }

    [Fact]
    public async Task AnExplicitEngineDoesNotDetourThroughTypst()
    {
        // Any explicit LaTeX engine means "compile this as LaTeX". Reaching the
        // Typst-first path would ignore the request and hand back a file the
        // caller did not ask for.
        var docId = await SeedFor("pdflatex");

        var (status, pdf) = await ExportPdfAsync(docId, "xelatex");

        status.Should().Be(HttpStatusCode.OK);
        ShouldBeAPdf(pdf);
        Contains(pdf, "pdfTeX").Should().BeFalse();
        Contains(pdf, "LuaTeX").Should().BeFalse();
    }

    [Fact]
    public async Task AnUnrecognisedEngineStillProducesADocument()
    {
        var docId = await SeedFor("pdflatex");

        var (status, pdf) = await ExportPdfAsync(docId, "notanengine");

        status.Should().Be(HttpStatusCode.OK, "an unknown hint falls back, it does not fail");
        ShouldBeAPdf(pdf);
    }

    [Fact]
    public async Task ADocumentWithNoEngineRecordedStillExports()
    {
        var doc = await SeedDocumentAsync(_userId, "No engine set");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""");

        var (status, pdf) = await ExportPdfAsync(doc.Id);

        status.Should().Be(HttpStatusCode.OK);
        ShouldBeAPdf(pdf);
    }
}
