using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines.Publishers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>The template update service (phase 3, 25 Oct 2026): installed class versions, pinned per document.</summary>
// With PublisherTemplatePinTests, which swaps the version reader process-wide: never at the same time.
[Collection("Integration")]
public class PublisherClassVersionsTests
{
    [Theory]
    [InlineData(@"\ProvidesClass{acmart} [2026/08/16 v2.20 Typesetting articles for the ACM]", "2026/08/16 v2.20")]
    [InlineData(@"\ProvidesClass{IEEEtran}[2015/08/26 V1.8b by Michael Shell]", "2015/08/26 v1.8b")]
    [InlineData(@"\ProvidesClass{revtex4-2}  [2022/06/05 4.2f (https://journals.aps.org/revtex/ for documentation)]", "2022/06/05 v4.2f")]
    [InlineData(@"\ProvidesClass{amsart}[2020/05/29 v2.20.6]", "2020/05/29 v2.20.6")]
    [InlineData(@"\ProvidesClass{iopjournal}[2024/01/31 IOP Publishing journal article LaTeX template]", "2024/01/31")]
    [InlineData("\\def\\RCSversion{3.5}%\n\\def\\RCSdate{2026/01/09}%\n\\ProvidesClass{\\@shortjid}[\\RCSdate, \\RCSversion: \\@journal]", "2026/01/09 v3.5")]
    public void Reads_the_version_each_class_declares(string text, string expected) =>
        PublisherClassVersions.Read(text).Should().Be(expected);

    [Fact]
    public void Compares_versions_by_number_or_else_by_date()
    {
        PublisherClassVersions.Same("acmart 2.20", "2026/08/16 v2.20").Should().BeTrue();
        PublisherClassVersions.Same("acmart 2.20", "2026/11/01 v2.21").Should().BeFalse();
        PublisherClassVersions.Same("iopjournal 2024/01/31", "2024/01/31").Should().BeTrue();
        PublisherClassVersions.Same(null, "2024/01/31").Should().BeFalse();
    }

    [Fact]
    public void A_class_updated_since_the_venue_was_chosen_is_a_note_and_nothing_else()
    {
        var doc = PublisherClassTests.Doc("acmart", "sigconf", "Ada Lovelace\\\\Analytical Engines\\\\UK", new[] { "LaTeX" });
        doc.PublisherProfile = "acm-sigconf-final";
        doc.PublisherTemplateVersion = "2026/08/16 v2.20";
        var blocks = doc.Blocks.OrderBy(b => b.SortOrder).ToList();
        PublisherCompliance.Check(doc, blocks, "", "2026/08/16 v2.20").Findings.Should().NotContain(f => f.Code.StartsWith("template_"));
        var later = PublisherCompliance.Check(doc, blocks, "", "2026/11/01 v2.21").Findings;
        later.Should().ContainSingle(f => f.Code == "template_updated" && f.Severity == "info" && f.Message.Contains("2026/08/16 v2.20 → 2026/11/01 v2.21"));
        later.Should().ContainSingle(f => f.Code == "template_unverified" && f.Severity == "info");
    }

    /// <summary>
    /// What the nightly TeX run guards: every venue's verified version is the one installed. A TeX Live
    /// update or a new IOP/SIAM install that changes a class fails here until its profile is re-verified.
    /// </summary>
    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task Every_profile_was_verified_with_the_class_installed_here()
    {
        PublisherClassVersions.OverrideForTests(null);
        foreach (var p in PublisherProfiles.All)
        {
            var installed = await PublisherClassVersions.InstalledAsync(p.DocumentClass);
            installed.Should().NotBeNull(p.DocumentClass);
            PublisherClassVersions.Same(p.TemplateVersion, installed).Should().BeTrue($"{p.Id}: verified with {p.TemplateVersion}, installed {installed}");
        }
    }
}

/// <summary>A document pins the class version when its venue is chosen; GET /api/publishers says what is installed.</summary>
[Collection("Integration")]
public class PublisherTemplatePinTests : IntegrationTestBase
{
    private readonly string _userId = $"pubpin-{Guid.NewGuid():N}"[..28];
    public PublisherTemplatePinTests(TestDatabaseFixture fixture) : base(fixture) { }
    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    public override async Task DisposeAsync()
    {
        PublisherClassVersions.OverrideForTests(null);
        await base.DisposeAsync();
    }

    [Fact]
    public async Task Choosing_a_venue_pins_the_installed_version_and_clearing_it_unpins()
    {
        PublisherClassVersions.OverrideForTests(cls => cls == "acmart" ? "2026/08/16 v2.20" : null);
        var api = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "Paper");
        (await api.PutAsJsonAsync($"/api/documents/{doc.Id}", new { publisherProfile = "acm-sigconf-final" })).EnsureSuccessStatusCode();
        await using (var db = CreateDbContext())
            (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id)).PublisherTemplateVersion.Should().Be("2026/08/16 v2.20");

        var list = await api.GetFromJsonAsync<JsonElement>("/api/publishers");
        list.GetProperty("installed").GetProperty("acmart").GetString().Should().Be("2026/08/16 v2.20");

        (await api.PutAsJsonAsync($"/api/documents/{doc.Id}", new { publisherProfile = "" })).EnsureSuccessStatusCode();
        await using (var db = CreateDbContext())
            (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id)).PublisherTemplateVersion.Should().BeNull();
    }
}
