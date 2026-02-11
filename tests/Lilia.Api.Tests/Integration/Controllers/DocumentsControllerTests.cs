using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class DocumentsControllerTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";
    private const string OtherUserId = "test_user_002";

    public DocumentsControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task SeedDefaultUsers()
    {
        await SeedUserAsync(UserId, "test@lilia.test", "Test User");
        await SeedUserAsync(OtherUserId, "other@lilia.test", "Other User");
    }

    // --- GET /api/documents ---

    [Fact]
    public async Task GetDocuments_ReturnsEmptyList_WhenNoDocuments()
    {
        await SeedDefaultUsers();

        var response = await Client.GetAsync("/api/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDocuments_ReturnsOnlyOwnedDocuments()
    {
        await SeedDefaultUsers();
        await SeedDocumentAsync(UserId, "My Doc");
        await SeedDocumentAsync(OtherUserId, "Other Doc");

        var response = await Client.GetAsync("/api/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListDto>>();
        result!.Items.Should().HaveCount(1);
        result.Items[0].Title.Should().Be("My Doc");
    }

    [Fact]
    public async Task GetDocuments_SupportsPagination()
    {
        await SeedDefaultUsers();
        for (int i = 0; i < 5; i++)
            await SeedDocumentAsync(UserId, $"Doc {i}");

        var response = await Client.GetAsync("/api/documents?page=1&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListDto>>();
        result!.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetDocuments_SupportsSearch()
    {
        await SeedDefaultUsers();
        await SeedDocumentAsync(UserId, "Research Paper");
        await SeedDocumentAsync(UserId, "Thesis Draft");

        var response = await Client.GetAsync("/api/documents?search=Research");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedResult<DocumentListDto>>();
        result!.Items.Should().HaveCount(1);
        result.Items[0].Title.Should().Be("Research Paper");
    }

    [Fact]
    public async Task GetDocuments_Returns401_WhenAnonymous()
    {
        using var anonClient = CreateAnonymousClient();
        var response = await anonClient.GetAsync("/api/documents");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- POST /api/documents ---

    [Fact]
    public async Task CreateDocument_WithParams_ReturnsCreated()
    {
        await SeedDefaultUsers();

        var response = await Client.PostAsJsonAsync("/api/documents", new
        {
            title = "New Document",
            language = "fr",
            paperSize = "letter"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await response.Content.ReadFromJsonAsync<DocumentDto>();
        doc.Should().NotBeNull();
        doc!.Title.Should().Be("New Document");
        doc.Language.Should().Be("fr");
        doc.PaperSize.Should().Be("letter");
        doc.OwnerId.Should().Be(UserId);
    }

    [Fact]
    public async Task CreateDocument_WithDefaults_ReturnsCreated()
    {
        await SeedDefaultUsers();

        var response = await Client.PostAsJsonAsync("/api/documents", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await response.Content.ReadFromJsonAsync<DocumentDto>();
        doc!.Title.Should().Be("Untitled");
        doc.Language.Should().Be("en");
    }

    // --- GET /api/documents/{id} ---

    [Fact]
    public async Task GetDocument_ReturnsDocument_WhenOwner()
    {
        await SeedDefaultUsers();
        var seeded = await SeedDocumentAsync(UserId, "My Doc");

        var response = await Client.GetAsync($"/api/documents/{seeded.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<DocumentDto>();
        doc!.Id.Should().Be(seeded.Id);
        doc.Title.Should().Be("My Doc");
    }

    [Fact]
    public async Task GetDocument_Returns404_WhenNotExist()
    {
        await SeedDefaultUsers();

        var response = await Client.GetAsync($"/api/documents/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDocument_Returns404_WhenNotOwner()
    {
        await SeedDefaultUsers();
        var seeded = await SeedDocumentAsync(OtherUserId, "Other's Doc");

        var response = await Client.GetAsync($"/api/documents/{seeded.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- PUT /api/documents/{id} ---

    [Fact]
    public async Task UpdateDocument_UpdatesTitle()
    {
        await SeedDefaultUsers();
        var seeded = await SeedDocumentAsync(UserId, "Old Title");

        var response = await Client.PutAsJsonAsync($"/api/documents/{seeded.Id}", new
        {
            title = "New Title"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<DocumentDto>();
        doc!.Title.Should().Be("New Title");
    }

    // --- DELETE /api/documents/{id} ---

    [Fact]
    public async Task DeleteDocument_Returns204_AndDocumentDisappears()
    {
        await SeedDefaultUsers();
        var seeded = await SeedDocumentAsync(UserId, "To Delete");

        var deleteResponse = await Client.DeleteAsync($"/api/documents/{seeded.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await Client.GetAsync($"/api/documents/{seeded.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteDocument_Returns404_WhenNotOwner()
    {
        await SeedDefaultUsers();
        var seeded = await SeedDocumentAsync(OtherUserId, "Other's Doc");

        var response = await Client.DeleteAsync($"/api/documents/{seeded.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- POST /api/documents/{id}/duplicate ---

    [Fact]
    public async Task DuplicateDocument_CreatesNewCopy()
    {
        await SeedDefaultUsers();
        var seeded = await SeedDocumentAsync(UserId, "Original");

        var response = await Client.PostAsync($"/api/documents/{seeded.Id}/duplicate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await response.Content.ReadFromJsonAsync<DocumentDto>();
        doc!.Id.Should().NotBe(seeded.Id);
        doc.Title.Should().Contain("Original");
    }

    // --- Share / Revoke ---

    [Fact]
    public async Task ShareDocument_MakesPublic_AndReturnsShareLink()
    {
        await SeedDefaultUsers();
        var seeded = await SeedDocumentAsync(UserId, "Shared Doc");

        var response = await Client.PostAsJsonAsync($"/api/documents/{seeded.Id}/share", new
        {
            isPublic = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<DocumentShareResultDto>();
        result!.ShareLink.Should().NotBeNullOrEmpty();
        result.IsPublic.Should().BeTrue();
    }

    [Fact]
    public async Task SharedDocument_IsAccessibleAnonymously()
    {
        await SeedDefaultUsers();
        var seeded = await SeedDocumentAsync(UserId, "Shared Doc");

        // Share it
        var shareResponse = await Client.PostAsJsonAsync($"/api/documents/{seeded.Id}/share", new { isPublic = true });
        var shareResult = await shareResponse.Content.ReadFromJsonAsync<DocumentShareResultDto>();

        // Access anonymously via share link
        using var anonClient = CreateAnonymousClient();
        var response = await anonClient.GetAsync($"/api/documents/shared/{shareResult!.ShareLink}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<DocumentDto>();
        doc!.Title.Should().Be("Shared Doc");
    }

    [Fact]
    public async Task RevokeShare_RemovesAccess()
    {
        await SeedDefaultUsers();
        var seeded = await SeedDocumentAsync(UserId, "Shared Doc");

        // Share then revoke
        var shareResponse = await Client.PostAsJsonAsync($"/api/documents/{seeded.Id}/share", new { isPublic = true });
        var shareResult = await shareResponse.Content.ReadFromJsonAsync<DocumentShareResultDto>();

        var revokeResponse = await Client.DeleteAsync($"/api/documents/{seeded.Id}/share");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Anonymous access should fail
        using var anonClient = CreateAnonymousClient();
        var response = await anonClient.GetAsync($"/api/documents/shared/{shareResult!.ShareLink}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
