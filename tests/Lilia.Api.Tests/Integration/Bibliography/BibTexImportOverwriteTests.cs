using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Lilia.Api.Tests.Integration.Bibliography;

/// <summary>
/// BibTeX import and the panel's "Overwrite existing entries" choice. The
/// checkbox had nothing to talk to — the API always replaced — and the panel's
/// import never reached this code at all (it sent `bibtex`, not
/// `bibTexContent`: every import was a 400). Real Postgres.
/// </summary>
[Collection("Integration")]
public class BibTexImportOverwriteTests : IntegrationTestBase
{
    public BibTexImportOverwriteTests(TestDatabaseFixture fixture) : base(fixture) { }

    private BibliographyService Service() =>
        new(CreateDbContext(), MockFactory(), NullLogger<BibliographyService>.Instance);

    private static IHttpClientFactory MockFactory()
    {
        var f = new Mock<IHttpClientFactory>();
        f.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        return f.Object;
    }

    private const string First = "@book{knuth1984, title = {Literate Programming}, year = {1984}}";
    private const string Second = "@book{knuth1984, title = {Literate Programming, 2nd}, year = {1992}}\n@article{new2020, title = {New}, year = {2020}}";

    private async Task<Guid> ADocument()
    {
        var owner = $"user-{Guid.NewGuid():N}";
        await SeedUserAsync(owner);
        return (await SeedDocumentAsync(owner)).Id;
    }

    private async Task<string?> TitleOf(Guid doc, string key)
    {
        await using var db = CreateDbContext();
        var e = await db.BibliographyEntries.AsNoTracking().FirstAsync(x => x.DocumentId == doc && x.CiteKey == key);
        return e.Data.RootElement.GetProperty("title").GetString();
    }

    [Fact]
    public async Task Without_overwrite_an_existing_key_is_kept_and_not_returned()
    {
        var doc = await ADocument();
        await Service().ImportBibTexAsync(doc, First);

        var returned = await Service().ImportBibTexAsync(doc, Second, overwrite: false);

        returned.Select(r => r.CiteKey).Should().Equal("new2020");
        (await TitleOf(doc, "knuth1984")).Should().Be("Literate Programming");
    }

    [Fact]
    public async Task With_overwrite_an_existing_key_is_replaced()
    {
        var doc = await ADocument();
        await Service().ImportBibTexAsync(doc, First);

        var returned = await Service().ImportBibTexAsync(doc, Second, overwrite: true);

        returned.Select(r => r.CiteKey).Should().BeEquivalentTo(["knuth1984", "new2020"]);
        (await TitleOf(doc, "knuth1984")).Should().Be("Literate Programming, 2nd");
    }

    [Fact]
    public async Task Every_braced_field_is_stored_including_nested_braces_and_several_lines()
    {
        // The old parser stopped at the first closing brace: this entry was
        // stored with no fields at all.
        var doc = await ADocument();
        await Service().ImportBibTexAsync(doc, """
            @book{knuth1984,
              author = {Donald E. Knuth},
              title = {Literate {P}rogramming},
              publisher = {CSLI},
              year = {1984}
            }
            """);

        await using var db = CreateDbContext();
        var data = (await db.BibliographyEntries.AsNoTracking().FirstAsync(x => x.DocumentId == doc)).Data.RootElement;
        data.GetProperty("author").GetString().Should().Be("Donald E. Knuth");
        data.GetProperty("title").GetString().Should().Contain("rogramming");
        data.GetProperty("year").GetString().Should().Be("1984");
        data.GetProperty("publisher").GetString().Should().Be("CSLI");
    }

    [Fact]
    public async Task Omitting_the_flag_keeps_the_old_behaviour_replace()
    {
        var doc = await ADocument();
        await Service().ImportBibTexAsync(doc, First);
        await Service().ImportBibTexAsync(doc, Second);
        (await TitleOf(doc, "knuth1984")).Should().Be("Literate Programming, 2nd");
    }
}
