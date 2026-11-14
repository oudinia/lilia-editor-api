using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// Operations bought in packs (the owner, 14 Nov 2026): $5 for 300, 375 each from 4 packs, 450 each from 10, never
/// expiring. Past Free's 25 connector actions a day, a check, an export or an import spends one (given back if the
/// call fails); reads and edits run free while there is a balance.
/// </summary>
public class OperationPacksTests : IntegrationTestBase, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];
    public OperationPacksTests(TestDatabaseFixture fixture) : base(fixture) { }
    public void Dispose() { foreach (var f in _factories) f.Dispose(); }

    private static string NewId(string what) => $"packs_{what}_{Guid.NewGuid():N}"[..36];
    private OperationPacks Packs(Lilia.Infrastructure.Data.LiliaDbContext db) => new(db, NullLogger<OperationPacks>.Instance);

    [Theory]
    [InlineData(1, 300)]
    [InlineData(3, 900)]
    [InlineData(4, 1500)]
    [InlineData(9, 3375)]
    [InlineData(10, 4500)]
    [InlineData(20, 9000)]
    public void The_bonus_tiers(int packs, int operations) => OperationPacks.OperationsFor(packs).Should().Be(operations);

    [Fact]
    public async Task A_checkout_session_credits_once()
    {
        var id = NewId("grant");
        await SeedUserAsync(id);
        await using var db = CreateDbContext();
        (await Packs(db).GrantPurchaseAsync(id, 4, "cs_test_" + id)).Should().BeTrue();
        await using var db2 = CreateDbContext();
        (await Packs(db2).GrantPurchaseAsync(id, 4, "cs_test_" + id)).Should().BeFalse("the webhook came twice");
        (await Packs(db2).BalanceAsync(id)).Should().Be(1500);
    }

    [Fact]
    public async Task Spends_stop_at_zero_and_a_refund_is_given_once()
    {
        var id = NewId("spend");
        await SeedUserAsync(id);
        await using var db = CreateDbContext();
        db.OperationCredits.Add(new OperationCredit { Id = Guid.NewGuid(), UserId = id, Delta = 2, Reason = "adjustment" });
        await db.SaveChangesAsync();
        var packs = Packs(db);
        var first = await packs.TrySpendAsync(id, "check_document");
        (await packs.TrySpendAsync(id, "export_document")).Should().NotBeNull();
        (await packs.TrySpendAsync(id, "import_document")).Should().BeNull("the balance is empty");
        await packs.RefundAsync(first!.Value);
        await packs.RefundAsync(first.Value);
        (await packs.BalanceAsync(id)).Should().Be(1, "one refund, not two");
    }

    [Fact]
    public async Task Calls_at_the_same_moment_never_spend_below_zero()
    {
        var id = NewId("race");
        await SeedUserAsync(id);
        await using (var db = CreateDbContext())
        {
            db.OperationCredits.Add(new OperationCredit { Id = Guid.NewGuid(), UserId = id, Delta = 3, Reason = "adjustment" });
            await db.SaveChangesAsync();
        }
        var spends = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = CreateDbContext();
            return await Packs(db).TrySpendAsync(id, "check_document");
        }));
        spends.Count(s => s is not null).Should().Be(3);
        await using var check = CreateDbContext();
        (await Packs(check).BalanceAsync(id)).Should().Be(0);
    }

    // Through the connector.

    private HttpClient Caller(string userId)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Entitlements:Enforce", "true");
            b.UseSetting("Mcp:CallsPerMinute", "1000");
        });
        _factories.Add(factory);
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    private static async Task<(bool IsError, string Text)> CallAsync(HttpClient client, string tool, object? args = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args ?? new { } } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var text = await res.Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var result = JsonDocument.Parse(json).RootElement.GetProperty("result");
        var isError = result.TryGetProperty("isError", out var e) && e.GetBoolean();
        return (isError, result.GetProperty("content")[0].GetProperty("text").GetString()!);
    }

    private async Task UseTheAllowanceAsync(string userId)
    {
        var cache = _factories[^1].Services.GetRequiredService<IDistributedCache>();
        await cache.SetStringAsync($"mcp-actions:{userId}:{DateTime.UtcNow:yyyy-MM-dd}", "25");
    }

    private async Task<int> BalanceAsync(string userId)
    {
        await using var db = CreateDbContext();
        return await Packs(db).BalanceAsync(userId);
    }

    [Fact]
    public async Task Past_the_allowance_a_check_spends_one_and_a_failed_one_is_given_back()
    {
        var id = NewId("check");
        await SeedUserAsync(id);
        await using (var db = CreateDbContext())
            (await Packs(db).GrantPurchaseAsync(id, 1, "cs_test_" + id)).Should().BeTrue();
        var client = Caller(id);
        await UseTheAllowanceAsync(id);

        // A check of a document that isn't there fails: its operation comes back.
        var missing = await CallAsync(client, "check_document", new { documentId = Guid.NewGuid().ToString() });
        missing.Text.Should().NotContain("connector actions a day");
        (await BalanceAsync(id)).Should().Be(300);

        // A real one spends.
        await using (var db = CreateDbContext())
        {
            db.Documents.Add(new Document { Id = Guid.NewGuid(), OwnerId = id, Title = "Packs", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        Guid docId;
        await using (var db = CreateDbContext()) docId = await db.Documents.Where(d => d.OwnerId == id).Select(d => d.Id).SingleAsync();
        var check = await CallAsync(client, "check_document", new { documentId = docId.ToString() });
        check.Text.Should().NotContain("connector actions a day");
        (await BalanceAsync(id)).Should().Be(299);
    }

    [Fact]
    public async Task Past_the_allowance_reads_run_free_with_a_balance_and_are_refused_without()
    {
        var withPack = NewId("reads");
        await SeedUserAsync(withPack);
        await using (var db = CreateDbContext())
            await Packs(db).GrantPurchaseAsync(withPack, 1, "cs_test_" + withPack);
        var client = Caller(withPack);
        await UseTheAllowanceAsync(withPack);
        (await CallAsync(client, "list_documents")).IsError.Should().BeFalse();
        (await BalanceAsync(withPack)).Should().Be(300, "reads don't spend");

        var without = NewId("noreads");
        await SeedUserAsync(without);
        var other = Caller(without);
        await UseTheAllowanceAsync(without);
        var refused = await CallAsync(other, "list_documents");
        refused.IsError.Should().BeTrue();
        refused.Text.Should().Contain("25 connector actions a day");
        (await CallAsync(other, "check_document", new { documentId = Guid.NewGuid().ToString() })).IsError.Should().BeTrue();
    }
}
