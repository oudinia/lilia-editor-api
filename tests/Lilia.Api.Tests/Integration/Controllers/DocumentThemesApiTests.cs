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

        foreach (var built in new[] { "carnet", "gazette" })
        {
            var t = themes.EnumerateArray().Single(x => x.GetProperty("id").GetString() == built);
            t.GetProperty("status").GetString().Should().Be("ready");
            t.GetProperty("available").GetBoolean().Should().BeTrue("this machine has ebgaramond, josefin and montserrat");
        }
        var exposition = themes.EnumerateArray().Single(x => x.GetProperty("id").GetString() == "exposition");
        exposition.GetProperty("available").GetBoolean().Should().BeTrue("this machine has beamer, montserrat, josefin and tikz");
        exposition.GetProperty("status").GetString().Should().Be("ready");
        exposition.GetProperty("classes").EnumerateArray().Select(c => c.GetString()).Should().Equal("beamer");
        index.GetProperty("classes").ValueKind.Should().Be(JsonValueKind.Null, "classes is written as null for a document theme");
    }

    [Fact]
    public async Task The_package_can_be_downloaded()
    {
        var sty = await Api.GetStringAsync("/api/themes/lilia-theme.sty");
        sty.Should().Contain(@"\ProvidesPackage{lilia-theme}");
        var expo = await Api.GetStringAsync("/api/themes/beamerthemeLiliaExposition.sty");
        expo.Should().Contain(@"\ProvidesPackage{beamerthemeLiliaExposition}");
    }

    [Fact]
    public async Task A_new_document_reads_back_look_null_and_unlocked()
    {
        var doc = await GetDocAsync(await SeedNotesAsync());
        doc.GetProperty("look").ValueKind.Should().Be(JsonValueKind.Null);
        doc.GetProperty("lookLocked").ValueKind.Should().Be(JsonValueKind.Null);
        doc.GetProperty("lookThemes").EnumerateArray().Select(t => t.GetString())
            .Should().Equal("classic", "cerulean", "index", "carnet", "gazette");
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
    [InlineData("""{"theme":"exposition"}""", "Exposition is a beamer theme: switch the class to beamer to use it.")]
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

        var doc = await GetDocAsync(id);
        doc.GetProperty("lookLocked").GetString().Should().Be("IEEEtran sets its own look, so themes are off for this document.");
        doc.GetProperty("lookThemes").EnumerateArray().Select(t => t.GetString()).Should().Equal("classic");
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
        var beamerOnly = await Api.GetAsync($"/api/documents/{id}/export/pdf?look=exposition");
        beamerOnly.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await beamerOnly.Content.ReadAsStringAsync()).Should().Contain("Exposition is a beamer theme");
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
        tex.Should().Contain(@"\liliaHeadRow \liliaTableHead{\textbf{Model}}");
    }

    // ── phase 3: Exposition, the beamer theme ────────────────────────────

    private async Task<Guid> SeedDeckAsync()
    {
        var doc = await SeedDocumentAsync(_userId, "Neutrino oscillations");
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Mixing","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "slide", """{"title":"The mixing matrix","content":"Quartz zebra slide."}""", 1);
        await SeedBlockAsync(doc.Id, "slide", """{"title":"Accuracy","content":"Muted owl closes the deck."}""", 2);
        await using var db = CreateDbContext();
        (await db.Documents.FirstAsync(d => d.Id == doc.Id)).LatexDocumentClass = "beamer";
        await db.SaveChangesAsync();
        return doc.Id;
    }

    [Fact]
    public async Task A_beamer_deck_takes_classic_or_exposition()
    {
        var id = await SeedDeckAsync();

        var doc = await GetDocAsync(id);
        doc.GetProperty("lookLocked").ValueKind.Should().Be(JsonValueKind.Null, "the beamer lock is lifted");
        doc.GetProperty("lookThemes").EnumerateArray().Select(t => t.GetString()).Should().Equal("classic", "exposition");

        var refused = await PutLookAsync(id, new { theme = "cerulean" });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Beamer decks take Classic or Exposition.");

        (await PutLookAsync(id, new { theme = "exposition" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetDocAsync(id)).GetProperty("look").GetProperty("theme").GetString().Should().Be("exposition");

        // The .zip carries the beamer theme (and not lilia-theme.sty); the .tex loads it with one line.
        var zip = await Api.GetByteArrayAsync($"/api/documents/{id}/export/latex");
        using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip)))
        {
            var names = archive.Entries.Select(e => e.FullName).ToList();
            names.Should().Contain("beamerthemeLiliaExposition.sty").And.NotContain("lilia-theme.sty");
        }
        var tex = await Api.GetStringAsync($"/api/documents/{id}/export/latex?mode=tex");
        tex.Should().Contain(@"\usetheme{LiliaExposition}").And.Contain(@"\begin{frame}{The mixing matrix}");

        // A Classic deck's .zip carries no theme file.
        (await PutLookAsync(id, new { theme = "classic" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var plainZip = await Api.GetByteArrayAsync($"/api/documents/{id}/export/latex");
        using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(plainZip)))
            archive.Entries.Select(e => e.FullName).Should().NotContain(n => n.EndsWith(".sty"));
    }

    [Fact]
    public async Task A_class_switch_keeps_exposition_and_prints_classic_until_switched_back()
    {
        var id = await SeedDeckAsync();
        (await PutLookAsync(id, new { theme = "exposition", paper = "white" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // To article: never refused; the look is kept, the LaTeX is Classic.
        (await Api.PutAsJsonAsync($"/api/documents/{id}", new { documentClass = "article" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await GetDocAsync(id);
        doc.GetProperty("look").GetProperty("theme").GetString().Should().Be("exposition");
        doc.GetProperty("lookThemes").EnumerateArray().Select(t => t.GetString()).Should().NotContain("exposition");
        var tex = await Api.GetStringAsync($"/api/documents/{id}/export/latex?mode=tex");
        tex.Should().NotContain("LiliaExposition").And.NotContain("{lilia-theme}");

        // Back to beamer: Exposition again, print-safe as stored.
        (await Api.PutAsJsonAsync($"/api/documents/{id}", new { documentClass = "beamer" })).StatusCode.Should().Be(HttpStatusCode.OK);
        tex = await Api.GetStringAsync($"/api/documents/{id}/export/latex?mode=tex");
        tex.Should().Contain(@"\usetheme[printsafe]{LiliaExposition}");
    }

    [Fact]
    public async Task The_preview_and_the_export_compile_an_exposition_deck()
    {
        var id = await SeedDeckAsync();
        (await PutLookAsync(id, new { theme = "exposition" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var preview = await Api.PostAsync($"/api/latex/{id}/pdf", null);
        preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        Encoding.Latin1.GetString(await preview.Content.ReadAsByteArrayAsync(), 0, 5).Should().StartWith("%PDF");

        var export = await Api.GetAsync($"/api/documents/{id}/export/pdf?printSafe=true");
        export.StatusCode.Should().Be(HttpStatusCode.OK, await export.Content.ReadAsStringAsync());
        export.Headers.GetValues("X-Render-Engine").Single().Should().NotBe("typst");

        var wrong = await Api.GetAsync($"/api/documents/{id}/export/pdf?look=index");
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Beamer decks take Classic or Exposition.");
    }

    // ── phase 2: table settings, the lists ───────────────────────────────

    [Fact]
    public async Task Put_stores_table_settings_and_refuses_bad_ones_with_the_valid_values()
    {
        var id = await SeedNotesAsync();

        (await PutLookAsync(id, new { theme = "carnet", tables = new { style = "banded", caption = "below" } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var tables = (await GetDocAsync(id)).GetProperty("look").GetProperty("tables");
        tables.GetProperty("style").GetString().Should().Be("banded");
        tables.GetProperty("caption").GetString().Should().Be("below");
        tables.TryGetProperty("density", out _).Should().BeFalse("only the values set are stored");

        var bad = await PutLookAsync(id, new { theme = "carnet", tables = new { style = "zebra" } });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("Valid values: ruled, banded, header");
        var unknown = await PutLookAsync(id, new { theme = "carnet", tables = new { width = "full" } });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("Valid keys: style, density, caption");

        // Classic with a table setting is kept (it loads the package's table part).
        (await PutLookAsync(id, new { theme = "classic", tables = new { style = "header" } })).StatusCode.Should().Be(HttpStatusCode.OK);
        var look = (await GetDocAsync(id)).GetProperty("look");
        look.GetProperty("theme").GetString().Should().Be("classic");
        look.GetProperty("tables").GetProperty("style").GetString().Should().Be("header");
        var tex = await Api.GetStringAsync($"/api/documents/{id}/export/latex?mode=tex");
        tex.Should().Contain(@"\usepackage[theme=classic, tables=header]{lilia-theme}");

        // Under a publisher class tables stay ruled.
        var locked = await SeedNotesAsync("IEEEtran");
        var refused = await PutLookAsync(locked, new { theme = "classic", tables = new { style = "banded" } });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Its tables stay ruled");
    }

    [Fact]
    public async Task The_document_list_carries_each_documents_look()
    {
        var themed = await SeedNotesAsync();
        var classic = await SeedNotesAsync();
        (await PutLookAsync(themed, new { theme = "gazette" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var list = JsonDocument.Parse(await Api.GetStringAsync("/api/documents?page=1&pageSize=100")).RootElement;
        var items = list.GetProperty("items").EnumerateArray().ToList();
        items.Single(d => d.GetProperty("id").GetGuid() == themed).GetProperty("look").GetProperty("theme").GetString().Should().Be("gazette");
        items.Single(d => d.GetProperty("id").GetGuid() == classic).GetProperty("look").ValueKind
            .Should().Be(JsonValueKind.Null, "null is Classic, and it is written");
    }

    [Fact]
    public async Task Used_in_says_which_section_each_paper_holds_the_table_in()
    {
        var table = await Api.PostAsJsonAsync("/api/tables", new
        {
            caption = "Top-1", label = "tab:top1",
            content = new { headers = new[] { "Model", "Top-1" }, rows = new[] { new[] { "ResNet", "76.1" } } },
        });
        table.StatusCode.Should().Be(HttpStatusCode.Created);
        var tableId = (await table.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        const string tableJson = """{"caption":"Top-1","headers":["Model","Top-1"],"rows":[["ResNet","76.1"]]}""";

        // An Index report: the table in chapter 2, pinned to the eighth colour.
        var notes = await SeedDocumentAsync(_userId, "Lecture notes");
        await SeedBlockAsync(notes.Id, "heading", """{"text":"Vectors","level":1}""", 0);
        var matrices = await SeedBlockAsync(notes.Id, "heading", """{"text":"Matrices","level":1}""", 1);
        await SeedBlockAsync(notes.Id, "heading", """{"text":"Products","level":2}""", 2);
        var inNotes = await SeedBlockAsync(notes.Id, "table", tableJson, 3);

        // A Cerulean article: the table under an unnumbered heading.
        var essay = await SeedDocumentAsync(_userId, "Essay");
        await SeedBlockAsync(essay.Id, "heading", """{"text":"Preface","level":1,"numbered":false}""", 0);
        var inEssay = await SeedBlockAsync(essay.Id, "table", tableJson, 1);

        // A Classic article: the table in the appendix, after the title heading the export drops.
        var paper = await SeedDocumentAsync(_userId, "A paper");
        await SeedBlockAsync(paper.Id, "heading", """{"text":"A paper","level":1}""", 0);
        await SeedBlockAsync(paper.Id, "heading", """{"text":"Method","level":1}""", 1);
        await SeedBlockAsync(paper.Id, "embed", """{"code":"\\appendix"}""", 2);
        await SeedBlockAsync(paper.Id, "heading", """{"text":"Proofs","level":1}""", 3);
        var inPaper = await SeedBlockAsync(paper.Id, "table", tableJson, 4);

        await using (var db = CreateDbContext())
        {
            var row = await db.Documents.FirstAsync(d => d.Id == notes.Id);
            row.LatexDocumentClass = "report";
            row.Look = $$$"""{"theme":"index","paper":"theme","pins":{"{{{matrices.Id}}}":7}}""";
            (await db.Documents.FirstAsync(d => d.Id == essay.Id)).Look = """{"theme":"cerulean","paper":"theme","pins":{}}""";
            await db.SaveChangesAsync();
        }
        foreach (var (doc, block) in new[] { (notes.Id, inNotes.Id), (essay.Id, inEssay.Id), (paper.Id, inPaper.Id) })
            (await Api.PostAsync($"/api/tables/{tableId}/documents/{doc}?blockId={block}", null))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // A paper this caller cannot read: counted, never described.
        var stranger = $"themes-other-{Guid.NewGuid():N}"[..28];
        await SeedUserAsync(stranger);
        var hidden = await SeedDocumentAsync(stranger, "Someone else's");
        await using (var db = CreateDbContext())
        {
            db.DocumentTables.Add(new Lilia.Core.Entities.DocumentTable { Id = Guid.NewGuid(), DocumentId = hidden.Id, TableId = tableId });
            await db.SaveChangesAsync();
        }

        var usage = JsonDocument.Parse(await Api.GetStringAsync($"/api/tables/{tableId}/documents")).RootElement;
        usage.GetProperty("total").GetInt32().Should().Be(4);
        var visible = usage.GetProperty("visible").EnumerateArray().ToDictionary(v => v.GetProperty("documentId").GetGuid());
        visible.Should().HaveCount(3).And.NotContainKey(hidden.Id);

        var n = visible[notes.Id];
        n.GetProperty("look").GetProperty("theme").GetString().Should().Be("index");
        n.GetProperty("sectionNumber").GetString().Should().Be("2", "the table sits in chapter 2");
        n.GetProperty("sectionColour").GetString().Should().Be("#2E6E9E", "chapter 2 is pinned to the eighth colour");

        var e = visible[essay.Id];
        e.GetProperty("look").GetProperty("theme").GetString().Should().Be("cerulean");
        e.GetProperty("sectionNumber").ValueKind.Should().Be(JsonValueKind.Null, "under an unnumbered heading");
        e.GetProperty("sectionColour").ValueKind.Should().Be(JsonValueKind.Null);

        var p = visible[paper.Id];
        p.GetProperty("look").ValueKind.Should().Be(JsonValueKind.Null, "Classic");
        p.GetProperty("sectionNumber").GetString().Should().Be("A", "appendix sections are lettered");
        p.GetProperty("sectionColour").ValueKind.Should().Be(JsonValueKind.Null, "only Index has section colours");
    }
}
