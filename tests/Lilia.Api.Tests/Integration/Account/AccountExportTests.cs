using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Account;

/// <summary>
/// "Export everything" (Settings → Your data, launch redesign 3f): a background job builds one archive
/// of the author's documents, as Lilia's data and as LaTeX, with their profile; only its owner can
/// download it.
/// </summary>
[Collection("Integration")]
public class AccountExportTests : IntegrationTestBase
{
    public AccountExportTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static async Task<JsonElement> WaitDone(HttpClient client)
    {
        for (var i = 0; i < 120; i++)
        {
            var r = await client.GetAsync("/api/me/export");
            if (r.StatusCode == HttpStatusCode.OK)
            {
                var j = await r.Content.ReadFromJsonAsync<JsonElement>();
                var status = j.GetProperty("status").GetString();
                if (status is "COMPLETED" or "FAILED") return j;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException("the export never finished");
    }

    [Fact]
    public async Task One_archive_of_every_document_and_the_profile_only_for_its_owner()
    {
        var id = $"export-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id, $"{id}@lilia.test", "Ada");
        var doc = await SeedDocumentAsync(id, "Thesis: chapter 1");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Hello world."}""");
        await SeedDocumentAsync(id, "Thesis: chapter 1"); // same title: its own folder
        var client = CreateClientAs(id, $"{id}@lilia.test", "Ada");

        (await client.GetAsync("/api/me/export")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var start = await client.PostAsync("/api/me/export", null);
        start.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var jobId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var done = await WaitDone(client);
        done.GetProperty("status").GetString().Should().Be("COMPLETED");
        done.GetProperty("documents").GetInt32().Should().Be(2);
        done.GetProperty("expiresAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(5));

        var download = await client.GetAsync($"/api/me/export/{jobId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        using var zip = new ZipArchive(await download.Content.ReadAsStreamAsync());
        var names = zip.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain(["account.json", "README.txt"]);
        names.Count(n => n.EndsWith("/document.json")).Should().Be(2, "two documents with one title get two folders");
        names.Should().Contain(n => n.Contains("/latex/") && n.EndsWith(".tex"));
        using (var reader = new StreamReader(zip.GetEntry("account.json")!.Open()))
            (await reader.ReadToEndAsync()).Should().Contain(id);

        var stranger = CreateClientAs($"stranger-{Guid.NewGuid():N}"[..20]);
        (await stranger.GetAsync($"/api/me/export/{jobId}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Asking_again_while_one_runs_returns_the_same_export()
    {
        // A running export, seeded so the test doesn't race the worker (an empty account's export
        // can finish before a second request arrives, and then a new one is right).
        var id = $"export-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id);
        var running = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            db.Jobs.Add(new Lilia.Core.Entities.Job
            {
                Id = running, TenantId = id, UserId = id, JobType = Lilia.Api.Services.AccountExportService.JobType,
                Status = Lilia.Core.Entities.JobStatus.Processing, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var client = CreateClientAs(id);
        var again = await client.PostAsync("/api/me/export", null);
        again.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().Should().Be(running);
    }
}
