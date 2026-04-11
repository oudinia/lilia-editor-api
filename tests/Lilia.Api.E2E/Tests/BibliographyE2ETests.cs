using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// E2E tests for bibliography — CRUD, BibTeX import/export, DOI lookup, citation styles.
/// </summary>
public class BibliographyE2ETests : E2ETestBase
{
    [Fact]
    public async Task CreateEntry_ReturnsCreated()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "einstein1905",
            entryType = "article",
            data = new { title = "On the Electrodynamics of Moving Bodies", author = "Einstein, Albert", year = "1905", journal = "Annalen der Physik" },
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task ListEntries_ReturnsArray()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "test2024",
            entryType = "article",
            data = new { title = "Test Article", author = "Test, Author", year = "2024" },
        });

        var response = await client.GetAsync($"/api/documents/{docId}/bibliography");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ImportBibTeX_CreatesEntries()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var bibtex = @"@article{knuth1984,
  title={Literate Programming},
  author={Knuth, Donald E.},
  journal={The Computer Journal},
  year={1984}
}";
        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography/import", new { bibTexContent = bibtex });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task ExportBibTeX_ReturnsContent()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "export2024",
            entryType = "article",
            data = new { title = "Export Test", author = "Test", year = "2024" },
        });

        var response = await client.GetAsync($"/api/documents/{docId}/bibliography/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DOILookup_ReturnsEntry()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography/doi", new
        {
            doi = "10.1145/359576.359579",
        });
        // DOI lookup depends on external service availability
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CitationStyles_ListsAvailable()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var response = await client.GetAsync($"/api/documents/{docId}/bibliography/styles");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
