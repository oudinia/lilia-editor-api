using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Interfaces;
using Lilia.Engines;
using Lilia.Import.Interfaces;
using Lilia.Import.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Lilia.Api.Tests.Integration.References;

/// <summary>
/// The Typst preview keeps the numbers it gave each label, as the pdflatex
/// export does — so after the preview an author already looks at, the editor
/// shows "table 1" instead of the pending slot. Real Postgres, real typst.
/// </summary>
[Collection("Integration")]
public class TypstPreviewKeepsNumbersTests : IntegrationTestBase
{
    public TypstPreviewKeepsNumbersTests(TestDatabaseFixture fixture) : base(fixture) { }

    private PreviewRenderService Service()
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

    private async Task<Guid> ADocument(params (string Type, string Json)[] blocks)
    {
        var owner = $"user-{Guid.NewGuid():N}";
        await SeedUserAsync(owner);
        var id = (await SeedDocumentAsync(owner)).Id;
        for (var i = 0; i < blocks.Length; i++) await SeedBlockAsync(id, blocks[i].Type, blocks[i].Json, i + 1);
        return id;
    }

    private async Task<IReadOnlyDictionary<string, Lilia.Core.Blocks.AuxLabel>> Kept(Guid id)
    {
        await using var db = CreateDbContext();
        var d = await db.Documents.AsNoTracking().FirstAsync(x => x.Id == id);
        return LabelNumbers.Parse(d.LabelNumbers);
    }

    [Fact]
    public async Task A_preview_keeps_the_numbers_it_printed()
    {
        var id = await ADocument(
            ("heading", """{"text":"Intro","level":1}"""),
            ("heading", """{"text":"Method","level":1,"label":"sec:method"}"""),
            ("heading", """{"text":"Details","level":2,"label":"sec:details"}"""),
            ("table", """{"caption":"First","label":"tab:one","rows":[["a"]]}"""),
            ("table", """{"caption":"Second","label":"tab:two","rows":[["b"]]}"""),
            ("equation", """{"source":"c = d","displayMode":true,"numbered":false}"""),
            ("equation", """{"source":"a = b","displayMode":true,"label":"eq:loss"}"""),
            ("paragraph", """{"text":"See \\cref{tab:two}."}"""));

        var pdf = await Service().TryTypstPdfAsync(id);

        pdf.Should().NotBeNull("the preview compiles on Typst");
        var kept = await Kept(id);
        kept.ToDictionary(k => k.Key, k => k.Value.Number).Should().Equal(new Dictionary<string, string?>
        {
            // What \ref prints in the PDF: the second section, its first
            // subsection, the second table, and the equation after an unnumbered one.
            ["sec:method"] = "2",
            ["sec:details"] = "2.1",
            ["tab:one"] = "1",
            ["tab:two"] = "2",
            ["eq:loss"] = "1",
        });
        kept["tab:two"].Page.Should().Be(1);
    }

    [Fact]
    public async Task Dates_the_numbers_so_the_panel_can_say_how_old_they_are()
    {
        var id = await ADocument(("table", """{"caption":"T","label":"tab:x","rows":[["a"]]}"""));
        var before = DateTime.UtcNow.AddSeconds(-1);

        await Service().TryTypstPdfAsync(id);

        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(x => x.Id == id)).LabelNumbersAt
            .Should().NotBeNull().And.BeAfter(before);
    }

    [Fact]
    public async Task A_document_without_labels_leaves_the_last_numbers_alone()
    {
        var id = await ADocument(("paragraph", """{"text":"No labels here."}"""));
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FirstAsync(x => x.Id == id);
            d.LabelNumbers = LabelNumbers.FromAux(@"\newlabel{tab:old}{{4}{2}}");
            await db.SaveChangesAsync();
        }

        await Service().TryTypstPdfAsync(id);

        (await Kept(id)).Should().ContainKey("tab:old");
    }
}
