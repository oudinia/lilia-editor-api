using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Features.BulkImport.Services;
using Lilia.Api.Tests.Features.BulkImport;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// Bulk PDF import through the connector (the owner, 21 and 22 Nov 2026): start_bulk_import puts PDFs into the same
/// batches as the web's /import/bulk, or gives the drop page's link; bulk_import_status reads them. The queue is
/// recorded, not run: these tests are about the connector's side.
/// </summary>
public class McpBulkImportTests : IntegrationTestBase, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];
    private readonly RecordingQueue _queue = new();

    public McpBulkImportTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private HttpClient App(string userId)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Import:PdfSitePagesPerMonth", "1000000");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IBulkImportQueue>();
                s.AddSingleton<IBulkImportQueue>(_queue);
            });
        });
        _factories.Add(factory);
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        c.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        c.DefaultRequestHeaders.Add("X-Test-Scope", "openid documents:read documents:write");
        return c;
    }

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

    private static string NewId(string what) => $"mcpbulk_{what}_{Guid.NewGuid():N}"[..32];

    [Fact]
    public async Task With_no_files_it_gives_the_drop_page()
    {
        var user = NewId("link");
        await SeedUserAsync(user);
        var r = await CallAsync(App(user), "start_bulk_import", new { });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        r.GetProperty("url").GetString().Should().EndWith("/import/bulk");
    }

    [Fact]
    public async Task PDFs_sent_by_the_app_go_into_one_batch_that_the_status_tool_reads()
    {
        var user = NewId("files");
        await SeedUserAsync(user);
        var app = App(user);
        var r = await CallAsync(app, "start_bulk_import", new
        {
            files = new[]
            {
                new { filename = "notebook-01.pdf", content = Convert.ToBase64String(BulkImportTestKit.Pdf(3, "a")) },
                new { filename = "notebook-02.pdf", content = Convert.ToBase64String(BulkImportTestKit.Pdf(4, "b")) },
                new { filename = "broken.pdf", content = "not base64!" },
            },
        });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        var batchId = r.GetProperty("batchId").GetGuid();
        var files = r.GetProperty("files").EnumerateArray().ToList();
        files.Select(f => f.GetProperty("added").GetBoolean()).Should().Equal(true, true, false);
        files[1].GetProperty("pages").GetInt32().Should().Be(4);
        files[2].GetProperty("error").GetString().Should().Contain("base64");
        r.GetProperty("url").GetString().Should().EndWith($"/import/bulk/{batchId}");

        // More files join the same batch.
        var more = await CallAsync(app, "start_bulk_import", new
        {
            batchId = batchId.ToString(),
            files = new[] { new { filename = "notebook-03.pdf", content = Convert.ToBase64String(BulkImportTestKit.Pdf(2, "c")) } },
        });
        more.GetProperty("batchId").GetGuid().Should().Be(batchId);

        var status = await CallAsync(app, "bulk_import_status", new { batchId = batchId.ToString() });
        status.TryGetProperty("error", out _).Should().BeFalse(status.ToString());
        status.GetProperty("pages").GetInt32().Should().Be(9);
        status.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().BeEquivalentTo("notebook-01.pdf", "notebook-02.pdf", "notebook-03.pdf");

        var latest = await CallAsync(app, "bulk_import_status", new { });
        latest.GetProperty("imports").EnumerateArray().Select(b => b.GetProperty("batchId").GetGuid()).Should().Contain(batchId);
    }

    [Fact]
    public async Task Past_the_plan_s_PDF_pages_the_file_is_refused_and_says_so()
    {
        // A Free account reads 10 PDF pages a month: an 11-page notebook is refused as a plan limit, not an error.
        var user = NewId("limit");
        await SeedUserAsync(user);
        var r = await CallAsync(App(user), "start_bulk_import", new
        {
            files = new[] { new { filename = "big.pdf", content = Convert.ToBase64String(BulkImportTestKit.Pdf(11, "l")) } },
        });
        var file = r.GetProperty("files")[0];
        file.GetProperty("added").GetBoolean().Should().BeFalse();
        file.GetProperty("category").GetString().Should().Be("plan-limit");
    }

    [Fact]
    public async Task Another_author_s_batch_is_neither_read_nor_added_to_and_six_files_are_refused()
    {
        var owner = NewId("owner");
        var other = NewId("other");
        await SeedUserAsync(owner);
        await SeedUserAsync(other);
        var started = await CallAsync(App(owner), "start_bulk_import", new
        {
            files = new[] { new { filename = "mine.pdf", content = Convert.ToBase64String(BulkImportTestKit.Pdf(1, "m")) } },
        });
        var batchId = started.GetProperty("batchId").GetString();
        var intruder = App(other);

        (await CallAsync(intruder, "bulk_import_status", new { batchId })).GetProperty("category").GetString().Should().Be("invalid");
        (await CallAsync(intruder, "start_bulk_import", new
        {
            batchId,
            files = new[] { new { filename = "x.pdf", content = Convert.ToBase64String(BulkImportTestKit.Pdf(1, "x")) } },
        })).GetProperty("category").GetString().Should().Be("invalid");

        var six = Enumerable.Range(1, 6).Select(i => new { filename = $"f{i}.pdf", content = Convert.ToBase64String(BulkImportTestKit.Pdf(1, $"s{i}")) }).ToArray();
        (await CallAsync(intruder, "start_bulk_import", new { files = six })).GetProperty("category").GetString().Should().Be("invalid");
    }
}
