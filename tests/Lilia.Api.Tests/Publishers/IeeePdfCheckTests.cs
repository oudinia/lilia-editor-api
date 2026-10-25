using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>The IEEE PDF check (phase 3, 25 Oct 2026): IEEE Xplore's PDF specification 4.02.</summary>
public class IeeePdfCheckTests
{
    private const string Info = """
        Producer:        pdfTeX-1.40.29
        Encrypted:       no
        Page size:       612 x 792 pts (letter)
        Pages:           2
        PDF version:     1.5
        """;

    private const string Fonts = """
        name                                 type              encoding         emb sub uni object ID
        ------------------------------------ ----------------- ---------------- --- --- --- ---------
        JMGGCF+NimbusRomNo9L-Medi            Type 1            Custom           yes yes no      34  0
        [none]                               Type 3            Custom           yes no  no      40  0
        Helvetica                            Type 1            Standard         no  no  no      41  0
        """;

    private const string Images = """
        page   num  type   width height color comp bpc  enc interp  object ID x-ppi y-ppi size ratio
        --------------------------------------------------------------------------------------------
           1     0 image     600   400  rgb     3   8  image  no        12  0   150   150  117K 16%
           2     1 image    2400  1800  rgb     3   8  jpeg   no        13  0   600   600  300K 2.3%
           2     2 image    1200   900  gray    1   1  ccitt  no        14  0   300   300   10K 1%
        """;

    [Fact]
    public void Reads_poppler_and_applies_IEEE_Xplores_rules()
    {
        var facts = IeeePdfCheck.Parse(Info, Fonts, Images);
        facts.Version.Should().Be("1.5");
        facts.Pages.Should().Be(2);
        facts.PageSize.Should().Be("letter");
        facts.Fonts.Should().HaveCount(3);
        facts.Images.Should().HaveCount(3);

        var report = IeeePdfCheck.Judge(facts, "ieee-conference");
        report.Status.Should().Be("errors");
        report.Findings.Should().ContainSingle(f => f.Code == "pdf_font_embedded" && f.Message.Contains("Helvetica"));
        report.Findings.Should().ContainSingle(f => f.Code == "pdf_type3_font");
        report.Findings.Where(f => f.Code == "pdf_image_resolution").Select(f => f.Message[..23])
            .Should().BeEquivalentTo(["An image on page 1 prin", "An image on page 2 prin"], "150 dpi colour, and 300 dpi black and white (needs 600)");
        report.Findings.Should().Contain(f => f.Code == "pdf_page_size" && f.Message.Contains("2 pages, letter"));
    }

    [Fact]
    public void Old_or_secured_PDFs_are_errors()
    {
        var facts = IeeePdfCheck.Parse(Info.Replace("1.5", "1.3").Replace("Encrypted:       no", "Encrypted:       yes (print:no)"), "", "");
        var codes = IeeePdfCheck.Judge(facts, null).Findings.Where(f => f.Severity == "error").Select(f => f.Code);
        codes.Should().BeEquivalentTo(["pdf_version", "pdf_security"]);
    }

    [Fact]
    public void IEEE_documents_are_written_without_links_or_bookmarks()
    {
        var packages = PublisherClasses.Packages(Lilia.Engines.LaTeXPreamble.Packages, "IEEEtran");
        packages.Should().Contain(@"\PassOptionsToPackage{draft}{hyperref}").And.NotContain("hidelinks");
        PublisherClasses.Packages(Lilia.Engines.LaTeXPreamble.Packages, "elsarticle").Should().Contain("hidelinks", "only IEEE forbids links");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task An_IEEE_paper_compiled_by_Lilia_meets_the_specification()
    {
        var doc = PublisherClassTests.Doc("IEEEtran", "conference", "Ada Lovelace\\\\Analytical Engines", new[] { "LaTeX" });
        doc.PublisherProfile = "ieee-conference";
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);

        var facts = await IeeePdfCheck.ReadAsync(pdf);
        facts.Fonts.Should().NotBeEmpty().And.OnlyContain(f => f.Embedded);
        facts.PageSize.Should().Be("letter", "IEEEtran's default");
        var report = IeeePdfCheck.Judge(facts, "ieee-conference");
        report.Findings.Should().NotContain(f => f.Severity == "error");
        // No link annotations at all: hyperref in draft mode writes none.
        System.Text.Encoding.Latin1.GetString(pdf).Should().NotContain("/Subtype/Link").And.NotContain("/Subtype /Link");
    }
}

/// <summary>GET …/export/pdf-check, as the export page calls it.</summary>
[Collection("Integration")]
public class IeeePdfCheckApiTests : IntegrationTestBase
{
    private readonly string _userId = $"ieeepdf-{Guid.NewGuid():N}"[..28];
    public IeeePdfCheckApiTests(TestDatabaseFixture fixture) : base(fixture) { }
    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_check_compiles_and_reports_and_only_for_who_can_read()
    {
        var api = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "IEEE paper");
        await SeedBlockAsync(doc.Id, "title", """{"title":"IEEE paper","author":"Ada Lovelace\\\\Analytical Engines","keywords":["LaTeX"]}""", 0);
        await SeedBlockAsync(doc.Id, "abstract", """{"text":"It works."}""", 1);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Hello."}""", 2);
        (await api.PutAsJsonAsync($"/api/documents/{doc.Id}", new { publisherProfile = "ieee-conference" })).EnsureSuccessStatusCode();

        var res = await api.GetAsync($"/api/documents/{doc.Id}/export/pdf-check");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().NotBe("errors");
        body.GetProperty("pdf").GetProperty("pageSize").GetString().Should().Be("letter");
        body.GetProperty("spec").GetString().Should().Contain("4.02");

        var stranger = $"ieeepdf-x-{Guid.NewGuid():N}"[..28];
        await SeedUserAsync(stranger);
        (await CreateClientAs(stranger).GetAsync($"/api/documents/{doc.Id}/export/pdf-check")).IsSuccessStatusCode.Should().BeFalse();
    }
}
