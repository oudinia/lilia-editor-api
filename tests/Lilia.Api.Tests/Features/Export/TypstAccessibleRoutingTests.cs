using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Interfaces;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// Where the accessible PDF is made (launch-bot #259, 19 Nov 2026). It always went to LaTeX's tagged PDF/UA-2; now a
/// document that compiles with Typst gets Typst's PDF/UA-1, and LaTeX keeps the documents Typst does not make, those
/// with maths (LaTeX tags it as MathML) and those Typst refuses as PDF/UA-1. On real Postgres; the last tests run Typst.
/// </summary>
public class TypstAccessibleRoutingTests : IntegrationTestBase
{
    public TypstAccessibleRoutingTests(TestDatabaseFixture fixture) : base(fixture) { }

    /// <summary>Thrown by the LaTeX project builder: reaching it means the export took the LaTeX path.</summary>
    private sealed class WentToLatex(bool tagged) : Exception("LaTeX path")
    {
        public bool Tagged { get; } = tagged;
    }

    private static readonly byte[] TypstPdf = "%PDF-1.7 typst ua-1"u8.ToArray();

    private (DocumentExportService Export, Mock<IPreviewRenderService> Preview) Service(TypstAccessiblePdf typstAnswer)
    {
        var preview = new Mock<IPreviewRenderService>();
        preview.Setup(p => p.TryAccessibleTypstPdfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(typstAnswer);
        preview.Setup(p => p.TryTypstPdfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(TypstPdf);
        var latex = new Mock<ILaTeXExportService>();
        latex.Setup(l => l.ExportToZipAsync(It.IsAny<Guid>(), It.IsAny<LaTeXExportOptions>()))
            .Returns((Guid _, LaTeXExportOptions o) => throw new WentToLatex(o.Tagged));
        var export = new DocumentExportService(
            CreateDbContext(),
            new Mock<Lilia.Import.Interfaces.IDocxExportService>().Object,
            new Mock<IRenderService>().Object,
            latex.Object,
            new Mock<ILaTeXRenderService>().Object,
            preview.Object,
            new Mock<IStorageService>().Object,
            NullLogger<DocumentExportService>.Instance);
        return (export, preview);
    }

    private async Task<Guid> ADocument(string? pdfEngine = null, params (string Type, string Json)[] blocks)
    {
        var owner = $"user-{Guid.NewGuid():N}";
        await SeedUserAsync(owner);
        var id = (await SeedDocumentAsync(owner, "River temperatures")).Id;
        if (pdfEngine is not null)
        {
            await using var db = CreateDbContext();
            await db.Documents.Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.PdfEngine, pdfEngine));
        }
        for (var i = 0; i < blocks.Length; i++) await SeedBlockAsync(id, blocks[i].Type, blocks[i].Json, i + 1);
        return id;
    }

    private static readonly (string, string)[] Prose =
    [
        ("heading", """{"text":"Method","level":1}"""),
        ("paragraph", """{"text":"We measured the water of the river every week of the year."}"""),
    ];

    [Fact]
    public async Task A_document_Typst_compiles_gets_Typsts_PDF_UA_1_and_never_reaches_LaTeX()
    {
        var id = await ADocument(null, Prose);
        var (export, preview) = Service(new TypstAccessiblePdf(TypstPdf, null));

        var (pdf, engine, problems) = await export.ExportToPdfWithProblemsAsync(id, "tagged", null, false);

        pdf.Should().Equal(TypstPdf);
        engine.Should().Be("typst");
        problems.Should().BeEmpty();
        preview.Verify(p => p.TryTypstPdfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task When_Typst_refuses_the_accessible_PDF_goes_to_LaTeXs_tagged_path()
    {
        var id = await ADocument(null, Prose);
        var (export, _) = Service(new TypstAccessiblePdf(null, "PDF/UA-1 error: skipped from heading level 1 to 3"));

        var act = () => export.ExportToPdfWithProblemsAsync(id, "tagged", null, false);

        (await act.Should().ThrowAsync<WentToLatex>()).Which.Tagged.Should().BeTrue();
    }

    [Fact]
    public async Task A_document_that_chose_LaTeX_keeps_LaTeXs_accessible_PDF()
    {
        var id = await ADocument("latex", Prose);
        var (export, preview) = Service(new TypstAccessiblePdf(TypstPdf, null));

        var act = () => export.ExportToPdfWithProblemsAsync(id, "tagged", null, false);

        (await act.Should().ThrowAsync<WentToLatex>()).Which.Tagged.Should().BeTrue();
        preview.Verify(p => p.TryAccessibleTypstPdfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_document_with_an_equation_goes_to_LaTeX_without_trying_Typst()
    {
        // Automatic already sends equations to LaTeX (ContentRouting); the accessible PDF follows the same routing.
        var id = await ADocument(null, ("equation", """{"source":"a^2 + b^2 = c^2","displayMode":true}"""));
        var (export, preview) = Service(new TypstAccessiblePdf(TypstPdf, null));

        var act = () => export.ExportToPdfWithProblemsAsync(id, "tagged", null, false);

        await act.Should().ThrowAsync<WentToLatex>();
        preview.Verify(p => p.TryAccessibleTypstPdfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_normal_PDF_still_comes_from_the_normal_Typst_compile()
    {
        var id = await ADocument(null, Prose);
        var (export, preview) = Service(new TypstAccessiblePdf(TypstPdf, null));

        var (_, engine, _) = await export.ExportToPdfWithProblemsAsync(id, "auto", null, false);

        engine.Should().Be("typst");
        preview.Verify(p => p.TryTypstPdfAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        preview.Verify(p => p.TryAccessibleTypstPdfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ────────── PreviewRenderService with real Typst ──────────

    private PreviewRenderService RealPreview()
    {
        var stager = new Mock<IDocumentImageStager>();
        stager.Setup(s => s.StageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StagedImages(new Dictionary<string, string>(), new Dictionary<string, byte[]>()));
        return new PreviewRenderService(
            CreateDbContext(),
            new TypstExportService(),
            new TypstCompileService(),
            new Mock<IRenderService>().Object,
            new Mock<ILaTeXRenderService>().Object,
            new Mock<IImportTelemetrySink>().Object,
            stager.Object,
            NullLogger<PreviewRenderService>.Instance);
    }

    [Fact]
    public async Task Typst_makes_the_accessible_PDF_of_a_stored_document_with_a_figure_and_a_table()
    {
        Typst.TypstEnvironment.Require();
        var id = await ADocument(null,
            ("heading", """{"text":"Method","level":1}"""),
            ("paragraph", """{"text":"We measured the water of the river every week of the year."}"""),
            ("figure", """{"src":"","caption":"The three sites along the valley"}"""),
            ("table", """{"headers":["Site","Mean"],"rows":[["North","4"],["South","19"]],"caption":"Averages"}"""));

        var made = await RealPreview().TryAccessibleTypstPdfAsync(id);

        made.WhyLatex.Should().BeNull();
        var raw = System.Text.Encoding.Latin1.GetString(made.Pdf!);
        raw.Should().StartWith("%PDF").And.Contain("pdfuaid");
    }

    [Fact]
    public async Task A_stored_document_with_maths_is_left_to_LaTeX_with_the_reason()
    {
        Typst.TypstEnvironment.Require();
        var id = await ADocument(null, ("paragraph", """{"text":"Energy is $E = mc^2$ for a body at rest."}"""));

        var made = await RealPreview().TryAccessibleTypstPdfAsync(id);

        made.Pdf.Should().BeNull();
        made.WhyLatex.Should().Contain("MathML");
    }

    [Fact]
    public async Task A_stored_document_whose_first_heading_is_level_2_is_left_to_LaTeX_with_Typsts_reason()
    {
        Typst.TypstEnvironment.Require();
        var id = await ADocument(null,
            ("heading", """{"text":"Background","level":2}"""),
            ("paragraph", """{"text":"We measured the water of the river every week of the year."}"""));

        var made = await RealPreview().TryAccessibleTypstPdfAsync(id);

        made.Pdf.Should().BeNull();
        made.WhyLatex.Should().Contain("first heading must be of level 1");
    }
}
