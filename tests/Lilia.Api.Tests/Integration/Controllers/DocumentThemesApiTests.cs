using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Document themes over HTTP: the descriptors, the look on a document (read, validated write, the
/// class lock), and the PDF a themed document produces, through the preview compile and the
/// export with its per-export look and print-safe options.
/// </summary>
[Collection("Integration")]
public class DocumentThemesApiTests : IntegrationTestBase
{
    private readonly string _userId = $"themes-{Guid.NewGuid():N}"[..28];

    public DocumentThemesApiTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    private HttpClient Api => CreateClientAs(_userId);

    private async Task<Guid> SeedNotesAsync(string? cls = null)
    {
        var doc = await SeedDocumentAsync(_userId, "Theme notes");
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Eigenvalues","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Quartz zebra paragraph."}""", 1);
        await SeedBlockAsync(doc.Id, "table", """{"caption":"R","headers":["Model","Acc"],"rows":[["A","1"]]}""", 2);
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Diagonalisation","level":1}""", 3);
        if (cls is not null)
        {
            await using var db = CreateDbContext();
            var row = await db.Documents.FirstAsync(d => d.Id == doc.Id);
            row.LatexDocumentClass = cls;
            await db.SaveChangesAsync();
        }
        return doc.Id;
    }

    private async Task<JsonElement> GetDocAsync(Guid id)
    {
        var json = await Api.GetStringAsync($"/api/documents/{id}");
        return JsonDocument.Parse(json).RootElement;
    }

    private Task<HttpResponseMessage> PutLookAsync(Guid id, object look) =>
        Api.PutAsJsonAsync($"/api/documents/{id}", new { look });

    [Fact]
    public async Task Themes_lists_six_descriptors_with_availability()
    {
        var themes = JsonDocument.Parse(await Api.GetStringAsync("/api/themes")).RootElement;

        themes.GetArrayLength().Should().Be(6);
        var index = themes.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "index");
        index.GetProperty("for").GetString().Should().Be("Course notes, lecture series, handbooks");
        index.GetProperty("available").GetBoolean().Should().BeTrue("this machine has montserrat and sourceserifpro");
        index.GetProperty("fonts").GetProperty("headingCss").GetString().Should().Contain("Montserrat");
        index.GetProperty("colours").GetProperty("ink").GetString().Should().Be("#2F2E2C");
        index.GetProperty("printSafe").GetProperty("paper").GetString().Should().Be("#FFFFFF");
        index.GetProperty("sequence").GetArrayLength().Should().Be(8);
        index.GetProperty("tablesDefault").GetString().Should().Be("ruled");

        var cerulean = themes.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "cerulean");
        cerulean.GetProperty("sequence").ValueKind.Should().Be(JsonValueKind.Null, "sequence is written as null, not left out");

        foreach (var planned in new[] { "carnet", "gazette", "exposition" })
        {
            var t = themes.EnumerateArray().Single(x => x.GetProperty("id").GetString() == planned);
            t.GetProperty("available").GetBoolean().Should().BeFalse();
            t.GetProperty("status").GetString().Should().Be("planned");
        }
    }

    [Fact]
    public async Task The_package_can_be_downloaded()
    {
        var sty = await Api.GetStringAsync("/api/themes/lilia-theme.sty");
        sty.Should().Contain(@"\ProvidesPackage{lilia-theme}");
    }

    [Fact]
    public async Task A_new_document_reads_back_look_null_and_unlocked()
    {
        var doc = await GetDocAsync(await SeedNotesAsync());
        doc.GetProperty("look").ValueKind.Should().Be(JsonValueKind.Null);
        doc.GetProperty("lookLocked").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Put_stores_a_look_and_classic_clears_it()
    {
        var id = await SeedNotesAsync();

        var put = await PutLookAsync(id, new { theme = "index", paper = "white", pins = new Dictionary<string, int> { ["b1"] = 2 } });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var look = (await GetDocAsync(id)).GetProperty("look");
        look.GetProperty("theme").GetString().Should().Be("index");
        look.GetProperty("paper").GetString().Should().Be("white");
        look.GetProperty("pins").GetProperty("b1").GetInt32().Should().Be(2);

        // A PUT without a look leaves it alone.
        (await Api.PutAsJsonAsync($"/api/documents/{id}", new { title = "Renamed" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetDocAsync(id)).GetProperty("look").GetProperty("theme").GetString().Should().Be("index");

        (await PutLookAsync(id, new { theme = "classic" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetDocAsync(id)).GetProperty("look").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("""{"theme":"neon"}""", "Valid values: classic, cerulean, index")]
    [InlineData("""{"theme":"index","paper":"cream"}""", "Valid values: theme, white")]
    [InlineData("""{"theme":"index","pins":{"b1":9}}""", "from 0 to 7")]
    [InlineData("""{"theme":"carnet"}""", "planned")]
    public async Task Put_refuses_an_invalid_look_with_400_and_the_valid_values(string look, string named)
    {
        var id = await SeedNotesAsync();
        var body = new StringContent($$"""{"look":{{look}}}""", Encoding.UTF8, "application/json");

        var response = await Api.PutAsync($"/api/documents/{id}", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(named);
        (await GetDocAsync(id)).GetProperty("look").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_publisher_class_locks_the_look()
    {
        var id = await SeedNotesAsync("IEEEtran");

        (await GetDocAsync(id)).GetProperty("lookLocked").GetString()
            .Should().Be("IEEEtran sets its own look, so themes are off for this document.");
        var refused = await PutLookAsync(id, new { theme = "cerulean" });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("IEEEtran sets its own look");
    }

    [Fact]
    public async Task The_preview_compiles_a_themed_document_in_the_themes_faces()
    {
        var id = await SeedNotesAsync();
        (await PutLookAsync(id, new { theme = "index" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Api.PostAsync($"/api/latex/{id}/pdf", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.GetValues("X-Render-Engine").Single().Should().NotBe("typst", "a theme is a LaTeX package");
        var pdf = await response.Content.ReadAsByteArrayAsync();
        Encoding.Latin1.GetString(pdf, 0, 5).Should().StartWith("%PDF");
    }

    [Fact]
    public async Task The_export_takes_a_look_for_this_export_only()
    {
        var id = await SeedNotesAsync();

        var themed = await Api.GetAsync($"/api/documents/{id}/export/pdf?look=cerulean&printSafe=true");
        themed.StatusCode.Should().Be(HttpStatusCode.OK, await themed.Content.ReadAsStringAsync());
        themed.Headers.GetValues("X-Render-Engine").Single().Should().NotBe("typst");

        // The document keeps its own look.
        (await GetDocAsync(id)).GetProperty("look").ValueKind.Should().Be(JsonValueKind.Null);

        var unknown = await Api.GetAsync($"/api/documents/{id}/export/pdf?look=neon");
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var planned = await Api.GetAsync($"/api/documents/{id}/export/pdf?look=gazette");
        planned.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_zip_carries_the_package_and_the_tex_loads_it_before_the_custom_preamble()
    {
        var id = await SeedNotesAsync();
        await using (var db = CreateDbContext())
        {
            var row = await db.Documents.FirstAsync(d => d.Id == id);
            row.CustomPreamble = @"\newcommand{\R}{\mathbb{R}}";
            await db.SaveChangesAsync();
        }
        (await PutLookAsync(id, new { theme = "index" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var zip = await Api.GetByteArrayAsync($"/api/documents/{id}/export/latex");
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        archive.Entries.Select(e => e.FullName).Should().Contain("lilia-theme.sty");

        var tex = await Api.GetStringAsync($"/api/documents/{id}/export/latex?mode=tex");
        var line = tex.IndexOf("{lilia-theme}", StringComparison.Ordinal);
        line.Should().BePositive();
        tex.IndexOf(@"\newcommand{\R}", StringComparison.Ordinal).Should().BeGreaterThan(line);
        tex.Should().Contain(@"\liliaHeadRow \textbf{Model}");
    }
}
