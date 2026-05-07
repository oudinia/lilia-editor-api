using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the LILIA-136 block-group primitive: M:N
/// between blocks and named groups, scoped to a document and a single
/// dimension. First user is the layout dimension (multi-column regions).
/// </summary>
[Collection("Integration")]
public class BlockGroupsControllerTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";

    public BlockGroupsControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    /// <summary>
    /// Seeds a doc with N paragraph blocks owned by <paramref name="ownerId"/>.
    /// Idempotent on user creation — calling twice with the same ownerId
    /// in one test won't blow up on a duplicate-PK error.
    /// </summary>
    private async Task<(Lilia.Core.Entities.Document Doc, List<Lilia.Core.Entities.Block> Blocks)> SeedDocWithBlocks(int blockCount = 4, string ownerId = UserId)
    {
        await using (var db = CreateDbContext())
        {
            if (!await db.Users.AnyAsync(u => u.Id == ownerId))
            {
                db.Users.Add(new Lilia.Core.Entities.User
                {
                    Id = ownerId,
                    Email = $"{ownerId}@lilia.test",
                    Name = ownerId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
        }

        var doc = await SeedDocumentAsync(ownerId, "Test Doc");
        var blocks = new List<Lilia.Core.Entities.Block>();
        for (int i = 0; i < blockCount; i++)
        {
            var b = await SeedBlockAsync(doc.Id, "paragraph", $$"""{"text":"Block {{i}}"}""", i);
            blocks.Add(b);
        }
        return (doc, blocks);
    }

    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    // --- POST groups ---

    [Fact]
    public async Task CreateGroup_LayoutTwoColumn_PersistsWithMembers()
    {
        var (doc, blocks) = await SeedDocWithBlocks();

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "layout",
            attributes = Json("""{"columns":2}"""),
            name = "Body 2-col",
            memberBlockIds = new[] { blocks[1].Id, blocks[2].Id }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var group = await response.Content.ReadFromJsonAsync<BlockGroupDto>();
        group!.Dimension.Should().Be("layout");
        group.Attributes.GetProperty("columns").GetInt32().Should().Be(2);
        group.MemberBlockIds.Should().BeEquivalentTo(new[] { blocks[1].Id, blocks[2].Id });
        group.Name.Should().Be("Body 2-col");
    }

    [Fact]
    public async Task CreateGroup_RejectsConflictWithinSameDimension()
    {
        var (doc, blocks) = await SeedDocWithBlocks();

        // First group on blocks 1 & 2 in 'layout' dimension.
        var first = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "layout",
            attributes = Json("""{"columns":2}"""),
            memberBlockIds = new[] { blocks[1].Id, blocks[2].Id }
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second group claiming block 2 again in the same dimension → 409.
        var second = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "layout",
            attributes = Json("""{"columns":3}"""),
            memberBlockIds = new[] { blocks[2].Id, blocks[3].Id }
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateGroup_AllowsSameBlockInDifferentDimension()
    {
        var (doc, blocks) = await SeedDocWithBlocks();

        // Block 1 in a layout group.
        var layoutResponse = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "layout",
            attributes = Json("""{"columns":2}"""),
            memberBlockIds = new[] { blocks[1].Id }
        });
        layoutResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Same block also in a hypothetical 'review' group — should succeed.
        var reviewResponse = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "review",
            attributes = Json("""{"flag":"needs-citation"}"""),
            memberBlockIds = new[] { blocks[1].Id }
        });
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateGroup_Rejects_BlockFromOtherDocument()
    {
        var (docA, _) = await SeedDocWithBlocks(2);
        var (_, blocksB) = await SeedDocWithBlocks(2);

        var response = await Client.PostAsJsonAsync($"/api/documents/{docA.Id}/groups", new
        {
            dimension = "layout",
            attributes = Json("""{"columns":2}"""),
            memberBlockIds = new[] { blocksB[0].Id }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- GET groups ---

    [Fact]
    public async Task ListGroups_ReturnsAllForDocument()
    {
        var (doc, blocks) = await SeedDocWithBlocks();
        await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "layout",
            attributes = Json("""{"columns":2}"""),
            memberBlockIds = new[] { blocks[0].Id }
        });
        await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "review",
            attributes = Json("""{"flag":"todo"}"""),
            memberBlockIds = new[] { blocks[1].Id }
        });

        var response = await Client.GetAsync($"/api/documents/{doc.Id}/groups");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var groups = await response.Content.ReadFromJsonAsync<List<BlockGroupDto>>();
        groups.Should().HaveCount(2);
    }

    // --- PATCH group ---

    [Fact]
    public async Task UpdateGroup_ChangesAttributesAndMembership()
    {
        var (doc, blocks) = await SeedDocWithBlocks();
        var createResp = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "layout",
            attributes = Json("""{"columns":2}"""),
            memberBlockIds = new[] { blocks[1].Id }
        });
        var group = await createResp.Content.ReadFromJsonAsync<BlockGroupDto>();

        var response = await Client.PatchAsJsonAsync($"/api/documents/{doc.Id}/groups/{group!.Id}", new
        {
            attributes = Json("""{"columns":3}"""),
            memberBlockIds = new[] { blocks[1].Id, blocks[2].Id }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<BlockGroupDto>();
        updated!.Attributes.GetProperty("columns").GetInt32().Should().Be(3);
        updated.MemberBlockIds.Should().BeEquivalentTo(new[] { blocks[1].Id, blocks[2].Id });
    }

    // --- DELETE group ---

    [Fact]
    public async Task DeleteGroup_RemovesIt()
    {
        var (doc, blocks) = await SeedDocWithBlocks();
        var createResp = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "layout",
            attributes = Json("""{"columns":2}"""),
            memberBlockIds = new[] { blocks[0].Id }
        });
        var group = await createResp.Content.ReadFromJsonAsync<BlockGroupDto>();

        var deleteResp = await Client.DeleteAsync($"/api/documents/{doc.Id}/groups/{group!.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await Client.GetAsync($"/api/documents/{doc.Id}/groups/{group.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- ownership / 404 ---

    [Fact]
    public async Task CreateGroup_Returns404_WhenDocumentNotOwnedByCaller()
    {
        await SeedUserAsync(UserId);
        await SeedUserAsync("test_user_002");
        var doc = await SeedDocumentAsync("test_user_002", "Other's Doc");

        var response = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/groups", new
        {
            dimension = "layout",
            attributes = Json("""{"columns":2}"""),
            memberBlockIds = Array.Empty<Guid>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
