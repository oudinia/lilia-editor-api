using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Documents;

/// <summary>
/// "Remove from my documents" (Olivia, documents-actions reply, 26 Sep): a
/// non-owner takes a shared document off their own list. Nothing is deleted,
/// no one else's list changes, the share still opens it, and Undo brings it
/// back. The owner trashes instead.
/// </summary>
[Collection("Integration")]
public class RemoveFromMyDocumentsTests : IntegrationTestBase
{
    private const string OwnerId = "test_user_001";
    private const string ReaderId = "test_user_002";
    private const string OtherReaderId = "test_user_003";

    public RemoveFromMyDocumentsTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SharedDocumentAsync()
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(ReaderId);
        await SeedUserAsync(OtherReaderId);
        var doc = await SeedDocumentAsync(OwnerId, "Shared paper");
        await using var db = CreateDbContext();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "viewer")
            ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "viewer", Permissions = [Permissions.Read] }).Entity;
        foreach (var user in new[] { ReaderId, OtherReaderId })
        {
            db.DocumentCollaborators.Add(new DocumentCollaborator
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = user, RoleId = role.Id,
                InvitedBy = OwnerId, CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private static async Task<List<Guid>> Listed(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<PaginatedResult<DocumentListDto>>("/api/documents?page=1&pageSize=100");
        return page!.Items.Select(d => d.Id).ToList();
    }

    [Fact]
    public async Task A_reader_removes_it_from_their_list_only_and_undo_brings_it_back()
    {
        var id = await SharedDocumentAsync();
        using var reader = CreateClientAs(ReaderId);
        using var other = CreateClientAs(OtherReaderId);
        (await Listed(reader)).Should().Contain(id);

        (await reader.PostAsync($"/api/documents/{id}/hide", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Listed(reader)).Should().NotContain(id, "it is off their list");
        (await Listed(other)).Should().Contain(id, "nobody else's list changes");
        (await Listed(Client)).Should().Contain(id, "nor the owner's");
        (await reader.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.OK, "the share still opens it");

        (await reader.DeleteAsync($"/api/documents/{id}/hide")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Listed(reader)).Should().Contain(id, "Undo brings it back");
    }

    [Fact]
    public async Task The_owner_cannot_hide_their_own_document_and_a_stranger_cannot_hide_anything()
    {
        var id = await SharedDocumentAsync();
        await SeedUserAsync("test_user_004");
        using var stranger = CreateClientAs("test_user_004");

        (await Client.PostAsync($"/api/documents/{id}/hide", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.PostAsync($"/api/documents/{id}/hide", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Listed(Client)).Should().Contain(id);
    }
}
