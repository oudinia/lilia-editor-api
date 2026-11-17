using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// MCP step 3 (the user, 12 Oct: "go ahead with step 3"): tools that change a document. Ask Lilia's
/// own writes, for an owner or editor only; an app needs the documents:write scope (edits allowed
/// on the consent page); a version is saved before an app's first change.
/// </summary>
public class McpWriteToolsTests : IntegrationTestBase
{
    public McpWriteToolsTests(TestDatabaseFixture fixture) : base(fixture) { }

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
        var res = await client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private HttpClient App(string userId, string? scope)
    {
        var c = CreateClientAs(userId);
        c.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        if (scope is not null) c.DefaultRequestHeaders.Add("X-Test-Scope", scope);
        return c;
    }

    private async Task<int> BlockCountAsync(Guid docId)
    {
        await using var db = CreateDbContext();
        return await db.Blocks.CountAsync(b => b.DocumentId == docId);
    }

    [Fact]
    public async Task An_app_allowed_to_edit_adds_a_block_and_an_undo_point_is_saved_first()
    {
        await SeedUserAsync("mcpw_owner");
        var doc = await SeedDocumentAsync("mcpw_owner", "Edited by Claude");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"First."}""", 0);
        var app = App("mcpw_owner", "openid documents:read documents:write");

        var r = await CallAsync(app, "add_block", new { documentId = doc.Id.ToString(), type = "paragraph", content = """{"text":"Added by an app."}""" });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        r.GetProperty("changedBlocks").GetArrayLength().Should().Be(1);
        var undo = r.GetProperty("undo").GetProperty("versionId").GetGuid();
        (await BlockCountAsync(doc.Id)).Should().Be(2);

        // A second change in the same burst reuses the same undo point.
        var r2 = await CallAsync(app, "set_title", new { documentId = doc.Id.ToString(), title = "Retitled" });
        r2.GetProperty("undo").GetProperty("versionId").GetGuid().Should().Be(undo);

        await using var db = CreateDbContext();
        (await db.DocumentVersions.CountAsync(v => v.DocumentId == doc.Id)).Should().Be(1);
    }

    // The connector test (17 Nov 2026): set_title replaced the whole title block, so a call with the title alone erased
    // the author and date, and every call dropped the keywords (which IEEE and ACM ask for, and which it couldn't set).
    [Fact]
    public async Task Set_title_changes_only_what_it_is_given_and_sets_keywords()
    {
        await SeedUserAsync("mcpw_title");
        var doc = await SeedDocumentAsync("mcpw_title", "A paper");
        await SeedBlockAsync(doc.Id, "title", """{"title":"A paper","author":"Ada Lovelace","date":"1843","keywords":["engines"],"affiliation":"Analytical Society"}""", 0);
        var app = App("mcpw_title", "openid documents:read documents:write");

        var r = await CallAsync(app, "set_title", new { documentId = doc.Id.ToString(), title = "A better paper" });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        var r2 = await CallAsync(app, "set_title", new { documentId = doc.Id.ToString(), title = "A better paper", keywords = "union-find, amortised analysis" });
        r2.TryGetProperty("error", out _).Should().BeFalse(r2.ToString());

        await using var db = CreateDbContext();
        var title = (await db.Blocks.SingleAsync(b => b.DocumentId == doc.Id && b.Type == "title")).Content.RootElement;
        title.GetProperty("title").GetString().Should().Be("A better paper");
        title.GetProperty("author").GetString().Should().Be("Ada Lovelace");
        title.GetProperty("date").GetString().Should().Be("1843");
        title.GetProperty("affiliation").GetString().Should().Be("Analytical Society");
        title.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()).Should().Equal("union-find", "amortised analysis");
    }

    // #129 (Grok, 17 Nov 2026): ACM asked for each author's affiliation, country and email, the rights lines and the CCS
    // concepts, and set_title had no way to give them. The title editor's own fields, through the connector.
    [Fact]
    public async Task Set_title_takes_authors_with_affiliations_and_ACMs_rights_and_CCS()
    {
        await SeedUserAsync("mcpw_acm");
        var doc = await SeedDocumentAsync("mcpw_acm", "A paper");
        var app = App("mcpw_acm", "openid documents:read documents:write");

        var r = await CallAsync(app, "set_title", new
        {
            documentId = doc.Id.ToString(),
            title = "A paper",
            authors = new object[]
            {
                new { name = "Grok Tester", affiliation = "Lilia Editor", city = "Paris", country = "France", email = "grok@example.org" },
                new { name = "Ada Lovelace", country = "UK" },
            },
            acmRights = "\\setcopyright{acmlicensed}\n\\acmDOI{10.1145/1234567.8901234}",
            acmCcs = "\\ccsdesc[500]{Theory of computation~Data structures design and analysis}",
        });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());

        await using var db = CreateDbContext();
        var title = (await db.Blocks.SingleAsync(b => b.DocumentId == doc.Id && b.Type == "title")).Content.RootElement;
        title.GetProperty("author").GetString().Should().Be(@"Grok Tester\\Lilia Editor\\Paris\\France\\grok@example.org \and Ada Lovelace\\UK");
        title.GetProperty("acmRights").GetString().Should().Contain(@"\setcopyright{acmlicensed}");
        title.GetProperty("acmCcs").GetString().Should().StartWith(@"\ccsdesc[500]");
    }

    // get_document's LML is what apply_lml takes: a rewrite can start from the document as it is.
    [Fact]
    public async Task The_LML_get_document_gives_goes_back_through_apply_lml_unchanged()
    {
        await SeedUserAsync("mcpw_roundtrip");
        var doc = await SeedDocumentAsync("mcpw_roundtrip", "Round trip");
        var app = App("mcpw_roundtrip", "openid documents:read documents:write");
        var made = await CallAsync(app, "apply_lml", new { documentId = doc.Id.ToString(), lml = """
            @heading[level=1, label=sec:a]
              Introduction
            @paragraph
              See Equation~(eq:e) and **bold** text.
            @equation[mode=display, label=eq:e]
              E = mc^2
            @list[kind=bullet]
              - one
              - two
            """ });
        made.TryGetProperty("error", out _).Should().BeFalse(made.ToString());

        var before = await CallAsync(app, "get_document", new { documentId = doc.Id.ToString() });
        var again = await CallAsync(app, "apply_lml", new { documentId = doc.Id.ToString(), lml = before.GetProperty("lml").GetString() });
        again.TryGetProperty("error", out _).Should().BeFalse(again.ToString());
        var after = await CallAsync(app, "get_document", new { documentId = doc.Id.ToString() });

        static IEnumerable<string> Shape(JsonElement d) => d.GetProperty("blocks").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() != "title")
            .Select(b => b.GetProperty("type").GetString() + " " + b.GetProperty("content").GetRawText());
        Shape(after).Should().Equal(Shape(before));
    }

    // #124 (Grok, 17 Nov 2026): a title with keywords, an align numbered by its rows' own labels, then a paragraph. Through
    // get_document's LML and apply_lml, the paragraph went into the equation (which stopped the PDF), the keywords and the
    // numbering were lost, and the rewrite's undo point was the burst's first version.
    [Fact]
    public async Task An_align_a_paragraph_and_title_keywords_survive_get_document_and_apply_lml()
    {
        await SeedUserAsync("mcpw_124");
        var doc = await SeedDocumentAsync("mcpw_124", "LTX-154");
        await SeedBlockAsync(doc.Id, "title", """{"title":"LTX-154","author":"Grok Tester","date":"17 November 2026","keywords":["union-find","amortised analysis"]}""", 0);
        await SeedBlockAsync(doc.Id, "equation", """{"latex":"\\begin{align}\nx &= 1 \\\\\n  y &= 2 \\label{eq:second}\n\\end{align}","equationMode":"display","label":"","numbered":true}""", 1);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Equation \\eqref{eq:second} is the second row."}""", 2);
        var app = App("mcpw_124", "openid documents:read documents:write");

        // An earlier edit in the same burst, so the burst already has an undo point.
        (await CallAsync(app, "set_title", new { documentId = doc.Id.ToString(), title = "LTX-154" })).TryGetProperty("error", out _).Should().BeFalse();
        var before = await CallAsync(app, "get_document", new { documentId = doc.Id.ToString() });
        var rewrite = await CallAsync(app, "apply_lml", new { documentId = doc.Id.ToString(), lml = before.GetProperty("lml").GetString() });
        rewrite.TryGetProperty("error", out _).Should().BeFalse(rewrite.ToString());
        var after = await CallAsync(app, "get_document", new { documentId = doc.Id.ToString() });

        var blocks = after.GetProperty("blocks").EnumerateArray().ToList();
        blocks.Select(b => b.GetProperty("type").GetString()).Should().Equal("title", "equation", "paragraph");
        blocks[0].GetProperty("content").GetProperty("keywords").EnumerateArray().Select(k => k.GetString()).Should().Equal("union-find", "amortised analysis");
        var eq = blocks[1].GetProperty("content");
        eq.GetProperty("latex").GetString().Should().Contain(@"y &= 2 \label{eq:second}").And.NotContain("second row");
        eq.GetProperty("numbered").GetBoolean().Should().BeTrue();
        blocks[2].GetProperty("content").GetProperty("text").GetString().Should().Contain("is the second row");

        // The rewrite's undo point is a version saved just before it, not the burst's first one.
        var undo = rewrite.GetProperty("undo").GetProperty("versionId").GetGuid();
        await using var db = CreateDbContext();
        var versions = await db.DocumentVersions.Where(v => v.DocumentId == doc.Id).OrderBy(v => v.CreatedAt).ToListAsync();
        versions.Should().HaveCountGreaterThanOrEqualTo(2);
        versions.Last().Id.Should().Be(undo);
    }

    // #124 retest (Grok, 17 Nov 2026, 15:40): an align pasted as LaTeX has no numbered field at all and no block label.
    // The PDF numbers it, but the LML was written without numbered= and read back unnumbered: \eqref printed ??. And the
    // title's empty date came back as \today.
    [Fact]
    public async Task An_equation_with_no_numbered_field_stays_numbered_and_an_empty_date_stays_empty_through_apply_lml()
    {
        await SeedUserAsync("mcpw_124b");
        var doc = await SeedDocumentAsync("mcpw_124b", "Retest");
        await SeedBlockAsync(doc.Id, "title", """{"title":"Retest","author":"Grok Tester","date":"","keywords":["union-find"]}""", 0);
        await SeedBlockAsync(doc.Id, "equation", """{"latex":"\\begin{align}\nx &= 1 \\\\\n  y &= 2 \\label{eq:second}\n\\end{align}","equationMode":"display"}""", 1);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Equation \\eqref{eq:second} is the second row."}""", 2);
        var app = App("mcpw_124b", "openid documents:read documents:write");

        var before = await CallAsync(app, "get_document", new { documentId = doc.Id.ToString() });
        before.GetProperty("lml").GetString().Should().Contain("numbered=true");
        var rewrite = await CallAsync(app, "apply_lml", new { documentId = doc.Id.ToString(), lml = before.GetProperty("lml").GetString() });
        rewrite.TryGetProperty("error", out _).Should().BeFalse(rewrite.ToString());
        var after = await CallAsync(app, "get_document", new { documentId = doc.Id.ToString() });

        var blocks = after.GetProperty("blocks").EnumerateArray().ToList();
        blocks[1].GetProperty("content").GetProperty("numbered").GetBoolean().Should().BeTrue();
        blocks[0].GetProperty("content").GetProperty("date").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task An_app_without_the_write_scope_may_read_but_not_edit_and_is_told_how_to_allow_it()
    {
        // Gate A, 16 Oct: the shipped settings require documents:write (it was open from 12 Oct).
        await SeedUserAsync("mcpw_no_scope");
        var doc = await SeedDocumentAsync("mcpw_no_scope");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 0);
        var r = await CallAsync(App("mcpw_no_scope", "openid email profile documents:read"), "add_block",
            new { documentId = doc.Id.ToString(), type = "paragraph", content = """{"text":"From an app."}""" });
        r.GetProperty("error").GetString().Should().Contain("connect again");
        (await BlockCountAsync(doc.Id)).Should().Be(1, "nothing was written");

        var allowed = await CallAsync(App("mcpw_no_scope", "openid email documents:read documents:write"), "add_block",
            new { documentId = doc.Id.ToString(), type = "paragraph", content = """{"text":"From an app."}""" });
        allowed.TryGetProperty("error", out _).Should().BeFalse(allowed.ToString());
        (await BlockCountAsync(doc.Id)).Should().Be(2);
    }

    [Fact]
    public void The_shipped_settings_require_the_write_scope_for_an_apps_edits()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Lilia.Api", "appsettings.json"))) dir = dir.Parent;
        dir.Should().NotBeNull("the test runs inside the repository");
        var config = Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonFile(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder(),
            Path.Combine(dir!.FullName, "src", "Lilia.Api", "appsettings.json")).Build();
        Lilia.Api.Mcp.LiliaMcpWriteTools.RequireWriteScope(config).Should().BeTrue();
    }

    [Fact]
    public void With_the_scope_required_an_app_connected_for_reading_only_may_not_write()
    {
        System.Security.Claims.ClaimsPrincipal App(string scope) => new(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("sub", "u"),
            new System.Security.Claims.Claim("client_id", "connected-app-test-claude"),
            new System.Security.Claims.Claim("scope", scope),
        }, "Test"));
        Lilia.Api.Mcp.LiliaMcpWriteTools.AppMayWrite(App("openid email"), requireScope: true).Should().BeFalse();
        Lilia.Api.Mcp.LiliaMcpWriteTools.AppMayWrite(App("openid documents:write"), requireScope: true).Should().BeTrue();
        Lilia.Api.Mcp.LiliaMcpWriteTools.AppMayWrite(App("openid email"), requireScope: false).Should().BeTrue();
    }

    [Fact]
    public async Task A_viewer_cannot_edit_even_with_the_scope()
    {
        await SeedUserAsync("mcpw_doc_owner");
        await SeedUserAsync("mcpw_viewer");
        var doc = await SeedDocumentAsync("mcpw_doc_owner");
        var block = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Owner's text."}""", 0);
        await using (var db = CreateDbContext())
        {
            var viewer = await db.Roles.FirstOrDefaultAsync(r => r.Name == "viewer")
                ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "viewer" }).Entity;
            db.DocumentCollaborators.Add(new DocumentCollaborator
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = "mcpw_viewer", RoleId = viewer.Id,
                InvitedBy = "mcpw_doc_owner", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var r = await CallAsync(App("mcpw_viewer", "documents:write"), "edit_block",
            new { documentId = doc.Id.ToString(), blockId = block.Id.ToString(), content = """{"text":"Changed."}""" });
        r.GetProperty("error").GetString().Should().Contain("view-only");
    }

    [Fact]
    public async Task Settings_change_through_one_json_object()
    {
        await SeedUserAsync("mcpw_settings");
        var doc = await SeedDocumentAsync("mcpw_settings");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 0);
        var r = await CallAsync(CreateClientAs("mcpw_settings"), "set_document_settings",
            new { documentId = doc.Id.ToString(), settings = new { fontSize = 12, marginLeft = "2cm" } });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        await using var db = CreateDbContext();
        var saved = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id);
        saved.FontSize.Should().Be(12);
        saved.MarginLeft.Should().Be("2cm");
    }

    // MCP-31: heading numbering is a document setting the author (or the app) chooses.
    [Fact]
    public async Task Heading_numbering_is_a_setting()
    {
        await SeedUserAsync("mcpw_numbering");
        var doc = await SeedDocumentAsync("mcpw_numbering");
        var client = CreateClientAs("mcpw_numbering");
        var r = await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { headingNumbering = "2" } });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        (await CallAsync(client, "get_document_settings", new { documentId = doc.Id.ToString() })).GetProperty("headingNumbering").GetString().Should().Be("2");
        await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { headingNumbering = "default" } });
        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id)).HeadingNumbering.Should().BeNull();
    }

    // From the MCP re-test (13 Oct): {"headingNumbering": 1} failed ("could not be converted to
    // System.String"), the object sent as a JSON string changed nothing, and a misspelt field was ignored.
    [Fact]
    public async Task Settings_take_numbers_for_choices_a_json_string_and_name_unknown_fields()
    {
        await SeedUserAsync("mcpw_shapes");
        var doc = await SeedDocumentAsync("mcpw_shapes");
        var client = CreateClientAs("mcpw_shapes");
        var r = await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { headingNumbering = 1 } });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        r = await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = """{"pageNumbering":"roman","FontSize":12}""" });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        await using (var db = CreateDbContext())
        {
            var saved = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id);
            saved.HeadingNumbering.Should().Be(1);
            saved.PageNumbering.Should().Be("roman");
            saved.FontSize.Should().Be(12);
        }
        r = await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { fontSize = 11, pageNumber = "none" } });
        r.GetProperty("error").GetString().Should().Contain("'pageNumber'").And.Contain("pageNumbering");
        await using (var db = CreateDbContext())
            (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id)).FontSize.Should().Be(12, "nothing changes when a field is unknown");
    }

    // MCP-86: null on a text setting clears it, as get_document_settings reports unset.
    [Fact]
    public async Task Null_clears_a_text_setting()
    {
        await SeedUserAsync("mcpw_null");
        var doc = await SeedDocumentAsync("mcpw_null");
        var client = CreateClientAs("mcpw_null");
        await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { marginLeft = "2cm" } });
        var r = await CallAsync(client, "set_document_settings",
            new { documentId = doc.Id.ToString(), settings = JsonDocument.Parse("""{"marginLeft":null}""").RootElement });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id)).MarginLeft.Should().BeNull();
    }

    // MCP-82 / MCP-83: a block that prints nothing makes printsCorrectly false, and export_document
    // reports the check that was just run instead of "never checked".
    [Fact]
    public async Task The_check_counts_unprinted_blocks_and_export_remembers_it()
    {
        await SeedUserAsync("mcpw_check");
        var doc = await SeedDocumentAsync("mcpw_check");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 0);
        await SeedBlockAsync(doc.Id, "cover", """{"text":"A cover."}""", 1);
        var client = CreateClientAs("mcpw_check");
        var check = await CallAsync(client, "check_document", new { documentId = doc.Id.ToString() });
        check.GetProperty("compiles").GetBoolean().Should().BeTrue(check.ToString());
        check.GetProperty("printsCorrectly").GetBoolean().Should().BeFalse();
        check.GetProperty("unprinted").ToString().Should().Contain("cover");
        var export = await CallAsync(client, "export_document", new { documentId = doc.Id.ToString(), format = "tex" });
        export.GetProperty("lastCheck").GetProperty("source").GetString().Should().Be("check_document");
        export.GetProperty("lastCheck").GetProperty("printsCorrectly").GetBoolean().Should().BeFalse();
    }

    // MCP-88 (run 3): an app calling check_document and export_document together got "Never
    // checked" from the export; it now waits for the check that is still compiling.
    [Fact]
    public async Task An_export_during_a_check_reports_that_check()
    {
        await SeedUserAsync("mcpw_together");
        var doc = await SeedDocumentAsync("mcpw_together");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 0);
        var client = CreateClientAs("mcpw_together");
        var check = CallAsync(client, "check_document", new { documentId = doc.Id.ToString() });
        await Task.Delay(400);
        var export = await CallAsync(CreateClientAs("mcpw_together"), "export_document", new { documentId = doc.Id.ToString(), format = "tex" });
        (await check).GetProperty("compiles").GetBoolean().Should().BeTrue();
        export.GetProperty("lastCheck").GetProperty("source").GetString().Should().Be("check_document", export.ToString());
    }

    // MCP-72: no pdf link for a version the check just found broken; the LaTeX still exports.
    [Fact]
    public async Task No_pdf_link_for_a_document_that_does_not_compile()
    {
        await SeedUserAsync("mcpw_broken");
        var doc = await SeedDocumentAsync("mcpw_broken");
        await SeedBlockAsync(doc.Id, "embed", """{"latex":"\\notACommandAnywhere"}""", 0);
        var client = CreateClientAs("mcpw_broken");
        var check = await CallAsync(client, "check_document", new { documentId = doc.Id.ToString() });
        check.GetProperty("compiles").GetBoolean().Should().BeFalse(check.ToString());
        var pdf = await CallAsync(client, "export_document", new { documentId = doc.Id.ToString(), format = "pdf" });
        pdf.GetProperty("category").GetString().Should().Be("does-not-compile", pdf.ToString());
        pdf.TryGetProperty("url", out var u).Should().BeTrue();
        u.GetString().Should().NotContain("/connector/files/");
        var tex = await CallAsync(client, "export_document", new { documentId = doc.Id.ToString(), format = "tex" });
        tex.GetProperty("url").GetString().Should().Contain("/connector/files/");
    }

    // Run 3b: MCP-108 (the note said "It compiles, but…" of a broken document), MCP-110 (a pdf link
    // made before the check served a PDF of a broken version), MCP-113 (null ignored on a choice).
    [Fact]
    public async Task A_broken_version_gets_no_pdf_even_from_an_earlier_link()
    {
        await SeedUserAsync("mcpw_early");
        var doc = await SeedDocumentAsync("mcpw_early");
        await SeedBlockAsync(doc.Id, "embed", """{"latex":"\\notACommandEarly"}""", 0);
        var client = CreateClientAs("mcpw_early");
        var early = await CallAsync(client, "export_document", new { documentId = doc.Id.ToString(), format = "pdf" });
        var url = early.GetProperty("url").GetString()!;
        var download = await CreateAnonymousClient().GetAsync(new Uri(url).PathAndQuery);
        download.StatusCode.Should().Be(System.Net.HttpStatusCode.UnprocessableEntity, "the version doesn't compile");
        (await download.Content.ReadAsStringAsync()).Should().Contain("does not compile");

        var check = await CallAsync(client, "check_document", new { documentId = doc.Id.ToString() });
        check.GetProperty("note").GetString().Should().StartWith("It does not compile");
    }

    [Fact]
    public async Task Null_puts_a_choice_back_to_its_default()
    {
        await SeedUserAsync("mcpw_nullchoice");
        var doc = await SeedDocumentAsync("mcpw_nullchoice");
        var client = CreateClientAs("mcpw_nullchoice");
        await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { pageNumbering = "none" } });
        var r = await CallAsync(client, "set_document_settings",
            new { documentId = doc.Id.ToString(), settings = JsonDocument.Parse("""{"pageNumbering":null}""").RootElement });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id)).PageNumbering.Should().Be("arabic");
    }

    // MCP-42 (run 3b): a verse block lost its lines on write; a cover block was accepted and printed nothing.
    [Fact]
    public async Task A_verse_keeps_its_lines_and_a_cover_is_refused()
    {
        await SeedUserAsync("mcpw_verse");
        var doc = await SeedDocumentAsync("mcpw_verse");
        var client = CreateClientAs("mcpw_verse");
        var r = await CallAsync(client, "add_block", new { documentId = doc.Id.ToString(), type = "verse", content = """{"text":"Line one\nLine two"}""" });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        await using (var db = CreateDbContext())
        {
            var block = await db.Blocks.AsNoTracking().FirstAsync(b => b.DocumentId == doc.Id && b.Type == "verse");
            block.Content.RootElement.GetProperty("text").GetString().Should().Be("Line one\nLine two");
        }
        (await CallAsync(client, "add_block", new { documentId = doc.Id.ToString(), type = "cover", content = """{"title":"X"}""" }))
            .GetProperty("error").GetString().Should().Contain("ePub");
    }

    // MCP-85 / MCP-87: get_lilia_latex says the engine the check uses; a list's outline is its text.
    [Fact]
    public async Task The_engine_and_the_outline_are_told_plainly()
    {
        await SeedUserAsync("mcpw_engine");
        var doc = await SeedDocumentAsync("mcpw_engine");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"数据 means data."}""", 0);
        await SeedBlockAsync(doc.Id, "list", """{"items":["first item","second item"],"ordered":false}""", 1);
        var client = CreateClientAs("mcpw_engine");
        (await CallAsync(client, "get_lilia_latex", new { documentId = doc.Id.ToString() }))
            .GetProperty("engine").GetString().Should().Be("xelatex");
        var outline = await CallAsync(client, "get_outline", new { documentId = doc.Id.ToString() });
        var snippet = outline.GetProperty("blocks").EnumerateArray().First(b => b.GetProperty("type").GetString() == "list").GetProperty("snippet").GetString();
        snippet.Should().Contain("first item").And.NotStartWith("{");
    }

    [Fact]
    public async Task An_app_creates_a_document_with_its_body()
    {
        await SeedUserAsync("mcpw_creator");
        var r = await CallAsync(CreateClientAs("mcpw_creator"), "create_document",
            new { title = "Written by an app", lml = "@heading[level=1]\n  Introduction\n@paragraph\n  A first paragraph." });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        var id = r.GetProperty("id").GetGuid();
        r.GetProperty("url").GetString().Should().EndWith($"/d/{id}");
        await using var db = CreateDbContext();
        var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == id);
        doc.OwnerId.Should().Be("mcpw_creator");
        (await db.Blocks.CountAsync(b => b.DocumentId == id && b.Type == "paragraph")).Should().Be(1);
    }
}
