using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Lilia.Api.Tests.Engines.Latex;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// Import and export over the connector without the file in the conversation (the user, 12 Oct:
/// "import and export must not consume too many tokens"): the app gets links; the file goes
/// between the author's browser and Lilia.
/// </summary>
public class McpTransfersTests : IntegrationTestBase
{
    public McpTransfersTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static async Task<JsonElement> CallAsync(HttpClient client, string tool, object args)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var text = await (await client.SendAsync(req)).Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private static string PathOf(string url) => new Uri(url).PathAndQuery;

    [Fact]
    public async Task Export_gives_a_link_and_the_link_gives_the_file_without_signing_in()
    {
        await SeedUserAsync("mcpx_owner");
        var doc = await SeedDocumentAsync("mcpx_owner", "Exported paper");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Exported through a link."}""", 0);

        var r = await CallAsync(CreateClientAs("mcpx_owner"), "export_document", new { documentId = doc.Id.ToString(), format = "tex" });
        var url = r.GetProperty("url").GetString()!;
        url.Should().StartWith("https://editor.liliaeditor.com/api/connector/files/");
        r.GetProperty("lastCheck").GetProperty("note").GetString().Should().Contain("check_document");

        var res = await CreateAnonymousClient().GetAsync(PathOf(url));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentDisposition!.FileNameStar.Should().EndWith(".tex");
        (await res.Content.ReadAsStringAsync()).Should().Contain("Exported through a link.").And.Contain("\\documentclass");
    }

    [Fact]
    public async Task A_made_up_link_and_someone_elses_document_get_nothing()
    {
        var made = await CreateAnonymousClient().GetAsync($"/api/connector/files/{new string('a', 48)}");
        made.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Opened in a browser, often a phone: a sentence, not JSON.
        made.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        await SeedUserAsync("mcpx_alice");
        await SeedUserAsync("mcpx_mallory");
        var doc = await SeedDocumentAsync("mcpx_alice");
        var r = await CallAsync(CreateClientAs("mcpx_mallory"), "export_document", new { documentId = doc.Id.ToString() });
        r.GetProperty("error").GetString().Should().Contain("No document");
    }

    [Fact]
    public async Task Import_through_the_drop_page_creates_the_authors_document_and_the_app_reads_its_id()
    {
        await SeedUserAsync("mcpx_importer");
        await SeedUserAsync("mcpx_other");
        var author = CreateClientAs("mcpx_importer");

        var start = await CallAsync(author, "start_import", new { });
        var importId = start.GetProperty("importId").GetString()!;
        start.GetProperty("url").GetString().Should().Be($"https://editor.liliaeditor.com/connect/import/{importId}");
        (await CallAsync(author, "import_status", new { importId })).GetProperty("status").GetString().Should().Be("waiting");

        // The drop page: only the author it was issued to.
        (await CreateClientAs("mcpx_other").GetAsync($"/api/connector/imports/{importId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await author.GetAsync($"/api/connector/imports/{importId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var tex = "\\documentclass{article}\\title{Dropped paper}\\begin{document}\\maketitle\\section{Intro}Hello from a dropped file.\\end{document}";
        var post = await author.PostAsJsonAsync($"/api/connector/imports/{importId}", new { content = tex, format = "latex", filename = "dropped.tex" });
        post.StatusCode.Should().Be(HttpStatusCode.OK, await post.Content.ReadAsStringAsync());

        var status = await CallAsync(author, "import_status", new { importId });
        status.GetProperty("status").GetString().Should().Be("done");
        var id = status.GetProperty("documentId").GetGuid();
        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == id)).OwnerId.Should().Be("mcpx_importer");

        // A link is used once.
        (await author.PostAsJsonAsync($"/api/connector/imports/{importId}", new { content = tex, format = "latex", filename = "again.tex" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // MCP-114 (run 3b): an app holding the file imports it in one call — .tex and .lml as text,
    // .docx as base64 — through the same importer; a numbered Word heading style carries over (MCP-31).
    [Fact]
    public async Task An_app_imports_a_file_it_has()
    {
        await SeedUserAsync("mcpx_appimport");
        var app = CreateClientAs("mcpx_appimport");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        // An import creates a document: the author allowed edits on the consent page (16 Oct).
        app.DefaultRequestHeaders.Add("X-Test-Scope", "openid email documents:read documents:write");

        var tex = await CallAsync(app, "import_document", new
        {
            filename = "paper.tex",
            content = "\\documentclass{article}\\title{Imported by an app}\\begin{document}\\maketitle\\section{Intro}Hello.\\end{document}",
        });
        tex.TryGetProperty("error", out _).Should().BeFalse(tex.ToString());
        tex.GetProperty("url").GetString().Should().EndWith($"/d/{tex.GetProperty("documentId").GetGuid()}");

        var lml = await CallAsync(app, "import_document", new { filename = "notes.lml", content = "@heading[level=1]\n  Notes\n@paragraph\n  From LML." });
        lml.TryGetProperty("error", out _).Should().BeFalse(lml.ToString());

        var path = Lilia.Api.Tests.Engines.Latex.HeadingNumberingTests.Docx(true, (1, "Introduction"), (2, "Scope"), (1, "Method"));
        var docx = await CallAsync(app, "import_document", new { filename = "thesis.docx", content = Convert.ToBase64String(await File.ReadAllBytesAsync(path)) });
        docx.TryGetProperty("error", out _).Should().BeFalse(docx.ToString());
        await using var db = CreateDbContext();
        var imported = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == docx.GetProperty("documentId").GetGuid());
        imported.OwnerId.Should().Be("mcpx_appimport");
        imported.HeadingNumbering.Should().Be(2, "the Word file numbered its headings to level 2");

        (await CallAsync(app, "import_document", new { filename = "x.pdf", content = "abc" })).GetProperty("error").GetString().Should().Contain(".tex, .zip (an Overleaf project, base64), .docx, .md and .lml");
        (await CallAsync(app, "import_document", new { filename = "x.docx", content = "not base64!" })).GetProperty("error").GetString().Should().Contain("base64");
    }

    // MCP-123: a .tex that asks to run commands is imported (the author fixes it in the editor), and the
    // reply says what the compile will refuse; a clean file has no warning.
    // The launch bot's #226 retest (19 Nov 2026): the reply and the documents list named the document with its LaTeX.
    [Fact]
    public async Task An_imported_papers_name_is_its_titles_plain_text()
    {
        await SeedUserAsync("mcpx_plainname");
        var app = CreateClientAs("mcpx_plainname");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        app.DefaultRequestHeaders.Add("X-Test-Scope", "openid email documents:read documents:write");

        var tex = await CallAsync(app, "import_document", new
        {
            filename = "conf.tex",
            content = "\\documentclass{article}\\title{Conference Paper Title*\\\\ {\\footnotesize \\textsuperscript{*}Note: Sub-titles} \\thanks{Funding.}}\\begin{document}\\maketitle\\section{Intro}Hello.\\end{document}",
        });
        tex.TryGetProperty("error", out _).Should().BeFalse(tex.ToString());
        tex.GetProperty("title").GetString().Should().Be("Conference Paper Title* *Note: Sub-titles");
    }

    [Fact]
    public async Task An_import_that_runs_commands_comes_with_a_warning()
    {
        await SeedUserAsync("mcpx_appunsafe");
        var app = CreateClientAs("mcpx_appunsafe");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        app.DefaultRequestHeaders.Add("X-Test-Scope", "openid email documents:read documents:write");

        var unsafeTex = await CallAsync(app, "import_document", new
        {
            filename = "shell.tex",
            content = "\\documentclass{article}\n\\immediate\\write18{echo preamble}\n\\begin{document}\nText.\n\\immediate\\write18{echo body}\n\\end{document}\n",
        });
        unsafeTex.TryGetProperty("error", out _).Should().BeFalse(unsafeTex.ToString());
        unsafeTex.GetProperty("warning").GetString().Should().Contain("write18").And.Contain("will not compile");

        var clean = await CallAsync(app, "import_document", new
        {
            filename = "clean.tex",
            content = "\\documentclass{article}\\begin{document}Hello.\\end{document}",
        });
        clean.TryGetProperty("error", out _).Should().BeFalse(clean.ToString());
        // The connector leaves null fields out: a clean file has no warning at all.
        (clean.TryGetProperty("warning", out var w) && w.ValueKind != System.Text.Json.JsonValueKind.Null).Should().BeFalse();
    }

    // MCP-111: a figure whose image can't be found prints a "[Missing figure: …]" frame, and the check
    // said the document printed correctly. It asks storage, as the export does; a found image is fine.
    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_check_reports_a_figure_whose_image_is_missing()
    {
        await SeedUserAsync("mcpx_missingfig");
        var app = CreateClientAs("mcpx_missingfig");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        app.DefaultRequestHeaders.Add("X-Test-Scope", "openid email documents:read documents:write");
        var storage = Fixture.Factory.Services.GetRequiredService<Lilia.Core.Interfaces.IStorageService>();
        var present = $"check-tests/{Guid.NewGuid():N}.png";
        await storage.UploadAsync(present, new MemoryStream(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")), "image/png");

        var author = CreateClientAs("mcpx_missingfig"); // the author's own client writes; the app reads
        var doc = (await (await author.PostAsJsonAsync("/api/documents", new { title = "Figures" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var gone = await (await author.PostAsJsonAsync($"/api/documents/{doc}/blocks", new { type = "figure", content = new { src = "figures/gone.png", caption = "Gone" }, sortOrder = 0 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        (await author.PostAsJsonAsync($"/api/documents/{doc}/blocks", new { type = "figure", content = new { src = present, caption = "Here" }, sortOrder = 1 })).EnsureSuccessStatusCode();

        var check = await CallAsync(app, "check_document", new { documentId = doc });
        check.GetProperty("printsCorrectly").GetBoolean().Should().BeFalse(check.ToString());
        var missing = check.GetProperty("missingFigures").EnumerateArray().ToList();
        missing.Should().ContainSingle();
        missing[0].GetProperty("blockId").GetGuid().Should().Be(gone.GetProperty("id").GetGuid());
        check.GetProperty("note").GetString().Should().Contain("Missing figure");
    }

    // The connector test (17 Nov 2026, item F): an @footnote{…} written in a paragraph printed as typed, and the check said
    // the document printed correctly. Through Lilia's own emitter, so the mark is what the PDF really gets.
    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_check_names_an_LML_mark_that_a_paragraph_prints_as_typed()
    {
        await SeedUserAsync("mcpx_literalmark");
        var app = CreateClientAs("mcpx_literalmark");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        app.DefaultRequestHeaders.Add("X-Test-Scope", "openid email documents:read documents:write");
        var doc = await SeedDocumentAsync("mcpx_literalmark", "Marks");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Cited @cite{knuth} properly."}""", 0);
        var bad = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"The result holds.@footnote{Measured in 2025.}"}""", 1);

        var check = await CallAsync(app, "check_document", new { documentId = doc.Id.ToString() });
        check.GetProperty("compiles").GetBoolean().Should().BeTrue(check.ToString());
        check.GetProperty("printsCorrectly").GetBoolean().Should().BeFalse(check.ToString());
        var problems = check.GetProperty("problems").EnumerateArray()
            .Where(p => p.GetProperty("message").GetString()!.Contains("@footnote")).ToList();
        problems.Should().ContainSingle(check.ToString());
        problems[0].GetProperty("blockId").GetString().Should().Be(bad.Id.ToString());
        problems[0].GetProperty("message").GetString().Should().Contain(@"\footnote{");
        check.GetProperty("problems").EnumerateArray().Should().NotContain(p => p.GetProperty("message").GetString()!.Contains("is not a Lilia mark"),
            "@cite{key} is Lilia's own mark, turned into \\cite");
        check.GetProperty("pages").GetInt32().Should().Be(1);
    }

    // MCP-124 (retested 14 Nov): a Word file on US Letter with 1 in / 1.25 in margins and a Title-style
    // paragraph came in titled by its file name, the title as body text, on A4 with no margins.
    [Fact]
    public async Task A_word_import_keeps_its_title_paper_and_margins()
    {
        await SeedUserAsync("mcpx_pagesetup");
        var app = CreateClientAs("mcpx_pagesetup");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        app.DefaultRequestHeaders.Add("X-Test-Scope", "openid email documents:read documents:write");

        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        static string P(string text, string? style = null) =>
            $"<w:p>{(style is null ? "" : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>")}<w:r><w:t>{text}</w:t></w:r></w:p>";
        var parts = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>""",
            ["_rels/.rels"] = """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""",
            ["word/_rels/document.xml.rels"] = """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""",
            ["word/styles.xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><w:styles xmlns:w="{w}"><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/></w:style><w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style></w:styles>""",
            ["word/document.xml"] = $"""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="{w}"><w:body>{P("Sediment Transport in Flumes", "Title")}{P("Introduction", "Heading1")}{P("This study measures sediment transport in a laboratory flume.")}<w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr></w:body></w:document>""",
        };
        using var zip = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var (name, xml) in parts)
            {
                await using var entry = archive.CreateEntry(name).Open();
                await entry.WriteAsync(Encoding.UTF8.GetBytes(xml));
            }

        var result = await CallAsync(app, "import_document", new { filename = "paper-setup.docx", content = Convert.ToBase64String(zip.ToArray()) });
        result.TryGetProperty("error", out _).Should().BeFalse(result.ToString());
        var id = result.GetProperty("documentId").GetGuid();
        await using var db = CreateDbContext();
        var doc = await db.Documents.AsNoTracking().Include(d => d.Blocks).FirstAsync(d => d.Id == id);
        doc.Title.Should().Be("Sediment Transport in Flumes");
        doc.PaperSize.Should().Be("letter");
        (doc.MarginTop, doc.MarginBottom, doc.MarginLeft, doc.MarginRight).Should().Be(("1in", "1in", "1.25in", "1.25in"));
        // The title is front matter (a Title block, without a date since the file has none: 18 Nov 2026), not body text.
        doc.Blocks.Should().NotContain(b => b.Type != "title" && b.Content.RootElement.GetRawText().Contains("Sediment Transport"), "the title is the document's, not body text");
        doc.Blocks.Single(b => b.Type == "title").Content.RootElement.GetProperty("dateMode").GetString().Should().Be("none");
        doc.Blocks.Should().NotContain(b => b.Type == "tableOfContents");
    }

    [Fact]
    public async Task An_apps_token_cannot_upload_on_the_drop_page()
    {
        await SeedUserAsync("mcpx_app");
        var start = await CallAsync(CreateClientAs("mcpx_app"), "start_import", new { });
        var app = CreateClientAs("mcpx_app");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        var res = await app.PostAsJsonAsync($"/api/connector/imports/{start.GetProperty("importId").GetString()}",
            new { content = "\\documentclass{article}\\begin{document}x\\end{document}", format = "latex", filename = "x.tex" });
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the file comes from the author's browser, not the app");
    }

    // The launch bot's Overleaf round trip (18 Nov 2026): an Overleaf project as it downloads, a zip with \input files,
    // a .bib and an image, through the connector. import_document took one .tex only.
    [Fact]
    public async Task An_overleaf_project_zip_imports_through_the_connector()
    {
        await SeedUserAsync("mcpx_zip");
        var app = CreateClientAs("mcpx_zip");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        app.DefaultRequestHeaders.Add("X-Test-Scope", "openid email documents:read documents:write");

        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var zip = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            async Task Add(string name, byte[] bytes) { await using var e = archive.CreateEntry(name).Open(); await e.WriteAsync(bytes); }
            await Add("main.tex", Encoding.UTF8.GetBytes("\\documentclass{article}\n\\usepackage{graphicx}\n\\graphicspath{{figures/}}\n\\title{Flume Study}\n\\begin{document}\n\\maketitle\n\\input{sections/intro}\n\\begin{figure}\\centering\\includegraphics{plot.png}\\caption{A plot.}\\end{figure}\n\\bibliographystyle{plain}\n\\bibliography{refs}\n\\end{document}\n"));
            await Add("sections/intro.tex", Encoding.UTF8.GetBytes("\\section{Introduction}\nSediment moves, as shown before~\\cite{smith2020}.\n"));
            await Add("refs.bib", Encoding.UTF8.GetBytes("@article{smith2020, author={J. Smith}, title={Flumes}, journal={Water}, year={2020}}\n"));
            await Add("figures/plot.png", png);
        }

        var result = await CallAsync(app, "import_document", new { filename = "flume-project.zip", content = Convert.ToBase64String(zip.ToArray()) });
        result.TryGetProperty("error", out _).Should().BeFalse(result.ToString());
        var id = result.GetProperty("documentId").GetGuid();
        await using var db = CreateDbContext();
        var doc = await db.Documents.AsNoTracking().Include(d => d.Blocks).Include(d => d.BibliographyEntries).FirstAsync(d => d.Id == id);
        doc.Title.Should().Be("Flume Study");
        doc.Blocks.Should().Contain(b => b.Content.RootElement.GetRawText().Contains("Sediment moves"), "the \\input file is read into the document");
        doc.BibliographyEntries.Select(e => e.CiteKey).Should().Contain("smith2020", "the zip's .bib is the document's references");
        (await db.Assets.AsNoTracking().Where(a => a.DocumentId == id).Select(a => a.FileName).ToListAsync()).Should().Contain(n => n.EndsWith("plot.png"));
        var figure = doc.Blocks.Single(b => b.Type == "figure");
        figure.Content.RootElement.GetProperty("src").GetString().Should().NotBe("plot.png", "the figure points at the stored image");
    }

    // #241/#242: a project with several main files and .bib files sharing keys answered an error yet made a document
    // (twice on a retry), and nothing said which .tex was used or which files the PDF can't use.
    [Fact]
    public async Task A_project_with_several_mains_imports_once_and_says_what_it_used()
    {
        await SeedUserAsync("mcpx_zip2");
        var app = CreateClientAs("mcpx_zip2");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        app.DefaultRequestHeaders.Add("X-Test-Scope", "openid email documents:read documents:write");
        using var zip = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            async Task Add(string name, string text) { await using var e = archive.CreateEntry(name).Open(); await e.WriteAsync(Encoding.UTF8.GetBytes(text)); }
            await Add("sorsamp.tex", "\\documentclass{article}\n\\begin{document}\nSOR sample \\cite{epr}.\n\\bibliography{sorsamp}\n\\end{document}\n");
            await Add("apssamp.tex", "\\documentclass{article}\n\\begin{document}\nAPS sample \\cite{epr}.\n\\bibliography{apssamp}\n\\end{document}\n");
            await Add("sorsamp.bib", "@article{epr, author={A. Einstein}, title={EPR}, journal={Phys. Rev.}, year={1935}}\n");
            await Add("apssamp.bib", "@article{epr, author={A. Einstein}, title={EPR}, journal={Phys. Rev.}, year={1935}}\n");
            await Add("revtex4-2.cls", "% a class\n");
            await Add("fig1.eps", "%!PS-Adobe-3.0 EPSF-3.0\n");
        }

        var before = await CreateDbContext().Documents.CountAsync(d => d.OwnerId == "mcpx_zip2");
        var result = await CallAsync(app, "import_document", new { filename = "RevTeX_Sample.zip", content = Convert.ToBase64String(zip.ToArray()) });
        result.TryGetProperty("error", out _).Should().BeFalse(result.ToString());
        (await CreateDbContext().Documents.CountAsync(d => d.OwnerId == "mcpx_zip2")).Should().Be(before + 1);
        var project = result.GetProperty("project");
        project.GetProperty("mainFile").GetString().Should().Be("apssamp.tex", "same length, so the name's order decides");
        project.GetProperty("otherMainFiles").EnumerateArray().Select(e => e.GetString()).Should().Equal("sorsamp.tex");
        project.GetProperty("notUsedByThePdf").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo("revtex4-2.cls", "fig1.eps");
        var id = result.GetProperty("documentId").GetGuid();
        (await CreateDbContext().BibliographyEntries.CountAsync(b => b.DocumentId == id && b.CiteKey == "epr")).Should().Be(1);
    }
}
