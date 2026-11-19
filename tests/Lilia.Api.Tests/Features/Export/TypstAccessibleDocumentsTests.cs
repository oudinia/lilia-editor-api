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
/// The three documents checked with veraPDF -f ua1 before Typst's accessible PDF went live (launch-bot #259, 19 Nov
/// 2026): a plain article (headings, paragraphs, lists), one with a table and figures (one described, one with only a
/// caption, one with no image yet) and a French one. Each is made the way production makes it: stored in Postgres,
/// read by PreviewRenderService, built by TypstExportService and compiled by the real typst binary as PDF/UA-1; the
/// article and the figures document through DocumentExportService's "tagged" export itself.
///
/// <para>Set LILIA_UA1_PDF_DIR to a directory to keep the three PDFs there, for veraPDF:
/// <c>docker run --rm -v "$DIR:/data" verapdf/cli:v1.30.3 -f ua1 /data/article.pdf</c>.</para>
/// </summary>
public class TypstAccessibleDocumentsTests : IntegrationTestBase
{
    public TypstAccessibleDocumentsTests(TestDatabaseFixture fixture) : base(fixture) { }

    // A 1×1 PNG, staged as the figures' image (DocumentImageStager's job in production).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private const string ImageUrl = "https://cdn.example.org/doc/sites.png";
    private const string StagedPath = "figures/sites.png";

    private PreviewRenderService Preview()
    {
        var stager = new Mock<IDocumentImageStager>();
        stager.Setup(s => s.StageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StagedImages(
                new Dictionary<string, string> { [ImageUrl] = StagedPath },
                new Dictionary<string, byte[]> { [StagedPath] = Png }));
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

    /// <summary>The export as the controller calls it; the LaTeX path is a mock that fails the test if reached.</summary>
    private DocumentExportService Export()
    {
        var latex = new Mock<ILaTeXExportService>(MockBehavior.Strict);
        return new DocumentExportService(
            CreateDbContext(),
            new Mock<Lilia.Import.Interfaces.IDocxExportService>().Object,
            new Mock<IRenderService>().Object,
            latex.Object,
            new Mock<ILaTeXRenderService>().Object,
            Preview(),
            new Mock<IStorageService>().Object,
            NullLogger<DocumentExportService>.Instance);
    }

    private async Task<Guid> ADocument(string title, string language, params (string Type, string Json)[] blocks)
    {
        var owner = $"user-{Guid.NewGuid():N}";
        await SeedUserAsync(owner);
        var id = (await SeedDocumentAsync(owner, title)).Id;
        await using (var db = CreateDbContext())
            await db.Documents.Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.Language, language));
        for (var i = 0; i < blocks.Length; i++) await SeedBlockAsync(id, blocks[i].Type, blocks[i].Json, i + 1);
        return id;
    }

    private static void Keep(string name, byte[] pdf)
    {
        var dir = Environment.GetEnvironmentVariable("LILIA_UA1_PDF_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), pdf);
    }

    // The PDF with its compressed streams inflated beside it, to read its catalog and tags.
    private static string Raw(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var output = new System.Text.StringBuilder(text);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "stream\r?\n"))
        {
            try
            {
                using var z = new System.IO.Compression.ZLibStream(new MemoryStream(pdf, m.Index + m.Length, pdf.Length - m.Index - m.Length), System.IO.Compression.CompressionMode.Decompress);
                using var inflated = new MemoryStream();
                z.CopyTo(inflated);
                output.Append(System.Text.Encoding.Latin1.GetString(inflated.ToArray()));
            }
            catch (InvalidDataException) { }
        }
        return output.ToString();
    }

    [Fact]
    public async Task A_plain_article_is_exported_as_Typsts_PDF_UA_1()
    {
        Typst.TypstEnvironment.Require();
        var id = await ADocument("Rivers in winter", "en",
            ("heading", """{"text":"Introduction","level":1}"""),
            ("paragraph", """{"text":"Rivers cool slowly in the autumn and reach their lowest temperature in the middle of the winter."}"""),
            ("heading", """{"text":"Where we measured","level":2}"""),
            ("paragraph", """{"text":"We measured the water at three sites every week of the year, at the same hour of the morning."}"""),
            ("list", """{"items":["The northern site, near the spring","The middle site, below the town","The southern site, at the mouth"],"ordered":false}"""),
            ("list", """{"items":["Fill the bottle","Read the thermometer","Write down the time"],"ordered":true}"""),
            ("heading", """{"text":"Conclusion","level":1}"""),
            ("paragraph", """{"text":"The coldest week was the same at every site."}"""));

        var (pdf, engine, _) = await Export().ExportToPdfWithProblemsAsync(id, "tagged", null, false);

        engine.Should().Be("typst");
        Raw(pdf).Should().Contain("pdfuaid");
        Keep("article.pdf", pdf);
    }

    [Fact]
    public async Task A_document_with_a_table_and_figures_is_exported_as_Typsts_PDF_UA_1()
    {
        Typst.TypstEnvironment.Require();
        var id = await ADocument("Sites and readings", "en",
            ("heading", """{"text":"Sites","level":1}"""),
            ("paragraph", """{"text":"The map below shows where the water was measured during the whole year."}"""),
            ("figure", $$"""{"src":"{{ImageUrl}}","alt":"A map of the valley with three sites marked along the river","caption":"The three sites"}"""),
            ("figure", $$"""{"src":"{{ImageUrl}}","caption":"The northern site in January"}"""),
            ("figure", """{"src":"","caption":"A photograph still to come"}"""),
            ("table", """{"headers":["Site","Lowest","Highest"],"rows":[["North","2","14"],["Middle","3","17"],["South","4","19"]],"caption":"Temperatures in degrees"}"""));

        var (pdf, engine, _) = await Export().ExportToPdfWithProblemsAsync(id, "tagged", null, false);

        engine.Should().Be("typst");
        Raw(pdf).Should().Contain("pdfuaid");
        Keep("figures.pdf", pdf);
    }

    [Fact]
    public async Task A_French_document_is_made_as_PDF_UA_1_in_French()
    {
        // Automatic sends a document set to French to LaTeX today (PageSetupRouting: "language"), so its accessible PDF
        // is LaTeX's. Typst's accessible build must still be right for it: asked here of PreviewRenderService directly.
        Typst.TypstEnvironment.Require();
        var id = await ADocument("La température des rivières", "fr",
            ("heading", """{"text":"Introduction","level":1}"""),
            ("paragraph", """{"text":"Les rivières se refroidissent lentement en automne et sont les plus froides au milieu de l'hiver."}"""),
            ("list", """{"items":["Le site du nord","Le site du milieu","Le site du sud"],"ordered":false}"""),
            ("figure", $$"""{"src":"{{ImageUrl}}","caption":"Les trois sites de mesure"}"""));

        var made = await Preview().TryAccessibleTypstPdfAsync(id);

        made.WhyLatex.Should().BeNull();
        Raw(made.Pdf!).Should().Contain("/Lang(fr)");
        Keep("french.pdf", made.Pdf!);
    }
}
