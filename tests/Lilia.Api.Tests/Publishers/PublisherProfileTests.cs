using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>
/// Publisher profiles (publisher templates phase 1, step 3, 25 Oct 2026): venue + stage → class, options,
/// bibliography style. Setting one on a document sets its class; every profile compiles a full document.
/// </summary>
public class PublisherProfileTests
{
    [Fact]
    public void The_catalogue_is_consistent()
    {
        PublisherProfiles.All.Select(p => p.Id).Should().OnlyHaveUniqueItems();
        PublisherProfiles.CatalogVersion.Should().NotBeNullOrWhiteSpace();
        foreach (var p in PublisherProfiles.All)
        {
            PublisherClasses.Is(p.DocumentClass).Should().BeTrue(p.Id);
            LaTeXPreambleBuilder.IsSafeClass(p.DocumentClass).Should().BeTrue($"{p.Id}: the class must be allowed");
            p.Stage.Should().BeOneOf("review", "final");
            p.RequiredMetadata.Should().Contain("title");
            p.GuidelinesUrl.Should().StartWith("https://");
        }
        PublisherProfiles.Find("ACM-SIGCONF-REVIEW")!.ClassOptions.Should().Be("sigconf,review,anonymous");
    }

    [Fact]
    public void The_profile_decides_the_bibliography_style()
    {
        PublisherProfiles.BibliographyStyleFor("elsevier-final", "elsarticle").Should().Be("elsarticle-num");
        PublisherProfiles.BibliographyStyleFor(null, "llncs").Should().Be("splncs04");
        PublisherProfiles.BibliographyStyleFor(null, "article").Should().BeNull();
    }

    private static Document Doc(string cls = "article", string? profile = null) =>
        new() { Id = Guid.NewGuid(), Title = "T", OwnerId = "u", LatexDocumentClass = cls, PublisherProfile = profile };

    [Fact]
    public void Setting_a_profile_sets_the_class_clearing_keeps_it_and_another_class_drops_the_profile()
    {
        var doc = Doc();
        DocumentService.ApplyPublisherProfile(doc, "acm-sigconf-final");
        (doc.PublisherProfile, doc.LatexDocumentClass, doc.LatexDocumentClassOptions).Should().Be(("acm-sigconf-final", "acmart", "sigconf"));
        DocumentService.ApplyPublisherProfile(doc, "lncs-submission");
        (doc.LatexDocumentClass, doc.LatexDocumentClassOptions).Should().Be(("llncs", null));

        DocumentService.ApplyPublisherProfile(doc, "");
        (doc.PublisherProfile, doc.LatexDocumentClass).Should().Be((null, "llncs"), "clearing the profile leaves the class");

        var moved = Doc("article", "ieee-conference");
        DocumentService.ApplyPublisherProfile(moved, null);
        moved.PublisherProfile.Should().BeNull("the class no longer matches the profile");
        var kept = Doc("IEEEtran", "ieee-conference");
        DocumentService.ApplyPublisherProfile(kept, null);
        kept.PublisherProfile.Should().Be("ieee-conference");

        var act = () => DocumentService.ApplyPublisherProfile(Doc(), "nature-2026");
        act.Should().Throw<PublisherProfileException>().WithMessage("*Unknown publisher profile*");
    }

    public static TheoryData<string> Profiles() => new(PublisherProfiles.All.Select(p => p.Id));

    [Trait("Category", "RequiresTeX")]
    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task Every_profile_compiles_a_full_document(string id)
    {
        var p = PublisherProfiles.Find(id)!;
        var doc = PublisherClassTests.Doc(p.DocumentClass, p.ClassOptions, "Ada Lovelace\\\\Analytical Engines\\\\UK \\and Alan Turing", new[] { "LaTeX", "publishing" });
        doc.PublisherProfile = p.Id;
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());
        tex.Should().Contain($@"\bibliographystyle{{{p.BibliographyStyle}}}");
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", id);
    }
}

/// <summary>The profile through the API: GET /api/publishers, and PUT on a document.</summary>
[Collection("Integration")]
public class PublisherProfileApiTests : IntegrationTestBase
{
    private readonly string _userId = $"pubprof-{Guid.NewGuid():N}"[..28];
    public PublisherProfileApiTests(TestDatabaseFixture fixture) : base(fixture) { }
    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    [Fact]
    public async Task The_picker_lists_the_profiles_and_a_document_takes_one()
    {
        var api = CreateClientAs(_userId);
        var list = await api.GetFromJsonAsync<JsonElement>("/api/publishers");
        list.GetProperty("catalogVersion").GetString().Should().Be(PublisherProfiles.CatalogVersion);
        list.GetProperty("profiles").GetArrayLength().Should().Be(PublisherProfiles.All.Count);

        var doc = await SeedDocumentAsync(_userId, "Paper");
        var put = await api.PutAsJsonAsync($"/api/documents/{doc.Id}", new { publisherProfile = "acm-sigconf-review" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await put.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("publisherProfile").GetString().Should().Be("acm-sigconf-review");
        dto.GetProperty("latexDocumentClass").GetString().Should().Be("acmart");
        dto.GetProperty("latexDocumentClassOptions").GetString().Should().Be("sigconf,review,anonymous");

        var bad = await api.PutAsJsonAsync($"/api/documents/{doc.Id}", new { publisherProfile = "nature-2026" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("invalid_publisher_profile");
    }
}
