using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// Ask Lilia's saved conversations had no test (2 Nov 2026, the coverage round). Each person's
/// conversations are theirs alone: another person can't list, read, add to, rename, move, clone or
/// delete them; every attempt reads as "not found", so nothing about them leaks.
/// </summary>
public class AiConversationsTests : IntegrationTestBase
{
    public AiConversationsTests(TestDatabaseFixture fixture) : base(fixture) { }

    private const string Url = "/api/ai/conversations";

    [Fact]
    public async Task A_conversation_is_kept_renamed_and_deleted_by_its_owner()
    {
        await SeedUserAsync("conv_owner");
        using var c = CreateClientAs("conv_owner");
        var created = await c.PostAsJsonAsync(Url, new { documentId = (Guid?)null, title = "Proof help" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var conv = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = conv.GetProperty("id").GetGuid();

        var msg = await c.PostAsJsonAsync($"{Url}/{id}/messages",
            new { role = "user", content = JsonSerializer.SerializeToElement(new { text = "Is Lemma 1 tight?" }), creditsUsed = 0 });
        msg.StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PatchAsJsonAsync($"{Url}/{id}/rename", new { title = "Lemma 1" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var read = await c.GetFromJsonAsync<JsonElement>($"{Url}/{id}");
        read.GetProperty("title").GetString().Should().Be("Lemma 1");
        read.GetProperty("messages").GetArrayLength().Should().Be(1);
        (await c.GetStringAsync(Url)).Should().Contain(id.ToString());

        (await c.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_person_cannot_reach_someone_elses_conversation()
    {
        await SeedUserAsync("conv_alice");
        await SeedUserAsync("conv_bob");
        using var alice = CreateClientAs("conv_alice");
        var created = await alice.PostAsJsonAsync(Url, new { documentId = (Guid?)null, title = "Private" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var bob = CreateClientAs("conv_bob");
        (await bob.GetStringAsync(Url)).Should().NotContain(id.ToString());
        (await bob.GetAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.PostAsJsonAsync($"{Url}/{id}/messages",
            new { role = "user", content = JsonSerializer.SerializeToElement(new { text = "hi" }), creditsUsed = 0 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.PatchAsJsonAsync($"{Url}/{id}/rename", new { title = "Mine" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.PatchAsJsonAsync($"{Url}/{id}/move", new { documentId = (Guid?)null })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.PostAsJsonAsync($"{Url}/{id}/clone", new { documentId = (Guid?)null, title = "Copy" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Still Alice's, untouched.
        (await alice.GetFromJsonAsync<JsonElement>($"{Url}/{id}")).GetProperty("title").GetString().Should().Be("Private");
    }

    [Fact]
    public async Task A_conversation_cannot_be_attached_to_someone_elses_document()
    {
        await SeedUserAsync("conv_carol");
        await SeedUserAsync("conv_dave");
        var davesDoc = await SeedDocumentAsync("conv_dave", "Dave's paper");
        using var carol = CreateClientAs("conv_carol");
        (await carol.PostAsJsonAsync(Url, new { documentId = davesDoc.Id, title = "Sneaky" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
