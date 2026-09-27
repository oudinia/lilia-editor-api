using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Integration.Recent;

/// <summary>
/// GET /api/recent — what the user did recently, per tool, for Home and the
/// switcher. Only the user's own acts: an untouched starter clone is not one.
/// </summary>
/// <remarks>Audit rows and tables survive the per-test cleanup, so every test has its own user.</remarks>
[Collection("Integration")]
public class RecentActivityTests : IntegrationTestBase
{
    public RecentActivityTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static string NewUserId() => $"test_recent_{Guid.NewGuid():N}";

    private async Task<(string UserId, HttpClient Client)> NewUserAsync()
    {
        var userId = NewUserId();
        await SeedUserAsync(userId);
        return (userId, CreateClientAs(userId));
    }

    private static async Task<JsonElement> RecentAsync(HttpClient client, string query = "")
    {
        var res = await client.GetAsync($"/api/recent{query}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement? Tool(JsonElement recent, string key) =>
        recent.GetProperty("tools").EnumerateArray()
            .Where(t => t.GetProperty("tool").GetString() == key)
            .Select(t => (JsonElement?)t)
            .FirstOrDefault();

    private static List<string> ToolKeys(JsonElement recent) =>
        recent.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("tool").GetString()!).ToList();

    private static async Task<Guid> CreateDocumentAsync(HttpClient client, string title)
    {
        var res = await client.PostAsJsonAsync("/api/documents", new { title });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTableAsync(HttpClient client, string caption)
    {
        var res = await client.PostAsJsonAsync("/api/tables", new
        {
            caption,
            label = "tab:x",
            content = new { headers = new[] { "A" }, rows = new[] { new[] { "1" } } },
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_fresh_user_has_no_tools()
    {
        var (_, client) = await NewUserAsync();

        var res = await client.GetAsync("/api/recent");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Be("""{"tools":[]}""");
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        using var anon = CreateAnonymousClient();
        (await anon.GetAsync("/api/recent")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_created_document_appears_with_its_create_action()
    {
        var (_, client) = await NewUserAsync();
        var id = await CreateDocumentAsync(client, "My paper");

        var recent = await RecentAsync(client);

        var docs = Tool(recent, "documents");
        docs.Should().NotBeNull();
        var item = docs!.Value.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("id").GetGuid().Should().Be(id);
        item.GetProperty("title").GetString().Should().Be("My paper");
        item.GetProperty("action").GetString().Should().Be("document.create");
        item.TryGetProperty("actionAt", out _).Should().BeTrue();
        item.TryGetProperty("updatedAt", out _).Should().BeTrue();
        item.TryGetProperty("actionFormat", out _).Should().BeFalse("nulls are omitted");
        docs.Value.GetProperty("lastUsedAt").GetDateTime().Should().Be(item.GetProperty("at").GetDateTime());
    }

    [Fact]
    public async Task An_untouched_starter_like_clone_does_not_appear_but_an_edited_or_opened_one_does()
    {
        var (userId, client) = await NewUserAsync();
        var now = DateTime.UtcNow;
        await using (var db = CreateDbContext())
        {
            Document Doc(string title, DateTime updated, DateTime? opened) => new()
            {
                Id = Guid.NewGuid(), OwnerId = userId, Title = title,
                CreatedAt = now.AddHours(-1), UpdatedAt = updated, LastOpenedAt = opened,
            };
            db.Documents.AddRange(
                Doc("Starter CV", now.AddHours(-1), null),
                Doc("Edited", now.AddMinutes(-10), null),
                Doc("Opened", now.AddHours(-1), now.AddMinutes(-5)));
            await db.SaveChangesAsync();
        }

        var recent = await RecentAsync(client);

        var titles = Tool(recent, "documents")!.Value.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("title").GetString()).ToList();
        titles.Should().Equal("Opened", "Edited");
    }

    [Fact]
    public async Task An_import_audited_against_its_job_is_attributed_to_the_document()
    {
        var (userId, client) = await NewUserAsync();
        var doc = await SeedDocumentAsync(userId, "Imported thesis");
        var completed = DateTime.UtcNow.AddMinutes(-2);
        var jobId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            db.Jobs.Add(new Job
            {
                Id = jobId, UserId = userId, DocumentId = doc.Id, JobType = JobTypes.Import,
                Status = JobStatus.Completed, SourceFormat = "DOCX", SourceFileName = "thesis.docx",
                CreatedAt = completed.AddMinutes(-1), CompletedAt = completed,
            });
            db.Jobs.Add(new Job
            {
                Id = Guid.NewGuid(), UserId = userId, JobType = JobTypes.Import,
                Status = JobStatus.Failed, SourceFormat = "pdf", SourceFileName = "broken.pdf",
            });
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(), UserId = userId, Action = "document.import", EntityType = "Job",
                EntityId = jobId.ToString(),
                Details = JsonDocument.Parse("""{"Filename":"thesis.docx","Format":"DOCX"}"""),
                CreatedAt = completed.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
        }

        var recent = await RecentAsync(client);

        var docItem = Tool(recent, "documents")!.Value.GetProperty("items").EnumerateArray().Single();
        docItem.GetProperty("id").GetGuid().Should().Be(doc.Id);
        docItem.GetProperty("action").GetString().Should().Be("document.import");
        docItem.GetProperty("actionFormat").GetString().Should().Be("docx");
        docItem.GetProperty("actionAt").GetDateTime().Should().BeCloseTo(completed, TimeSpan.FromMilliseconds(1));

        var jobItem = Tool(recent, "import")!.Value.GetProperty("items").EnumerateArray().Single();
        jobItem.GetProperty("id").GetGuid().Should().Be(jobId, "a failed job is skipped");
        jobItem.GetProperty("title").GetString().Should().Be("thesis.docx");
        jobItem.GetProperty("action").GetString().Should().Be("document.import");
        jobItem.GetProperty("actionFormat").GetString().Should().Be("docx");
        jobItem.GetProperty("documentId").GetGuid().Should().Be(doc.Id);
    }

    [Fact]
    public async Task A_created_table_appears_with_no_papers_using_it()
    {
        var (_, client) = await NewUserAsync();
        var id = await CreateTableAsync(client, "Results");

        var recent = await RecentAsync(client);

        var item = Tool(recent, "tables")!.Value.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("id").GetGuid().Should().Be(id);
        item.GetProperty("title").GetString().Should().Be("Results");
        item.GetProperty("documentCount").GetInt32().Should().Be(0);
        item.GetProperty("updatedAt").GetDateTime().Should().Be(item.GetProperty("at").GetDateTime());
    }

    [Fact]
    public async Task Another_users_items_never_appear()
    {
        var (_, alice) = await NewUserAsync();
        var (_, bob) = await NewUserAsync();
        await CreateDocumentAsync(alice, "Alice's paper");
        await CreateTableAsync(alice, "Alice's table");

        var recent = await RecentAsync(bob);

        recent.GetProperty("tools").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task PerTool_limits_the_items_newest_first_and_is_clamped()
    {
        var (_, client) = await NewUserAsync();
        foreach (var title in new[] { "One", "Two", "Three", "Four" })
            await CreateDocumentAsync(client, title);

        var two = Tool(await RecentAsync(client, "?perTool=2"), "documents")!.Value.GetProperty("items");
        two.EnumerateArray().Select(i => i.GetProperty("title").GetString()).Should().Equal("Four", "Three");

        Tool(await RecentAsync(client), "documents")!.Value.GetProperty("items").GetArrayLength()
            .Should().Be(3, "the default is 3");
        Tool(await RecentAsync(client, "?perTool=0"), "documents")!.Value.GetProperty("items").GetArrayLength()
            .Should().Be(1, "perTool is clamped to at least 1");
        Tool(await RecentAsync(client, "?perTool=500"), "documents")!.Value.GetProperty("items").GetArrayLength()
            .Should().Be(4, "clamped to 10, and there are only 4");
    }

    [Fact]
    public async Task Tools_are_ordered_by_last_use()
    {
        var (_, client) = await NewUserAsync();
        var tableId = await CreateTableAsync(client, "Earlier table");
        await CreateDocumentAsync(client, "Later paper");

        ToolKeys(await RecentAsync(client)).Should().Equal("documents", "tables");

        (await client.PutAsJsonAsync($"/api/tables/{tableId}", new
        {
            caption = "Edited table",
            label = "tab:x",
            content = new { headers = new[] { "A" }, rows = new[] { new[] { "2" } } },
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var recent = await RecentAsync(client);
        ToolKeys(recent).Should().Equal("tables", "documents");
        var lastUsed = recent.GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("lastUsedAt").GetDateTime()).ToList();
        lastUsed.Should().BeInDescendingOrder();
    }
}
