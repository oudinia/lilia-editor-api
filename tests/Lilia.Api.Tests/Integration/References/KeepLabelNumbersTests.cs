using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Lilia.Core.Interfaces;

namespace Lilia.Api.Tests.Integration.References;

/// <summary>
/// The PDF export keeping each label's number — the half that runs on every
/// preview the author looks at. On real Postgres.
///
/// <para>The rules: numbers and a date are written together; a compile with no
/// labels leaves the last good numbers alone; and nothing here can fail the
/// export, because the PDF is what was asked for.</para>
/// </summary>
[Collection("Integration")]
public class KeepLabelNumbersTests : IntegrationTestBase
{
    public KeepLabelNumbersTests(TestDatabaseFixture fixture) : base(fixture) { }

    private DocumentExportService Service() => new(
        CreateDbContext(),
        new Mock<Lilia.Import.Interfaces.IDocxExportService>().Object,
        new Mock<IRenderService>().Object,
        new Mock<ILaTeXExportService>().Object,
        new Mock<ILaTeXRenderService>().Object,
        new Mock<IPreviewRenderService>().Object,
        new Mock<IStorageService>().Object,
        NullLogger<DocumentExportService>.Instance);

    private async Task<Guid> ADocument()
    {
        var owner = $"user-{Guid.NewGuid():N}";
        await SeedUserAsync(owner);
        return (await SeedDocumentAsync(owner)).Id;
    }

    private async Task<(string? Numbers, DateTime? At)> Kept(Guid id)
    {
        await using var db = CreateDbContext();
        var d = await db.Documents.AsNoTracking().FirstAsync(x => x.Id == id);
        return (d.LabelNumbers, d.LabelNumbersAt);
    }

    [Fact]
    public async Task Keeps_the_numbers_and_when_they_were_measured()
    {
        var id = await ADocument();
        var before = DateTime.UtcNow.AddSeconds(-1);

        await Service().KeepLabelNumbersAsync(id, @"\newlabel{tab:results}{{3}{7}}");

        var (numbers, at) = await Kept(id);
        LabelNumbers.Parse(numbers)["tab:results"].Number.Should().Be("3");
        at.Should().NotBeNull().And.BeAfter(before);
    }

    [Fact]
    public async Task A_compile_with_no_labels_leaves_the_last_good_numbers_alone()
    {
        // A preamble error produces no .aux worth reading. Blanking the numbers
        // then would make the panel lose them on every broken compile; the date
        // beside the old ones already says how old they are.
        var id = await ADocument();
        var svc = Service();
        await svc.KeepLabelNumbersAsync(id, @"\newlabel{tab:results}{{3}{7}}");
        var (_, firstAt) = await Kept(id);

        await Service().KeepLabelNumbersAsync(id, null);

        var (numbers, at) = await Kept(id);
        LabelNumbers.Parse(numbers).Should().ContainKey("tab:results");
        at.Should().Be(firstAt);
    }

    [Fact]
    public async Task A_later_compile_replaces_the_numbers()
    {
        var id = await ADocument();
        await Service().KeepLabelNumbersAsync(id, @"\newlabel{tab:results}{{3}{7}}");

        await Service().KeepLabelNumbersAsync(id, @"\newlabel{tab:results}{{4}{8}}");

        LabelNumbers.Parse((await Kept(id)).Numbers)["tab:results"].Number.Should().Be("4");
    }

    [Fact]
    public async Task Never_fails_the_export()
    {
        // A document that does not exist is the simplest way to make the write
        // go nowhere. It must return quietly — the PDF has already been made.
        var act = () => Service().KeepLabelNumbersAsync(Guid.NewGuid(), @"\newlabel{tab:x}{{1}{1}}");
        await act.Should().NotThrowAsync();
    }
}
