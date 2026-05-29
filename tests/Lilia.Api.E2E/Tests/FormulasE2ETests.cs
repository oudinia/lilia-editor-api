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
            latexContent = @"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}",
            category = "algebra",
            description = "Quadratic formula",
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
            latexContent = @"\sum_{i=1}^{n} i",
            category = "calculus",
            description = "Sum of integers",
        });
        if (!createResp.IsSuccessStatusCode) return;
        var formula = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var formulaId = formula.GetProperty("id").GetString()!;
        TrackForCleanup("/api/formulas", formulaId);

        var favResp = await client.PostAsync($"/api/formulas/{formulaId}/favorite", null);
        favResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetThemeCounts_ReturnsAllEightThemes()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync("/api/formulas/themes");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var counts = await resp.Content.ReadFromJsonAsync<JsonElement>();
        counts.GetArrayLength().Should().Be(8);
        var ids = counts.EnumerateArray().Select(c => c.GetProperty("theme").GetString()).ToHashSet();
        ids.Should().BeEquivalentTo(new[]
        {
            "general", "calculus", "linalg", "stats", "discrete", "sets", "physics", "cs",
        });
    }

    [Fact]
    public async Task SystemThemedSeed_ProducesReferenceFormulas()
    {
        // The themed seed pulls 64 formulas (8 per theme) from
        // lilia-docs/reference/math/data/formulas.json — verify the
        // catalog is visible to an authenticated user.
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.GetAsync("/api/formulas?theme=calculus&pageSize=20");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = page.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
        items[0].GetProperty("theme").GetString().Should().Be("calculus");
    }

    [Fact]
    public async Task CreateFormula_WithThemeAndTokens_PersistsBothFields()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var tokensJson = "[{\"kind\":\"letter\",\"glyph\":\"a\"}]";
        var resp = await client.PostAsJsonAsync("/api/formulas", new
        {
            name = "E2E Themed",
            latexContent = "a + b = c",
            category = "math",
            theme = "general",
            tokensJson,
        });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        if (!resp.IsSuccessStatusCode) return;

        var f = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = f.GetProperty("id").GetString()!;
        TrackForCleanup("/api/formulas", id);

        f.GetProperty("theme").GetString().Should().Be("general");
        f.GetProperty("tokensJson").GetString().Should().Be(tokensJson);
    }

    [Fact]
    public async Task CreateFormula_WithInvalidTheme_SilentlyDropsTheme()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var resp = await client.PostAsJsonAsync("/api/formulas", new
        {
            name = "E2E Invalid Theme",
            latexContent = "a",
            category = "math",
            theme = "not-a-real-theme",
        });
        if (!resp.IsSuccessStatusCode) return;

        var f = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = f.GetProperty("id").GetString()!;
        TrackForCleanup("/api/formulas", id);

        // FormulaThemes.IsValid rejected the value — Theme is null.
        var themeProp = f.GetProperty("theme");
        themeProp.ValueKind.Should().Be(JsonValueKind.Null);
    }
}
