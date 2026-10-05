using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Infrastructure;

/// <summary>
/// One document and the four people who matter to its access checks: its owner, an editor,
/// a viewer, and a stranger. For "this route must check who is asking" tests (the cloud
/// session's authz audit, 3 Oct 2026): a route that never checks answers the stranger and
/// lets the viewer write.
/// </summary>
public abstract class FourCallersTestBase : IntegrationTestBase
{
    protected const string OwnerId = "authz_owner";
    protected const string EditorId = "authz_editor";
    protected const string ViewerId = "authz_viewer";
    protected const string StrangerId = "authz_stranger";

    protected FourCallersTestBase(TestDatabaseFixture fixture) : base(fixture) { }

    protected sealed record Seeded(Guid DocumentId, Guid BlockId);

    protected async Task<Seeded> SeedSharedDocumentAsync(string title = "Authz paper")
    {
        foreach (var u in new[] { OwnerId, EditorId, ViewerId, StrangerId }) await SeedUserAsync(u);
        var doc = await SeedDocumentAsync(OwnerId, title);
        var block = await SeedBlockAsync(doc.Id, "paragraph", "{\"text\":\"Private text\"}", 0);
        await using var db = CreateDbContext();
        foreach (var (user, roleName, perms) in new[]
        {
            (EditorId, RoleNames.Editor, new[] { Permissions.Read, Permissions.Write }),
            (ViewerId, RoleNames.Viewer, new[] { Permissions.Read }),
        })
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == roleName)
                ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = roleName, Permissions = perms.ToList() }).Entity;
            db.DocumentCollaborators.Add(new DocumentCollaborator
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = user, RoleId = role.Id,
                InvitedBy = OwnerId, CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        return new Seeded(doc.Id, block.Id);
    }

    protected HttpClient As(string userId) => CreateClientAs(userId);
}
