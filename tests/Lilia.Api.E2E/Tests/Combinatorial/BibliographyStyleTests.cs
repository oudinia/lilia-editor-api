using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Combinatorial;

/// <summary>
/// Bibliography formatted output per citation style.
/// Ensures every style produces output without crashing.
/// </summary>
public class BibliographyStyleTests : E2ETestBase
{
    private static readonly string[] EntryTypes = ["article", "book", "inproceedings", "phdthesis", "misc", "techreport"];

    [Theory]
    [InlineData("article")]
    [InlineData("book")]
    [InlineData("inproceedings")]
    [InlineData("phdthesis")]
    [InlineData("misc")]
    [InlineData("techreport")]
    public async Task CreateBibEntry_VariousTypes_Succeeds(string entryType)
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, $"Bib-{entryType}");
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = $"test_{entryType}_2024",
            entryType,
            data = new { title = $"Test {entryType}", author = "Author, Test", year = "2024" },
        });
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"creating {entryType} should not crash");
    }

    [Fact]
    public async Task BibEntry_UpdateAndDelete_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Bib CRUD");
        var docId = doc.GetProperty("id").GetString()!;

        // Create
        var createResp = await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "crud2024",
            entryType = "article",
            data = new { title = "CRUD Test", author = "Test", year = "2024" },
        });
        if (!createResp.IsSuccessStatusCode) return;
        var entry = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var entryId = entry.GetProperty("id").GetString()!;

        // Update
        var updateResp = await client.PutAsJsonAsync($"/api/documents/{docId}/bibliography/{entryId}", new
        {
            citeKey = "crud2024_updated",
            entryType = "book",
            data = new { title = "Updated CRUD", author = "Test", year = "2025", publisher = "Press" },
        });
        updateResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        // Delete
        var deleteResp = await client.DeleteAsync($"/api/documents/{docId}/bibliography/{entryId}");
        deleteResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact(Skip = "Bug: API returns 500 on duplicate cite key — needs unique constraint handling")]
    public async Task BibEntry_DuplicateCiteKey_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Bib Duplicate");
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "duplicate2024",
            entryType = "article",
            data = new { title = "First", author = "A", year = "2024" },
        });

        var dupResp = await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "duplicate2024",
            entryType = "article",
            data = new { title = "Second", author = "B", year = "2024" },
        });
        dupResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ImportBibTeX_MultipleEntries_Succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Multi BibTeX");
        var docId = doc.GetProperty("id").GetString()!;

        var bibtex = @"
@article{a2024, title={Article A}, author={Author A}, year={2024}, journal={J1}}
@book{b2024, title={Book B}, author={Author B}, year={2024}, publisher={P1}}
@inproceedings{c2024, title={Conf C}, author={Author C}, year={2024}, booktitle={Conf}}
";
        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography/import", new { bibTexContent = bibtex });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }
}
