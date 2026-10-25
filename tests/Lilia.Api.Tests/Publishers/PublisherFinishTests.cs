using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Infrastructure.Data.Seeds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>
/// Publisher phase 1's last pieces (25 Oct 2026): plain tables in a publisher's LaTeX (NFR-3), one
/// starter template per publisher class, and the publisher findings in the document check.
/// </summary>
public class PublisherFinishTests
{
    private static string Export(Lilia.Core.Entities.Document doc) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());

    [Fact]
    public void PlainTables_unwraps_the_head_cells_and_drops_the_hooks()
    {
        var tex = LatexText.HeadRowFallback + "\n" + LatexText.TableHeadFallback + "\n" + LatexText.FewRows + "\n"
            + @"\begin{tabular}{ll}" + "\n" + LatexText.HeadRow + LatexText.TableHead(@"\textbf{A {b} \{}") + " & " + LatexText.TableHead("$x^{2}$") + @" \\" + "\n"
            + @"\end{tabular}";
        PublisherClasses.PlainTables(tex).Should().Be(@"\begin{tabular}{ll}" + "\n" + @"\textbf{A {b} \{} & $x^{2}$ \\" + "\n" + @"\end{tabular}");
    }

    [Fact]
    public void A_publisher_document_exports_no_Lilia_table_macros_and_an_article_keeps_them()
    {
        var acm = PublisherClassTests.Doc("acmart", "sigconf");
        var tex = Export(acm);
        tex.Should().Contain(@"\begin{tabular}").And.Contain("Compound");
        tex.Should().NotContain(@"\liliaHeadRow").And.NotContain(@"\liliaTableHead").And.NotContain(@"\liliaFewRows");

        var preview = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(acm);
        preview.Should().NotContain(@"\liliaTableHead", "the preview compiles what the export writes");

        Export(PublisherClassTests.Doc("article")).Should().Contain(@"\liliaTableHead", "a themed document needs its hooks");
    }

    [Fact]
    public void Each_publisher_class_has_a_starter_template_that_only_asks_for_its_sample_text_to_go()
    {
        var starters = LaunchTemplateSeeder.Build().Where(d => d.PublisherProfile is not null).ToList();
        starters.Select(d => PublisherProfiles.Find(d.PublisherProfile)!.DocumentClass)
            .Should().BeEquivalentTo(["acmart", "IEEEtran", "elsarticle", "llncs"]);
        foreach (var doc in starters)
        {
            var profile = PublisherProfiles.Find(doc.PublisherProfile)!;
            doc.LatexDocumentClass.Should().Be(profile.DocumentClass, doc.TemplateName);
            (doc.LatexDocumentClassOptions ?? "").Should().Be(profile.ClassOptions, doc.TemplateName);
            var report = PublisherCompliance.Check(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), Export(doc));
            report.Findings.Select(f => f.Code).Distinct().Should().Equal(["sample_text"], doc.TemplateName);
        }
        LaunchTemplateSeeder.Build().Select(d => d.TemplateThumbnail).Should().OnlyHaveUniqueItems("the seed's version key is per template");
    }
}

/// <summary>The document check (Ask Lilia, check_document) says what the publisher would reject.</summary>
[Collection("Integration")]
public class PublisherCheckDocumentTests : IntegrationTestBase
{
    private readonly string _userId = $"pubchk-{Guid.NewGuid():N}"[..28];
    public PublisherCheckDocumentTests(TestDatabaseFixture fixture) : base(fixture) { }
    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_check_carries_the_publisher_findings()
    {
        var api = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "A Paper");
        await SeedBlockAsync(doc.Id, "title", """{"title":"A Paper","author":"Ada Lovelace"}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Hello."}""", 1);
        (await api.PutAsJsonAsync($"/api/documents/{doc.Id}", new { publisherProfile = "acm-sigconf-final" })).EnsureSuccessStatusCode();

        using var scope = Fixture.Factory.Services.CreateScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var checker = scope.ServiceProvider.GetRequiredService<IDocumentCheckService>();
        var check = await checker.CheckAsync((await docs.GetDocumentAsync(doc.Id, _userId))!);

        var report = JsonSerializer.SerializeToElement(check.Report);
        var publisher = report.GetProperty("publisher");
        publisher.GetProperty("profile").GetString().Should().Be("acm-sigconf-final");
        publisher.GetProperty("status").GetString().Should().Be("errors");
        publisher.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("code").GetString())
            .Should().Contain(["abstract", "keywords", "country"]);
        check.Problems.Should().NotContain(p => p.Message.Contains("abstract"), "the author supplies these; they are not LaTeX to repair");
    }
}
