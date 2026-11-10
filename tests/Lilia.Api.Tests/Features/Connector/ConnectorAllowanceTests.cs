using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// The connector's daily allowance (the owner: "Free 50/day, Pro unlimited" on 5 Nov 2026, 25 a day from 10 Nov): Free's 26th
/// action of the day is refused as a tool error that names Pro, the help tools stay open, and Pro and
/// staff aren't counted. Applied with the other plan limits, so these callers turn them on.
/// </summary>
public class ConnectorAllowanceTests : IntegrationTestBase, IDisposable
{
    private const string Staff = "allow_staff_account";
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public ConnectorAllowanceTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private HttpClient Caller(string userId)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Entitlements:Enforce", "true");
            b.UseSetting("Entitlements:StaffUserIds:0", Staff);
            b.UseSetting("Mcp:CallsPerMinute", "1000");
        });
        _factories.Add(factory);
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    private static string NewId(string what) => $"allow_{what}_{Guid.NewGuid():N}"[..36];

    /// <summary>One tools/call: whether it was refused as a tool error, and its text.</summary>
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

    private async Task GivePlanAsync(string userId, string slug)
    {
        await using var db = CreateDbContext();
        var plan = await db.Plans.SingleAsync(p => p.Slug == slug);
        db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_plans_carry_the_allowance_free_25_and_the_others_no_limit()
    {
        await using var db = CreateDbContext();
        var caps = await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Slug, p => p.Caps.RootElement.GetProperty("mcpActionsPerDay").GetInt32());
        caps["free"].Should().Be(25);
        caps.Where(c => c.Key != "free").Should().OnlyContain(c => c.Value == -1);
    }

    [Fact]
    public async Task Free_gets_25_actions_a_day_then_a_tool_error_that_names_Pro_while_help_stays_open()
    {
        var id = NewId("free");
        await SeedUserAsync(id);
        var client = Caller(id);

        for (var i = 0; i < 25; i++)
            (await CallAsync(client, "list_documents")).IsError.Should().BeFalse($"action {i + 1} of 25 is within the allowance");

        var refused = await CallAsync(client, "list_documents");
        refused.IsError.Should().BeTrue();
        refused.Text.Should().Contain("25 connector actions a day").And.Contain("Pro has no daily limit").And.Contain("?plans=1");

        (await CallAsync(client, "search_help", new { query = "export" })).IsError.Should().BeFalse("help doesn't count");
    }

    [Fact]
    public async Task Pro_and_staff_are_not_counted()
    {
        var pro = NewId("pro");
        await SeedUserAsync(pro);
        await GivePlanAsync(pro, "pro");
        await SeedUserAsync(Staff);
        foreach (var client in new[] { Caller(pro), Caller(Staff) })
            for (var i = 0; i < 55; i++)
                (await CallAsync(client, "list_documents")).IsError.Should().BeFalse();
    }
}
