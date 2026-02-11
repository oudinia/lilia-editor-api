using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class BibliographyControllerTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";
    private const string OtherUserId = "test_user_002";

    public BibliographyControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Lilia.Core.Entities.Document> SeedDocWithOwner()
    {
        await SeedUserAsync(UserId);
        return await SeedDocumentAsync(UserId, "Bib Doc");
    }

    // --- GET entries ---

    [Fact]
    public async Task GetEntries_ReturnsEntries()
    {
        var doc = await SeedDocWithOwner();
        await SeedBibliographyEntryAsync(doc.Id, "smith2024", "article");
        await SeedBibliographyEntryAsync(doc.Id, "jones2023", "book");

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/bibliography");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<List<BibliographyEntryDto>>();
        entries.Should().HaveCount(2);
    }

    // --- POST entry ---

    [Fact]
    public async Task CreateEntry_ReturnsCreated()
    {
        var doc = await SeedDocWithOwner();

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/bibliography", new
        {
            citeKey = "doe2024",
            entryType = "article",
            data = JsonSerializer.Deserialize<JsonElement>("""{"title":"Test","author":"Doe, Jane","year":"2024"}""")
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var entry = await response.Content.ReadFromJsonAsync<BibliographyEntryDto>();
        entry!.CiteKey.Should().Be("doe2024");
        entry.EntryType.Should().Be("article");
    }

    // --- PUT entry ---

    [Fact]
    public async Task UpdateEntry_UpdatesCiteKey()
    {
        var doc = await SeedDocWithOwner();
        var seeded = await SeedBibliographyEntryAsync(doc.Id, "old_key", "article");

        var response = await Client.PutAsJsonAsync($"/api/documents/{doc.Id}/bibliography/{seeded.Id}", new
        {
            citeKey = "new_key"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry = await response.Content.ReadFromJsonAsync<BibliographyEntryDto>();
        entry!.CiteKey.Should().Be("new_key");
    }

    // --- DELETE entry ---

    [Fact]
    public async Task DeleteEntry_Returns204()
    {
        var doc = await SeedDocWithOwner();
        var seeded = await SeedBibliographyEntryAsync(doc.Id);

        var response = await Client.DeleteAsync($"/api/documents/{doc.Id}/bibliography/{seeded.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // --- POST import bibtex ---

    [Fact]
    public async Task ImportBibTex_ImportsEntries()
    {
        var doc = await SeedDocWithOwner();

        var bibtex = """
            @article{einstein1905,
              title={On the Electrodynamics of Moving Bodies},
              author={Einstein, Albert},
              year={1905},
              journal={Annalen der Physik}
            }
            """;

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/bibliography/import", new
        {
            bibTexContent = bibtex
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<List<BibliographyEntryDto>>();
        entries.Should().HaveCount(1);
        entries![0].CiteKey.Should().Be("einstein1905");
    }

    // --- GET export ---

    [Fact]
    public async Task ExportBibTex_ReturnsText()
    {
        var doc = await SeedDocWithOwner();
        await SeedBibliographyEntryAsync(doc.Id, "test2024", "article",
            """{"title":"Export Test","author":"Test, Author","year":"2024"}""");

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/bibliography/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("test2024");
    }

    // --- Access control ---

    [Fact]
    public async Task GetEntries_Returns403_WhenNotOwner()
    {
        await SeedUserAsync(UserId);
        await SeedUserAsync(OtherUserId);
        var doc = await SeedDocumentAsync(OtherUserId, "Other's Doc");

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/bibliography");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
