using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Feedback triage is for operators (16 Oct): list, change and stats asked only for a login, so any
/// signed-in account could read, change and count everyone's feedback. An author still reads their own.
/// </summary>
[Collection("Integration")]
public class FeedbackTriageAuthzTests : IntegrationTestBase
{
    public FeedbackTriageAuthzTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SubmitAsync(string userId)
    {
        await SeedUserAsync(userId);
        var r = await CreateClientAs(userId).PostAsJsonAsync("/api/feedback", new { type = "bug", message = "The toolbar overlaps.", page = "/d/x" });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_signed_in_non_admin_cannot_list_change_or_count_feedback()
    {
        var id = await SubmitAsync("fb_author");
        await SeedUserAsync("fb_other");
        var other = CreateClientAs("fb_other");
        (await other.GetAsync("/api/feedback")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync("/api/feedback/stats")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PatchAsJsonAsync($"/api/feedback/{id}", new { status = "dismissed" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync($"/api/feedback/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_author_reads_their_own_feedback()
    {
        var id = await SubmitAsync("fb_reader");
        var r = await CreateClientAs("fb_reader").GetAsync($"/api/feedback/{id}");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString().Should().Be("The toolbar overlaps.");
    }
}
