using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Documents;

/// <summary>
/// A document put in a team is open to the team's members, at their team
/// role (e2e plan, Teams: "team documents visible to members; role change
/// limits editing").
///
/// Found 26 Sep: the team page listed the document to a member, and opening
/// it was a 404 — access only looked at direct collaborators and the
/// document's groups, never at the team the document is in.
///
/// Team access stops at editing: deleting the document, managing its
/// sharing and transferring it stay with its owner.
/// </summary>
[Collection("Integration")]
public class TeamDocumentAccessTests : IntegrationTestBase
{
    private const string OwnerId = "team_doc_owner";
    private const string MemberId = "team_doc_member";
    private const string StrangerId = "team_doc_stranger";

    public TeamDocumentAccessTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<(Guid TeamId, Guid DocId, Guid BlockId)> TeamDocumentAsync(string memberRole)
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(MemberId);
        await SeedUserAsync(StrangerId);
        var doc = await SeedDocumentAsync(OwnerId, "Team paper");
        await using var db = CreateDbContext();
        var role = async (string name) => (await db.Roles.FirstAsync(r => r.Name == name)).Id;
        var team = new Team
        {
            Id = Guid.NewGuid(), Name = "Lab", TeamCode = "lab-" + Guid.NewGuid().ToString("N")[..8],
            Slug = "lab-" + Guid.NewGuid().ToString("N")[..8], OwnerId = OwnerId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var group = new Group { Id = Guid.NewGuid(), TeamId = team.Id, Name = "Everyone", IsDefault = true, CreatedAt = DateTime.UtcNow };
        group.Members.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = OwnerId, RoleId = await role("owner"), CreatedAt = DateTime.UtcNow });
        group.Members.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = MemberId, RoleId = await role(memberRole), CreatedAt = DateTime.UtcNow });
        team.Groups.Add(group);
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        (await db.Documents.FirstAsync(d => d.Id == doc.Id)).TeamId = team.Id;
        var block = new Block
        {
            Id = Guid.NewGuid(), DocumentId = doc.Id, Type = "paragraph", SortOrder = 1,
            Content = System.Text.Json.JsonDocument.Parse("{\"text\":\"Owner's words.\"}"),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Blocks.Add(block);
        await db.SaveChangesAsync();
        return (team.Id, doc.Id, block.Id);
    }

    private static async Task<string?> ListedRole(HttpClient client, Guid docId)
    {
        var page = await client.GetFromJsonAsync<PaginatedResult<DocumentListDto>>("/api/documents?page=1&pageSize=100");
        return page!.Items.FirstOrDefault(d => d.Id == docId) is { } d ? d.Role ?? "(none)" : null;
    }

    private static Task<HttpResponseMessage> Edit(HttpClient client, Guid docId, Guid blockId, string text) =>
        client.PutAsJsonAsync($"/api/documents/{docId}/blocks/{blockId}", new { content = new { text } });

    [Fact]
    public async Task An_editor_in_the_team_opens_and_edits_the_teams_document()
    {
        var (_, docId, blockId) = await TeamDocumentAsync("editor");
        using var member = CreateClientAs(MemberId);

        var open = await member.GetAsync($"/api/documents/{docId}");
        open.StatusCode.Should().Be(HttpStatusCode.OK, "the team page lists it to them, so it opens");
        (await open.Content.ReadFromJsonAsync<DocumentDto>())!.Role.Should().Be("editor");
        (await ListedRole(member, docId)).Should().Be("editor", "it is in their documents, as an editor");

        (await Edit(member, docId, blockId, "An editor's words.")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await member.DeleteAsync($"/api/documents/{docId}")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "team access stops at editing: deleting is the owner's");
    }

    [Fact]
    public async Task A_viewer_in_the_team_reads_it_and_cannot_edit()
    {
        var (_, docId, blockId) = await TeamDocumentAsync("viewer");
        using var member = CreateClientAs(MemberId);

        var open = await member.GetAsync($"/api/documents/{docId}");
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        (await open.Content.ReadFromJsonAsync<DocumentDto>())!.Role.Should().Be("viewer");
        (await ListedRole(member, docId)).Should().Be("viewer");

        (await Edit(member, docId, blockId, "A viewer's words.")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Leaving_the_team_or_never_being_in_it_means_no_access()
    {
        var (teamId, docId, _) = await TeamDocumentAsync("editor");
        using var member = CreateClientAs(MemberId);
        using var stranger = CreateClientAs(StrangerId);

        (await stranger.GetAsync($"/api/documents/{docId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListedRole(stranger, docId)).Should().BeNull();

        (await member.GetAsync($"/api/documents/{docId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        // Leave the team (the row the owner's Remove deletes; straight to the
        // database so no removal event is published from a test).
        await using (var db = CreateDbContext())
        {
            await db.GroupMembers.Where(gm => gm.Group.TeamId == teamId && gm.UserId == MemberId).ExecuteDeleteAsync();
        }
        (await member.GetAsync($"/api/documents/{docId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListedRole(member, docId)).Should().BeNull("it left their documents with them");
    }
}
