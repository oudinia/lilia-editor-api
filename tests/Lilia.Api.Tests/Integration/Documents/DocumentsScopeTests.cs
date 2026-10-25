using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Documents;

/// <summary>
/// The documents list's <c>scope</c> (26 Oct 2026): <c>mine</c> lists what the user owns, <c>shared</c>
/// what someone else owns and shared with them, nothing lists both. Home's "Shared with you" read the
/// 100 newest documents and kept the shared ones, so a document shared with you below those never showed.
/// </summary>
[Collection("Integration")]
public class DocumentsScopeTests : IntegrationTestBase
{
    private const string OwnerId = "test_user_001";
    private const string ReaderId = "test_user_002";

    public DocumentsScopeTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static async Task<PaginatedResult<DocumentListDto>> List(HttpClient client, string query, int pageSize = 3) =>
        (await client.GetFromJsonAsync<PaginatedResult<DocumentListDto>>($"/api/documents?page=1&pageSize={pageSize}&{query}"))!;

    [Fact]
    public async Task Shared_lists_only_what_others_own_and_mine_only_what_you_own()
    {
        await SeedUserAsync(OwnerId);
        await SeedUserAsync(ReaderId);
        var shared = await SeedDocumentAsync(OwnerId, "Their paper");
        // More of the reader's own, newer, than a page holds.
        var own = new List<Guid>();
        for (var i = 0; i < 4; i++) own.Add((await SeedDocumentAsync(ReaderId, $"My paper {i}")).Id);
        await using (var db = CreateDbContext())
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "viewer")
                ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "viewer", Permissions = [Permissions.Read] }).Entity;
            db.DocumentCollaborators.Add(new DocumentCollaborator
            {
                Id = Guid.NewGuid(), DocumentId = shared.Id, UserId = ReaderId, RoleId = role.Id,
                InvitedBy = OwnerId, CreatedAt = DateTime.UtcNow,
            });
            await db.Documents.Where(d => d.Id == shared.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.UpdatedAt, DateTime.UtcNow.AddDays(-30)).SetProperty(d => d.LastOpenedAt, (DateTime?)null));
            await db.SaveChangesAsync();
        }
        using var reader = CreateClientAs(ReaderId);

        var all = await List(reader, "sortBy=updatedAt&sortDir=desc");
        all.TotalCount.Should().Be(5);
        all.Items.Select(d => d.Id).Should().NotContain(shared.Id, "a page of everything has only the newer, own ones");

        var sharedOnly = await List(reader, "sortBy=updatedAt&sortDir=desc&scope=shared");
        sharedOnly.Items.Select(d => d.Id).Should().Equal(shared.Id);
        sharedOnly.TotalCount.Should().Be(1);

        var mine = await List(reader, "scope=mine", pageSize: 10);
        mine.TotalCount.Should().Be(4);
        mine.Items.Select(d => d.Id).Should().NotContain(shared.Id);

        using var owner = CreateClientAs(OwnerId);
        (await List(owner, "scope=shared")).TotalCount.Should().Be(0, "the owner's own paper is not shared with them");
    }
}
