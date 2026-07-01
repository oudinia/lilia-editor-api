using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class SharingControllerTests : IntegrationTestBase
{
    // The default Client authenticates as this user (TestAuthHandler fallback).
    private const string OwnerId = "test_user_001";
    private const string CollaboratorId = "test_user_002";
    private const string CollaboratorId2 = "test_user_003";

    public SharingControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    // --- seeding helpers ---

    private async Task<Guid> EnsureRoleAsync(string name)
    {
        await using var db = CreateDbContext();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
        if (role == null)
        {
            role = new Role { Id = Guid.NewGuid(), Name = name };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        return role.Id;
    }

    private async Task SeedCollaboratorAsync(Guid documentId, string userId, string role = "editor")
    {
        var roleId = await EnsureRoleAsync(role);
        await using var db = CreateDbContext();
        db.DocumentCollaborators.Add(new DocumentCollaborator
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            UserId = userId,
            RoleId = roleId,
            InvitedBy = OwnerId,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedPendingInviteAsync(Guid documentId, string email, string role = "editor")
    {
        await using var db = CreateDbContext();
        db.DocumentPendingInvites.Add(new DocumentPendingInvite
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Email = email,
            Role = role,
            InvitedBy = OwnerId,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();
    }

    private async Task MakePublicAsync(Guid documentId, string? shareLink = null)
    {
        await using var db = CreateDbContext();
        var doc = await db.Documents.FirstAsync(d => d.Id == documentId);
        doc.IsPublic = true;
        doc.ShareLink = shareLink;
        await db.SaveChangesAsync();
    }

    // --- GET /api/shared/by-me ---

    [Fact]
    public async Task SharedByMe_ExcludesPrivateUnsharedDocs()
    {
        await SeedUserAsync(OwnerId);
        await SeedDocumentAsync(OwnerId, "Private Draft");

        var response = await Client.GetAsync("/api/shared/by-me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<SharedByMeDto>>();
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedByMe_IncludesPublicDoc()
    {
        await SeedUserAsync(OwnerId);
        var doc = await SeedDocumentAsync(OwnerId, "Public Paper");
        await MakePublicAsync(doc.Id, "https://liliaeditor.com/s/abc");

        var response = await Client.GetAsync("/api/shared/by-me");

        var items = await response.Content.ReadFromJsonAsync<List<SharedByMeDto>>();
        items.Should().ContainSingle(i => i.Id == doc.Id);
        var row = items!.Single(i => i.Id == doc.Id);
        row.IsPublic.Should().BeTrue();
        row.HasShareLink.Should().BeTrue();
        row.CollaboratorCount.Should().Be(0);
    }

    [Fact]
    public async Task SharedByMe_IncludesDocWithCollaborator_AndSummary()
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(CollaboratorId, "collab@lilia.test", "Collab User");
        var doc = await SeedDocumentAsync(OwnerId, "Shared Thesis");
        await SeedCollaboratorAsync(doc.Id, CollaboratorId, "editor");

        var response = await Client.GetAsync("/api/shared/by-me");

        var items = await response.Content.ReadFromJsonAsync<List<SharedByMeDto>>();
        var row = items!.Single(i => i.Id == doc.Id);
        row.IsPublic.Should().BeFalse();
        row.HasShareLink.Should().BeFalse();
        row.CollaboratorCount.Should().Be(1);
        row.Collaborators.Should().ContainSingle();
        row.Collaborators[0].UserId.Should().Be(CollaboratorId);
        row.Collaborators[0].Name.Should().Be("Collab User");
        row.Collaborators[0].Role.Should().Be("editor");
    }

    [Fact]
    public async Task SharedByMe_CountsPendingInvites()
    {
        await SeedUserAsync(OwnerId);
        var doc = await SeedDocumentAsync(OwnerId, "Doc With Invite");
        await SeedPendingInviteAsync(doc.Id, "invitee@example.com");

        var response = await Client.GetAsync("/api/shared/by-me");

        var items = await response.Content.ReadFromJsonAsync<List<SharedByMeDto>>();
        var row = items!.Single(i => i.Id == doc.Id);
        row.PendingInviteCount.Should().Be(1);
        row.CollaboratorCount.Should().Be(0);
    }

    // --- GET /api/shared/people ---

    [Fact]
    public async Task People_AggregatesSameCollaboratorAcrossDocs()
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(CollaboratorId, "collab@lilia.test", "Collab User");
        var docA = await SeedDocumentAsync(OwnerId, "Doc A");
        var docB = await SeedDocumentAsync(OwnerId, "Doc B");
        await SeedCollaboratorAsync(docA.Id, CollaboratorId, "editor");
        await SeedCollaboratorAsync(docB.Id, CollaboratorId, "viewer");

        var response = await Client.GetAsync("/api/shared/people");

        var people = await response.Content.ReadFromJsonAsync<List<SharedPersonDto>>();
        var person = people!.Single(p => p.UserId == CollaboratorId);
        person.Status.Should().Be("active");
        person.DocumentCount.Should().Be(2);
        person.Documents.Should().Contain(d => d.DocumentId == docA.Id && d.Role == "editor" && d.Status == "active");
        person.Documents.Should().Contain(d => d.DocumentId == docB.Id && d.Role == "viewer" && d.Status == "active");
    }

    [Fact]
    public async Task People_ListsUnregisteredPendingInviteAsPending()
    {
        await SeedUserAsync(OwnerId);
        var doc = await SeedDocumentAsync(OwnerId, "Doc");
        await SeedPendingInviteAsync(doc.Id, "stranger@example.com", "editor");

        var response = await Client.GetAsync("/api/shared/people");

        var people = await response.Content.ReadFromJsonAsync<List<SharedPersonDto>>();
        var person = people!.Single(p => p.Email == "stranger@example.com");
        person.Status.Should().Be("pending");
        person.UserId.Should().BeNull();
        person.DocumentCount.Should().Be(1);
        person.Documents[0].Status.Should().Be("pending");
    }

    [Fact]
    public async Task People_ActiveWinsOverStalePendingForSameDoc()
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(CollaboratorId, "collab@lilia.test", "Collab User");
        var doc = await SeedDocumentAsync(OwnerId, "Doc");
        // Registered collaborator that also still has a lingering pending row
        // for the same address+document (the invite path creates both).
        await SeedCollaboratorAsync(doc.Id, CollaboratorId, "editor");
        await SeedPendingInviteAsync(doc.Id, "collab@lilia.test", "editor");

        var response = await Client.GetAsync("/api/shared/people");

        var people = await response.Content.ReadFromJsonAsync<List<SharedPersonDto>>();
        var person = people!.Single(p => p.Email == "collab@lilia.test");
        person.UserId.Should().Be(CollaboratorId);
        person.Status.Should().Be("active");
        person.DocumentCount.Should().Be(1); // not double-counted
        person.Documents[0].Status.Should().Be("active");
    }

    [Fact]
    public async Task People_ExcludesCollaboratorsOnDocsIDoNotOwn()
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(CollaboratorId2, "other-owner@lilia.test", "Other Owner");
        await SeedUserAsync(CollaboratorId, "collab@lilia.test", "Collab User");
        // A doc owned by someone else, with a collaborator — must not leak.
        var foreignDoc = await SeedDocumentAsync(CollaboratorId2, "Not Mine");
        await SeedCollaboratorAsync(foreignDoc.Id, CollaboratorId, "editor");

        var response = await Client.GetAsync("/api/shared/people");

        var people = await response.Content.ReadFromJsonAsync<List<SharedPersonDto>>();
        people.Should().NotContain(p => p.UserId == CollaboratorId);
    }

    // --- POST /api/shared/resend ---

    [Fact]
    public async Task Resend_ReturnsFailure_WhenNoPendingInvite()
    {
        await SeedUserAsync(OwnerId);
        var doc = await SeedDocumentAsync(OwnerId, "Doc");

        var response = await Client.PostAsJsonAsync("/api/shared/resend",
            new ResendInviteDto(doc.Id, "nobody@example.com"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<InviteResultDto>();
        result!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Resend_ReturnsFailure_WhenNotOwner()
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(CollaboratorId2, "other-owner@lilia.test");
        var foreignDoc = await SeedDocumentAsync(CollaboratorId2, "Not Mine");
        await SeedPendingInviteAsync(foreignDoc.Id, "invitee@example.com");

        var response = await Client.PostAsJsonAsync("/api/shared/resend",
            new ResendInviteDto(foreignDoc.Id, "invitee@example.com"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<InviteResultDto>();
        result!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Resend_Succeeds_ForPendingInvite()
    {
        await SeedUserAsync(OwnerId, "owner@lilia.test", "Owner");
        var doc = await SeedDocumentAsync(OwnerId, "Doc");
        await SeedPendingInviteAsync(doc.Id, "invitee@example.com", "editor");

        var response = await Client.PostAsJsonAsync("/api/shared/resend",
            new ResendInviteDto(doc.Id, "invitee@example.com"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<InviteResultDto>();
        // Email delivery is best-effort (swallowed on failure); the invite
        // itself is refreshed, so the operation reports success.
        result!.Success.Should().BeTrue();
    }

    // --- auth ---

    [Fact]
    public async Task Endpoints_Return401_WhenAnonymous()
    {
        using var anon = CreateAnonymousClient();

        (await anon.GetAsync("/api/shared/by-me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("/api/shared/people")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
