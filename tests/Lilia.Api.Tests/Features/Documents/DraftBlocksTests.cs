using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Features.Documents;

/// <summary>
/// Draft blocks had no test (2 Nov 2026, the coverage round). A person's drafts are theirs: another
/// person can't read, change, favourite or delete them.
/// </summary>
public class DraftBlocksTests : IntegrationTestBase
{
    public DraftBlocksTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static object Draft(string name) => new { type = "paragraph", name, content = JsonSerializer.SerializeToElement(new { text = "A reusable paragraph." }) };

    [Fact]
    public async Task A_person_keeps_changes_and_deletes_their_draft()
    {
        await SeedUserAsync("draft_owner");
        using var c = CreateClientAs("draft_owner");
        var created = await c.PostAsJsonAsync("/api/draft-blocks", Draft("Intro"));
        created.IsSuccessStatusCode.Should().BeTrue();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await c.PutAsJsonAsync($"/api/draft-blocks/{id}", new { name = "Introduction" })).IsSuccessStatusCode.Should().BeTrue();
        (await c.GetFromJsonAsync<JsonElement>($"/api/draft-blocks/{id}")).GetProperty("name").GetString().Should().Be("Introduction");
        (await c.GetStringAsync("/api/draft-blocks")).Should().Contain(id.ToString());
        (await c.DeleteAsync($"/api/draft-blocks/{id}")).IsSuccessStatusCode.Should().BeTrue();
        (await c.GetAsync($"/api/draft-blocks/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_person_cannot_reach_someone_elses_draft()
    {
        await SeedUserAsync("draft_alice");
        await SeedUserAsync("draft_bob");
        using var alice = CreateClientAs("draft_alice");
        var id = (await (await alice.PostAsJsonAsync("/api/draft-blocks", Draft("Alice's"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var bob = CreateClientAs("draft_bob");
        (await bob.GetStringAsync("/api/draft-blocks")).Should().NotContain(id.ToString());
        (await bob.GetAsync($"/api/draft-blocks/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.PutAsJsonAsync($"/api/draft-blocks/{id}", new { name = "Bob's now" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.PostAsync($"/api/draft-blocks/{id}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.DeleteAsync($"/api/draft-blocks/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await alice.GetFromJsonAsync<JsonElement>($"/api/draft-blocks/{id}")).GetProperty("name").GetString().Should().Be("Alice's");
    }
}
