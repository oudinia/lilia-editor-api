using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Sharing;

/// <summary>
/// Access as the rest of the API now asks Sharing for it (2 Nov 2026, the bounded contexts, step 3): a
/// share with a person or with a group they are in opens the document at that role, the document list
/// shows it, and signing up turns an invitation into a share. The old code read Sharing's tables itself;
/// these hold the outcomes the same.
/// </summary>
public class SharingAccessTests : IntegrationTestBase
{
    public SharingAccessTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static string Id(string who) => $"acc_{who}_{Guid.NewGuid():N}"[..24];

    private async Task<Guid> RoleAsync(string name)
    {
        await using var db = CreateDbContext();
        return (await db.Roles.FirstAsync(r => r.Name == name)).Id;
    }

    /// <summary>A document shared with a team group of <paramref name="members"/> at <paramref name="role"/>.</summary>
    private async Task<(Guid Doc, Guid Block)> SharedWithGroupAsync(string owner, string role, params string[] members)
    {
        var doc = await SeedDocumentAsync(owner, "Group paper");
        var block = await SeedBlockAsync(doc.Id);
        var editor = await RoleAsync("editor");
        var shareRole = await RoleAsync(role);
        await using var db = CreateDbContext();
        var team = new Team
        {
            Id = Guid.NewGuid(), Name = "Lab", TeamCode = "lab-" + Guid.NewGuid().ToString("N")[..8],
            Slug = "lab-" + Guid.NewGuid().ToString("N")[..8], OwnerId = owner,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var group = new Group { Id = Guid.NewGuid(), TeamId = team.Id, Name = "Readers", CreatedAt = DateTime.UtcNow };
        foreach (var m in members)
            group.Members.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = m, RoleId = editor, CreatedAt = DateTime.UtcNow });
        team.Groups.Add(group);
        db.Teams.Add(team);
        db.DocumentGroups.Add(new DocumentGroup { Id = Guid.NewGuid(), DocumentId = doc.Id, GroupId = group.Id, RoleId = shareRole, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (doc.Id, block.Id);
    }

    private static async Task<string?> ListedRole(HttpClient client, Guid docId)
    {
        var page = await client.GetFromJsonAsync<PaginatedResult<DocumentListDto>>("/api/documents?page=1&pageSize=100");
        return page!.Items.FirstOrDefault(d => d.Id == docId) is { } d ? d.Role ?? "(none)" : null;
    }

    private static Task<HttpResponseMessage> Edit(HttpClient client, Guid docId, Guid blockId) =>
        client.PutAsJsonAsync($"/api/documents/{docId}/blocks/{blockId}", new { content = new { text = "Changed." } });

    [Fact]
    public async Task A_group_share_opens_the_document_to_its_members_at_the_groups_role()
    {
        var owner = Id("owner"); var reader = Id("reader"); var stranger = Id("stranger");
        foreach (var u in new[] { owner, reader, stranger }) await SeedUserAsync(u);
        var (doc, block) = await SharedWithGroupAsync(owner, "viewer", reader);
        var asReader = CreateClientAs(reader);
        var asStranger = CreateClientAs(stranger);

        (await asReader.GetAsync($"/api/documents/{doc}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListedRole(asReader, doc)).Should().NotBeNull("a group share lists the document");
        (await Edit(asReader, doc, block)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the group can view, not edit");

        (await asStranger.GetAsync($"/api/documents/{doc}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListedRole(asStranger, doc)).Should().BeNull();
    }

    [Fact]
    public async Task An_editor_group_can_edit()
    {
        var owner = Id("owner"); var writer = Id("writer");
        foreach (var u in new[] { owner, writer }) await SeedUserAsync(u);
        var (doc, block) = await SharedWithGroupAsync(owner, "editor", writer);

        (await Edit(CreateClientAs(writer), doc, block)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_direct_share_is_listed_at_its_role_and_opens_at_it()
    {
        var owner = Id("owner"); var viewer = Id("viewer"); var editor = Id("editor");
        foreach (var u in new[] { owner, viewer, editor }) await SeedUserAsync(u);
        var doc = await SeedDocumentAsync(owner, "Direct paper");
        var block = await SeedBlockAsync(doc.Id);
        await using (var db = CreateDbContext())
        {
            db.DocumentCollaborators.AddRange(
                new DocumentCollaborator { Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = viewer, RoleId = await RoleAsync("viewer"), InvitedBy = owner, CreatedAt = DateTime.UtcNow },
                new DocumentCollaborator { Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = editor, RoleId = await RoleAsync("editor"), InvitedBy = owner, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        (await ListedRole(CreateClientAs(viewer), doc.Id)).Should().Be("viewer");
        (await ListedRole(CreateClientAs(editor), doc.Id)).Should().Be("editor");
        (await Edit(CreateClientAs(viewer), doc.Id, block.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Edit(CreateClientAs(editor), doc.Id, block.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_up_turns_an_invitation_into_a_share()
    {
        var owner = Id("owner"); var newcomer = Id("newcomer");
        var email = $"{newcomer}@lilia.test";
        await SeedUserAsync(owner);
        var doc = await SeedDocumentAsync(owner, "Invited paper");
        var token = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            db.DocumentPendingInvites.Add(new DocumentPendingInvite
            {
                Id = token, DocumentId = doc.Id, Email = email, Role = "viewer", InvitedBy = owner,
                Status = "pending", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(14),
            });
            await db.SaveChangesAsync();
        }

        // Sign-up, as the sign-in sync runs it for a new account (the shared test server stubs that sync, so
        // the real UserService is built here): the invitations to its address are claimed.
        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var users = new UserService(sp.GetRequiredService<Lilia.Infrastructure.Data.LiliaDbContext>(), sp.GetRequiredService<IDocumentService>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UserService>.Instance, sp.GetRequiredService<Lilia.Api.Directories.ISharingAccounts>());
            await users.CreateOrUpdateUserAsync(new CreateOrUpdateUserDto(newcomer, email, "New Comer", null));
        }

        (await ListedRole(CreateClientAs(newcomer, email), doc.Id)).Should().Be("viewer");

        await using var check = CreateDbContext();
        (await check.DocumentPendingInvites.FirstAsync(i => i.Id == token)).Status.Should().Be("accepted");
    }
}
