using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Versions;

/// <summary>
/// A restore advances the document's version, like every other write to its
/// blocks — so an editor that saved before the restore gets a 409 and pulls
/// the restored state, instead of writing its older copy back over it.
///
/// <para>Found 27 Sep by the stability pass: Ask Lilia's Undo restores the
/// snapshot taken before its edit, and the open editor's next save (84 ms
/// later, still carrying the edit) was accepted, because the restore had
/// left the version where it was. The edit came back. History → Restore had
/// the same hole with any open tab.</para>
/// </summary>
[Collection("Integration")]
public class RestoreAdvancesVersionTests : IntegrationTestBase
{
    private const string OwnerId = "test_user_001";

    public RestoreAdvancesVersionTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<int> VersionOf(Guid id)
    {
        await using var db = CreateDbContext();
        return (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == id)).Version;
    }

    private static object Row(Guid id, string text, int order) =>
        new { id, type = "paragraph", content = new { text }, sortOrder = order };

    [Fact]
    public async Task A_save_from_before_a_restore_is_refused_and_the_restore_stands()
    {
        await SeedUserAsync(OwnerId);
        var doc = await SeedDocumentAsync(OwnerId, "Restore");
        var original = Guid.NewGuid();
        (await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks/batch",
            new { blocks = new[] { Row(original, "Original prose.", 0) } })).EnsureSuccessStatusCode();

        var snapshot = await (await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/versions",
            new CreateVersionDto("Before Ask Lilia"))).Content.ReadFromJsonAsync<VersionDto>();

        // The edit being undone (Ask Lilia writes through the block API).
        var added = Guid.NewGuid();
        var v = await VersionOf(doc.Id);
        (await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks/batch", new
        {
            blocks = new[] { Row(original, "Original prose.", 0), Row(added, "Written by Lilia.", 1) },
            expectedVersion = v,
        })).EnsureSuccessStatusCode();
        var beforeRestore = await VersionOf(doc.Id);

        (await Client.PostAsync($"/api/documents/{doc.Id}/versions/{snapshot!.Id}/restore", null)).EnsureSuccessStatusCode();
        (await VersionOf(doc.Id)).Should().BeGreaterThan(beforeRestore, "a restore rewrites the blocks");

        // The open editor saves what it had before it heard of the restore.
        var stale = await Client.PostAsJsonAsync($"/api/documents/{doc.Id}/blocks/batch", new
        {
            blocks = new[] { Row(original, "Original prose.", 0), Row(added, "Written by Lilia.", 1) },
            expectedVersion = beforeRestore,
        });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = CreateDbContext();
        var texts = await db.Blocks.Where(b => b.DocumentId == doc.Id)
            .Select(b => b.Content.RootElement.GetProperty("text").GetString()).ToListAsync();
        texts.Should().Equal("Original prose.");
    }
}
