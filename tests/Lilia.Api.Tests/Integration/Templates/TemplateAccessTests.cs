using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Templates;

/// <summary>
/// A template copies content into the caller's account, so it follows the same
/// access rules as reading: a private template is used only by its owner, and
/// a template is made only from a document the caller may read (as Duplicate).
/// Found by the e2e run with a second user.
/// </summary>
[Collection("Integration")]
public class TemplateAccessTests : IntegrationTestBase
{
    private const string Owner = "test_user_001";
    private const string Other = "test_user_002";

    public TemplateAccessTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Document> SeedTemplateAsync(string ownerId, bool isPublic, string name)
    {
        await using var db = CreateDbContext();
        if (ownerId == "system" && await db.Users.FindAsync("system") is null)
        {
            db.Users.Add(new User
            {
                Id = "system", Email = "system@lilia.internal", Name = "Lilia",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        }
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Title = name,
            IsTemplate = true,
            IsPublicTemplate = isPublic,
            TemplateName = name,
            TemplateCategory = "academic",
            Language = "en",
            PaperSize = "a4",
            FontFamily = "serif",
            FontSize = 12,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Secret recipe"}""");
        return doc;
    }

    private async Task<int> DocumentCountAsync(string ownerId)
    {
        await using var db = CreateDbContext();
        return await db.Documents.CountAsync(d => d.OwnerId == ownerId);
    }

    [Fact]
    public async Task Another_user_cannot_use_a_private_template()
    {
        await SeedUserAsync(Owner);
        await SeedUserAsync(Other);
        var template = await SeedTemplateAsync(Owner, isPublic: false, "Private template");

        using var other = CreateClientAs(Other);
        var get = await other.GetAsync($"/api/templates/{template.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var use = await other.PostAsJsonAsync($"/api/templates/{template.Id}/use", new { title = "Mine now" });

        use.StatusCode.Should().Be(HttpStatusCode.NotFound, "using follows the same rule as reading");
        (await DocumentCountAsync(Other)).Should().Be(0, "nothing is copied into the other user's account");
        await using var db = CreateDbContext();
        (await db.Documents.SingleAsync(d => d.Id == template.Id)).TemplateUsageCount.Should().Be(0);
    }

    [Fact]
    public async Task The_owner_uses_their_private_template()
    {
        await SeedUserAsync(Owner);
        var template = await SeedTemplateAsync(Owner, isPublic: false, "Private template");

        using var owner = CreateClientAs(Owner);
        var use = await owner.PostAsJsonAsync($"/api/templates/{template.Id}/use", new { title = "From my template" });

        use.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await use.Content.ReadFromJsonAsync<DocumentDto>();
        doc!.Title.Should().Be("From my template");
    }

    [Fact]
    public async Task Another_user_uses_a_public_template_and_a_system_template()
    {
        await SeedUserAsync(Owner);
        await SeedUserAsync(Other);
        var publicTemplate = await SeedTemplateAsync(Owner, isPublic: true, "Public template");
        var systemTemplate = await SeedTemplateAsync("system", isPublic: false, "System template");

        using var other = CreateClientAs(Other);
        (await other.PostAsJsonAsync($"/api/templates/{publicTemplate.Id}/use", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await other.PostAsJsonAsync($"/api/templates/{systemTemplate.Id}/use", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await DocumentCountAsync(Other)).Should().Be(2);
    }

    [Fact]
    public async Task Using_a_template_that_does_not_exist_is_not_found()
    {
        await SeedUserAsync(Owner);

        using var owner = CreateClientAs(Owner);
        var use = await owner.PostAsJsonAsync($"/api/templates/{Guid.NewGuid()}/use", new { });

        use.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task ShareAsync(Guid documentId, string userId, string roleName)
    {
        await using var db = CreateDbContext();
        var role = await db.Roles.SingleAsync(r => r.Name == roleName);
        db.DocumentCollaborators.Add(new DocumentCollaborator
        {
            Id = Guid.NewGuid(), DocumentId = documentId, UserId = userId, RoleId = role.Id,
            InvitedBy = Owner, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static object NewTemplate(Guid documentId) => new
    {
        documentId,
        name = "Lifted template",
        description = "made from a document",
        category = "academic",
        isPublic = true,
    };

    [Fact]
    public async Task A_stranger_cannot_make_a_template_from_someone_elses_document()
    {
        await SeedUserAsync(Owner);
        await SeedUserAsync(Other);
        var doc = await SeedDocumentAsync(Owner, "Unpublished paper");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Unpublished result"}""");

        using var other = CreateClientAs(Other);
        var create = await other.PostAsJsonAsync("/api/templates", NewTemplate(doc.Id));

        create.StatusCode.Should().Be(HttpStatusCode.NotFound, "like GET, the document's existence is not revealed");
        (await DocumentCountAsync(Other)).Should().Be(0, "no copy of the blocks lands in the stranger's account");
    }

    [Fact]
    public async Task A_viewer_makes_a_template_from_a_document_shared_with_them()
    {
        await SeedUserAsync(Owner);
        await SeedUserAsync(Other);
        var doc = await SeedDocumentAsync(Owner, "Shared paper");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Shared result"}""");
        await ShareAsync(doc.Id, Other, RoleNames.Viewer);

        using var viewer = CreateClientAs(Other);
        var create = await viewer.PostAsJsonAsync("/api/templates", NewTemplate(doc.Id));

        create.StatusCode.Should().Be(HttpStatusCode.Created, "a viewer may Duplicate, and a template is a copy");
        var template = await create.Content.ReadFromJsonAsync<TemplateDto>();
        template!.UserId.Should().Be(Other);
        template.Content.GetProperty("blocks").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Making_a_template_from_a_document_that_does_not_exist_is_not_found()
    {
        await SeedUserAsync(Owner);

        using var owner = CreateClientAs(Owner);
        var create = await owner.PostAsJsonAsync("/api/templates", NewTemplate(Guid.NewGuid()));

        create.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
