using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Shared;

/// <summary>
/// Over the plan's limit after a downgrade (launch redesign 4d/4e, decided 14 Oct 2026): the most recently edited
/// documents up to the plan's number stay editable, the others are read-only for everyone and never deleted; the
/// owner may choose which stay editable, once a day. An account that has always been on Free is left as it is.
/// </summary>
public class OverLimitReadOnlyTests : IntegrationTestBase
{
    private readonly WebApplicationFactory<Program> _enforced;

    public OverLimitReadOnlyTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _enforced = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.PostConfigure<EntitlementOptions>(o => { o.Enforce = true; o.StaffUserIds = []; })));
    }

    public override async Task DisposeAsync()
    {
        await _enforced.DisposeAsync();
        await base.DisposeAsync();
    }

    private HttpClient As(string userId)
    {
        var c = _enforced.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    /// <summary>An author with five documents, edited one after the other (the first oldest); downgraded if asked.</summary>
    private async Task<(string User, List<Guid> Docs)> AuthorWith5(bool downgraded)
    {
        var id = $"over-{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(id);
        var docs = new List<Guid>();
        for (var i = 0; i < 5; i++) docs.Add((await SeedDocumentAsync(id, $"Paper {i + 1}")).Id);
        await using var db = CreateDbContext();
        for (var i = 0; i < 5; i++)
            await db.Documents.Where(d => d.Id == docs[i]).ExecuteUpdateAsync(u => u.SetProperty(d => d.UpdatedAt, DateTime.UtcNow.AddDays(i - 10)));
        if (downgraded)
        {
            var pro = await db.Plans.FirstAsync(p => p.Slug == "pro");
            db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = id, PlanId = pro.Id, Status = "cancelled", StartedAt = DateTime.UtcNow.AddMonths(-2) });
            await db.SaveChangesAsync();
        }
        return (id, docs);
    }

    private static Task<HttpResponseMessage> AddBlock(HttpClient c, Guid doc) =>
        c.PostAsJsonAsync($"/api/documents/{doc}/blocks", new { type = "paragraph", content = new { text = "More." }, sortOrder = 9 });

    private static async Task<bool> Locked(HttpClient c, Guid doc) =>
        (await c.GetFromJsonAsync<JsonElement>($"/api/documents/{doc}")).GetProperty("planLocked").GetBoolean();

    [Fact]
    public async Task After_a_downgrade_the_three_most_recent_stay_editable_and_the_others_are_read_only()
    {
        var (user, docs) = await AuthorWith5(downgraded: true);
        var c = As(user);

        var plan = await c.GetFromJsonAsync<JsonElement>("/api/plan");
        plan.GetProperty("readOnly").GetProperty("locked").GetInt32().Should().Be(2);
        plan.GetProperty("readOnly").GetProperty("editable").GetInt32().Should().Be(3);

        (await Locked(c, docs[0])).Should().BeTrue("the oldest two are read-only");
        (await Locked(c, docs[1])).Should().BeTrue();
        (await Locked(c, docs[4])).Should().BeFalse();
        (await AddBlock(c, docs[0])).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AddBlock(c, docs[4])).StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await c.GetFromJsonAsync<JsonElement>("/api/documents?pageSize=50");
        list.GetProperty("items").EnumerateArray().Count(d => d.GetProperty("planLocked").GetBoolean()).Should().Be(2);
        (await c.DeleteAsync($"/api/documents/{docs[0]}")).IsSuccessStatusCode.Should().BeTrue("moving to the Trash still works");
    }

    [Fact]
    public async Task An_account_always_on_Free_is_left_as_it_is()
    {
        var (user, docs) = await AuthorWith5(downgraded: false);
        var c = As(user);

        (await c.GetFromJsonAsync<JsonElement>("/api/plan")).TryGetProperty("readOnly", out _)
            .Should().Be(false, "nothing is read-only: the plan says nothing about it");
        (await Locked(c, docs[0])).Should().BeFalse();
        (await AddBlock(c, docs[0])).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_owner_chooses_exactly_three_once_a_day()
    {
        var (user, docs) = await AuthorWith5(downgraded: true);
        var c = As(user);

        (await c.PutAsJsonAsync("/api/plan/editable", new { documentIds = new[] { docs[0], docs[1] } })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "exactly as many as the plan holds");

        var choose = await c.PutAsJsonAsync("/api/plan/editable", new { documentIds = new[] { docs[0], docs[1], docs[2] } });
        choose.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Locked(c, docs[0])).Should().BeFalse("chosen");
        (await Locked(c, docs[4])).Should().BeTrue("the newest, not chosen, is now the read-only one");
        (await AddBlock(c, docs[0])).StatusCode.Should().Be(HttpStatusCode.Created);

        var again = await c.PutAsJsonAsync("/api/plan/editable", new { documentIds = new[] { docs[2], docs[3], docs[4] } });
        again.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("PLAN_CHOOSE_ONCE_A_DAY");
        (await c.GetFromJsonAsync<JsonElement>("/api/plan")).GetProperty("readOnly").GetProperty("chooseAgainAt").ValueKind
            .Should().Be(JsonValueKind.String);
    }

    [Fact]
    public void Ask_Lilia_and_the_connector_never_write_to_a_read_only_document()
    {
        var doc = ((Lilia.Core.DTOs.DocumentDto)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Lilia.Core.DTOs.DocumentDto)))
            with { Role = "owner" };
        AskLiliaService.MayEditWithAi(true, doc).Should().BeTrue();
        AskLiliaService.MayEditWithAi(true, doc with { PlanLocked = true }).Should().BeFalse();
    }
}
