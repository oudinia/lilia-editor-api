using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Sharing;

/// <summary>
/// What Sharing shows of other contexts' records, now that it asks the directories for them (2 Nov 2026, the
/// bounded contexts, step 2): people's names and emails, a group's name and size, a document's title and
/// owner. Each was a join before; these hold the screens the same, including what the join left out (a
/// trashed document's invite is not found).
/// </summary>
public class SharingDirectoriesTests : IntegrationTestBase
{
    public SharingDirectoriesTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static string Id(string who) => $"dir_{who}_{Guid.NewGuid():N}"[..24];

    private async Task<Guid> RoleAsync(string name)
    {
        await using var db = CreateDbContext();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
        if (role != null) return role.Id;
        role = new Role { Id = Guid.NewGuid(), Name = name };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    /// <summary>A team group of <paramref name="members"/>, for sharing a document with.</summary>
    private async Task<Guid> GroupAsync(string ownerId, string name, params string[] members)
    {
        var editor = await RoleAsync("editor");
        await using var db = CreateDbContext();
        var team = new Team
        {
            Id = Guid.NewGuid(), Name = "Lab", TeamCode = "lab-" + Guid.NewGuid().ToString("N")[..8],
            Slug = "lab-" + Guid.NewGuid().ToString("N")[..8], OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var group = new Group { Id = Guid.NewGuid(), TeamId = team.Id, Name = name, CreatedAt = DateTime.UtcNow };
        foreach (var m in members)
            group.Members.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = m, RoleId = editor, CreatedAt = DateTime.UtcNow });
        team.Groups.Add(group);
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return group.Id;
    }

    [Fact]
    public async Task The_share_list_names_the_people_and_the_groups()
    {
        var owner = Id("owner"); var ada = Id("ada"); var bo = Id("bo");
        await SeedUserAsync(owner);
        await SeedUserAsync(ada, $"{ada}@lilia.test", "Ada Lovelace");
        await SeedUserAsync(bo);
        var doc = await SeedDocumentAsync(owner, "Shared paper");
        var groupId = await GroupAsync(owner, "Reviewers", ada, bo);
        var client = CreateClientAs(owner);

        (await client.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/users", new AddUserCollaboratorDto(ada, "viewer")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var added = await client.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/groups", new AddGroupCollaboratorDto(groupId, "editor"));
        added.StatusCode.Should().Be(HttpStatusCode.OK);
        (await added.Content.ReadFromJsonAsync<GroupCollaboratorDto>())!.Should().Match<GroupCollaboratorDto>(g => g.GroupName == "Reviewers" && g.MemberCount == 2);

        var list = await client.GetFromJsonAsync<CollaboratorListDto>($"/api/documents/{doc.Id}/collaborators");

        list!.Users.Should().ContainSingle().Which.Should().Match<UserCollaboratorDto>(u =>
            u.UserId == ada && u.Name == "Ada Lovelace" && u.Email == $"{ada}@lilia.test" && u.RoleName == "viewer");
        list.Groups.Should().ContainSingle().Which.Should().Match<GroupCollaboratorDto>(g =>
            g.GroupId == groupId && g.GroupName == "Reviewers" && g.MemberCount == 2 && g.RoleName == "editor");
    }

    [Fact]
    public async Task A_role_change_answers_with_the_person_or_group_named()
    {
        var owner = Id("owner"); var ada = Id("ada");
        await SeedUserAsync(owner);
        await SeedUserAsync(ada, $"{ada}@lilia.test", "Ada Lovelace");
        var doc = await SeedDocumentAsync(owner, "Shared paper");
        var groupId = await GroupAsync(owner, "Reviewers", ada);
        var client = CreateClientAs(owner);
        await client.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/users", new AddUserCollaboratorDto(ada, "viewer"));
        await client.PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/groups", new AddGroupCollaboratorDto(groupId, "viewer"));

        var person = await client.PutAsJsonAsync($"/api/documents/{doc.Id}/collaborators/users/{ada}", new UpdateCollaboratorRoleDto("editor"));
        var group = await client.PutAsJsonAsync($"/api/documents/{doc.Id}/collaborators/groups/{groupId}", new UpdateCollaboratorRoleDto("editor"));

        (await person.Content.ReadFromJsonAsync<UserCollaboratorDto>())!.Should().Match<UserCollaboratorDto>(u =>
            u.Name == "Ada Lovelace" && u.Email == $"{ada}@lilia.test" && u.RoleName == "editor");
        (await group.Content.ReadFromJsonAsync<GroupCollaboratorDto>())!.Should().Match<GroupCollaboratorDto>(g =>
            g.GroupName == "Reviewers" && g.MemberCount == 1 && g.RoleName == "editor");
    }

    [Fact]
    public async Task Comments_and_replies_carry_their_authors_names()
    {
        var owner = Id("owner"); var ada = Id("ada");
        await SeedUserAsync(owner, null, "Olive Owner");
        await SeedUserAsync(ada, $"{ada}@lilia.test", "Ada Lovelace");
        var doc = await SeedDocumentAsync(owner, "Commented paper");
        await CreateClientAs(owner).PostAsJsonAsync($"/api/documents/{doc.Id}/collaborators/users", new AddUserCollaboratorDto(ada, "viewer"));

        var created = await CreateClientAs(ada).PostAsJsonAsync($"/api/documents/{doc.Id}/comments", new CreateCommentDto(null, "A question."));
        var comment = (await created.Content.ReadFromJsonAsync<CommentDto>())!;
        comment.UserName.Should().Be("Ada Lovelace");
        var replied = await CreateClientAs(owner).PostAsJsonAsync($"/api/documents/{doc.Id}/comments/{comment.Id}/replies", new CreateReplyDto("An answer."));
        (await replied.Content.ReadFromJsonAsync<CommentReplyDto>())!.UserName.Should().Be("Olive Owner");

        var list = await CreateClientAs(owner).GetFromJsonAsync<List<CommentDto>>($"/api/documents/{doc.Id}/comments");

        var listed = list!.Should().ContainSingle().Subject;
        listed.UserName.Should().Be("Ada Lovelace");
        listed.Replies.Should().ContainSingle().Which.UserName.Should().Be("Olive Owner");
    }

    [Fact]
    public async Task A_comment_on_a_block_of_another_document_is_refused()
    {
        var owner = Id("owner");
        await SeedUserAsync(owner);
        var doc = await SeedDocumentAsync(owner, "Here");
        var other = await SeedDocumentAsync(owner, "Elsewhere");
        var here = await SeedBlockAsync(doc.Id);
        var there = await SeedBlockAsync(other.Id);
        var client = CreateClientAs(owner);

        (await client.PostAsJsonAsync($"/api/documents/{doc.Id}/comments", new CreateCommentDto(here.Id, "On this block.")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync($"/api/documents/{doc.Id}/comments", new CreateCommentDto(there.Id, "On theirs.")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_invite_names_its_document_and_inviter_until_the_document_is_trashed()
    {
        var owner = Id("owner");
        await SeedUserAsync(owner, null, "Olive Owner");
        var doc = await SeedDocumentAsync(owner, "Invited paper");
        var token = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            db.DocumentPendingInvites.Add(new DocumentPendingInvite
            {
                Id = token, DocumentId = doc.Id, Email = "someone@lilia.test", Role = "viewer", InvitedBy = owner,
                Status = "pending", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(14),
            });
            await db.SaveChangesAsync();
        }
        var anonymous = CreateAnonymousClient();

        var invite = await anonymous.GetFromJsonAsync<InviteResolveDto>($"/api/invites/{token}");
        invite!.Should().Match<InviteResolveDto>(i => i.DocumentTitle == "Invited paper" && i.InviterName == "Olive Owner");

        await using (var db = CreateDbContext())
        {
            (await db.Documents.FirstAsync(d => d.Id == doc.Id)).DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        (await anonymous.GetAsync($"/api/invites/{token}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
