using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class TemplatesControllerTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";

    public TemplatesControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    /// <summary>
    /// Seeds a public system template (a Document owned by "system" with
    /// IsTemplate=true). Production seeds these from database/003_seed_templates.sql
    /// at deploy time; the migrate-only test DB has none, and the per-test
    /// cleanup wipes documents — so tests that need a system template seed
    /// their own here instead of relying on shared global state.
    /// </summary>
    private async Task<Lilia.Core.Entities.Document> SeedSystemTemplateAsync(
        string? category = "academic", string? name = "System Article Template")
    {
        await using var db = CreateDbContext();
        if (await db.Users.FindAsync("system") is null)
        {
            db.Users.Add(new Lilia.Core.Entities.User
            {
                Id = "system",
                Email = "system@lilia.internal",
                Name = "Lilia",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        var doc = new Lilia.Core.Entities.Document
        {
            Id = Guid.NewGuid(),
            OwnerId = "system",
            Title = name!,
            IsTemplate = true,
            IsPublicTemplate = true,
            TemplateName = name,
            TemplateCategory = category,
            TemplateDescription = "Seeded system template for tests.",
            Language = "en",
            PaperSize = "a4",
            FontFamily = "serif",
            FontSize = 12,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    [Fact]
    public async Task GetTemplates_ReturnsSystemTemplates()
    {
        await SeedUserAsync(UserId);
        await SeedSystemTemplateAsync();

        var response = await Client.GetAsync("/api/templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var templates = await response.Content.ReadFromJsonAsync<List<TemplateListDto>>();
        templates.Should().NotBeNull();
        templates!.Should().Contain(t => t.IsSystem);
    }

    [Fact]
    public async Task GetTemplates_FiltersByCategory()
    {
        await SeedUserAsync(UserId);

        // First get all templates to find a category that exists
        var allResponse = await Client.GetAsync("/api/templates");
        var allTemplates = await allResponse.Content.ReadFromJsonAsync<List<TemplateListDto>>();
        var firstCategory = allTemplates?.FirstOrDefault(t => t.Category != null)?.Category;

        if (firstCategory != null)
        {
            var response = await Client.GetAsync($"/api/templates?category={firstCategory}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var filtered = await response.Content.ReadFromJsonAsync<List<TemplateListDto>>();
            filtered!.Should().OnlyContain(t => t.Category == firstCategory);
        }
    }

    [Fact]
    public async Task GetTemplate_ById_ReturnsTemplate()
    {
        await SeedUserAsync(UserId);
        await SeedSystemTemplateAsync();

        // Get list first to find a valid ID
        var listResponse = await Client.GetAsync("/api/templates");
        var templates = await listResponse.Content.ReadFromJsonAsync<List<TemplateListDto>>();
        var first = templates!.First();

        var response = await Client.GetAsync($"/api/templates/{first.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var template = await response.Content.ReadFromJsonAsync<TemplateDto>();
        template!.Id.Should().Be(first.Id);
        template.Name.Should().Be(first.Name);
    }

    [Fact]
    public async Task GetCategories_ReturnsCategories()
    {
        await SeedUserAsync(UserId);

        var response = await Client.GetAsync("/api/templates/categories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var categories = await response.Content.ReadFromJsonAsync<List<TemplateCategoryDto>>();
        categories.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAndDeleteTemplate_Succeeds()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Template Source");

        // Create template from document
        var createResponse = await Client.PostAsJsonAsync("/api/templates", new
        {
            documentId = doc.Id,
            name = "My Template",
            description = "A test template",
            category = "academic",
            isPublic = false
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var template = await createResponse.Content.ReadFromJsonAsync<TemplateDto>();
        template!.Name.Should().Be("My Template");
        template.IsSystem.Should().BeFalse();

        // Delete it
        var deleteResponse = await Client.DeleteAsync($"/api/templates/{template.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetTemplates_Returns401_WhenAnonymous()
    {
        using var anonClient = CreateAnonymousClient();
        var response = await anonClient.GetAsync("/api/templates");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
