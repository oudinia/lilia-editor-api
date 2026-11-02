using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Features.Library;

/// <summary>
/// The formula library had no test (2 Nov 2026, the coverage round; the first file of the Library
/// project with none). A person's own formulas are theirs: another person can't read, change,
/// favourite or delete them.
/// </summary>
public class FormulasTests : IntegrationTestBase
{
    public FormulasTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static object Formula(string name) => new { name, latexContent = @"\int_0^1 x\,dx = \tfrac12", category = "calculus" };

    [Fact]
    public async Task A_person_saves_changes_favourites_and_deletes_their_formula()
    {
        await SeedUserAsync("formula_owner");
        using var c = CreateClientAs("formula_owner");
        var created = await c.PostAsJsonAsync("/api/formulas", Formula("Half"));
        created.IsSuccessStatusCode.Should().BeTrue();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await c.PutAsJsonAsync($"/api/formulas/{id}", new { name = "One half" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetFromJsonAsync<JsonElement>($"/api/formulas/{id}")).GetProperty("name").GetString().Should().Be("One half");
        (await c.PostAsync($"/api/formulas/{id}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetFromJsonAsync<JsonElement>($"/api/formulas/{id}")).GetProperty("isFavorite").GetBoolean().Should().BeTrue();
        (await c.DeleteAsync($"/api/formulas/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync($"/api/formulas/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_person_cannot_reach_someone_elses_formula()
    {
        await SeedUserAsync("formula_alice");
        await SeedUserAsync("formula_bob");
        using var alice = CreateClientAs("formula_alice");
        var id = (await (await alice.PostAsJsonAsync("/api/formulas", Formula("Alice's"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var bob = CreateClientAs("formula_bob");
        (await bob.GetAsync($"/api/formulas/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.PutAsJsonAsync($"/api/formulas/{id}", new { name = "Bob's now" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.PostAsync($"/api/formulas/{id}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.DeleteAsync($"/api/formulas/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await alice.GetFromJsonAsync<JsonElement>($"/api/formulas/{id}")).GetProperty("name").GetString().Should().Be("Alice's");
    }
}
