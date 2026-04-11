using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for formula library — CRUD, favorites, categories.
/// </summary>
public class FormulasE2ETests : E2ETestBase
{
    [Fact]
    public async Task ListFormulas_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/formulas");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCategories_ReturnsOk()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/formulas/categories");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateFormula_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/formulas", new
        {
            name = "E2E Quadratic",
            latex = @"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}",
            category = "algebra",
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        if (response.IsSuccessStatusCode)
        {
            var formula = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (formula.TryGetProperty("id", out var id))
                TrackForCleanup("/api/formulas", id.GetString()!);
        }
    }

    [Fact]
    public async Task ToggleFavorite_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();

        // Create formula first
        var createResp = await client.PostAsJsonAsync("/api/formulas", new
        {
            name = "E2E Favorite",
            latex = @"\sum_{i=1}^{n} i",
            category = "calculus",
        });
        if (!createResp.IsSuccessStatusCode) return;
        var formula = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var formulaId = formula.GetProperty("id").GetString()!;
        TrackForCleanup("/api/formulas", formulaId);

        var favResp = await client.PostAsync($"/api/formulas/{formulaId}/favorite", null);
        favResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}
