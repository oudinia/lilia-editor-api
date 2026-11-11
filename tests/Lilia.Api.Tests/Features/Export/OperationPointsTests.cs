using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// Operations weighted by complexity (the owner, 11 Nov 2026: "an import of a document of many pages does not count the
/// same as asking for a title… pondering the tasks by complexity, kind of like agility"). The catalogue gives each kind
/// its points; every connector action and every export is recorded with the points it was given.
/// </summary>
public class OperationPointsTests : IntegrationTestBase
{
    public OperationPointsTests(TestDatabaseFixture fixture) : base(fixture) { OperationLog.Forget(); }

    private HttpClient App(string userId)
    {
        var c = CreateClientAs(userId);
        c.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        c.DefaultRequestHeaders.Add("X-Test-Scope", "openid documents:read documents:write");
        return c;
    }

    private static async Task CallAsync(HttpClient client, string tool, object args)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        (await client.SendAsync(req)).EnsureSuccessStatusCode();
    }

    private async Task<List<(string Kind, int Points)>> OperationsOfAsync(string userId)
    {
        await using var db = CreateDbContext();
        return (await db.Operations.AsNoTracking().Where(o => o.UserId == userId).OrderBy(o => o.CreatedAt).ToListAsync())
            .Select(o => (o.Kind, o.Points)).ToList();
    }

    [Fact]
    public async Task The_catalogue_weighs_a_title_change_far_below_an_import_of_many_pages()
    {
        await using var db = CreateDbContext();
        var kinds = await db.OperationKinds.AsNoTracking().ToDictionaryAsync(k => k.Code);
        kinds["help"].Points.Should().Be(0);
        kinds["edit.small"].PointsFor(null).Should().Be(1);
        kinds["export.pdf"].PointsFor(null).Should().Be(5);
        kinds["export.accessible_pdf"].PointsFor(null).Should().Be(13);
        kinds["import"].PointsFor(3).Should().Be(6, "5, and 1 for the first 10 pages");
        kinds["import"].PointsFor(120).Should().Be(17, "5, and 1 per 10 pages: 12");
    }

    [Fact]
    public async Task Each_connector_action_is_recorded_with_its_kind_s_points()
    {
        await SeedUserAsync("opspts_app");
        var doc = await SeedDocumentAsync("opspts_app", "Weighted");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"A sentence."}""");
        var app = App("opspts_app");

        await CallAsync(app, "search_help", new { query = "export" });
        await CallAsync(app, "get_outline", new { documentId = doc.Id.ToString() });
        await CallAsync(app, "set_title", new { documentId = doc.Id.ToString(), title = "Weighted, renamed" });
        await CallAsync(app, "create_document", new { title = "Drafted", lml = "@paragraph\n  Body.\n" });

        (await OperationsOfAsync("opspts_app")).Should().Equal(("help", 0), ("read", 1), ("edit.small", 1), ("document.write", 3));
    }

    [Fact]
    public async Task A_refused_action_is_not_an_operation()
    {
        await SeedUserAsync("opspts_refused");
        await CallAsync(App("opspts_refused"), "get_outline", new { documentId = Guid.NewGuid().ToString() });
        (await OperationsOfAsync("opspts_refused")).Should().BeEmpty();
    }

    [Fact]
    public async Task An_accessible_pdf_counts_as_one_not_a_plain_pdf()
    {
        OperationLog.ExportCode("pdf", accessible: true).Should().Be("export.accessible_pdf");
        OperationLog.ExportCode("pdf").Should().Be("export.pdf");
        OperationLog.ExportCode("docx").Should().Be("export.file");
    }
}
