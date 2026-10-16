using System.Net;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Applying a review hint makes the change it offers (16 Oct): before, Apply only marked the hint
/// applied and "looks like a heading" left the paragraph a paragraph.
/// </summary>
[Collection("Integration")]
public class ImportHintApplyTests : IntegrationTestBase
{
    private const string OwnerId = "test_user_hint_owner";

    public ImportHintApplyTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedSessionAsync()
    {
        await SeedUserAsync(OwnerId);
        var sessionId = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId, OwnerId = OwnerId, DocumentTitle = "hints.tex", SourceFormat = "tex",
            Status = "pending_review", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        var blocks = new (string id, string type, string json)[]
        {
            ("b-0", "heading", "{\"text\":\"Introduction\",\"level\":1}"),
            ("b-1", "paragraph", "{\"text\":\"Key Findings\"}"),
            ("b-2", "list", "{\"items\":[\"One\",\"Two\"]}"),
            ("b-3", "tableOfContents", "{}"),
        };
        for (var i = 0; i < blocks.Length; i++)
            db.ImportBlockReviews.Add(new ImportBlockReview
            {
                Id = Guid.NewGuid(), SessionId = sessionId, BlockId = blocks[i].id, BlockIndex = i, SortOrder = i,
                OriginalType = blocks[i].type, OriginalContent = JsonDocument.Parse(blocks[i].json), Status = "pending",
            });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private async Task<Guid> FindingAsync(Guid sessionId, string kind)
    {
        await using var db = CreateDbContext();
        return (await db.ImportStructuralFindings.FirstAsync(f => f.SessionId == sessionId && f.Kind == kind)).Id;
    }

    [Fact]
    public async Task Applying_looks_like_a_heading_makes_the_paragraph_a_heading()
    {
        var sessionId = await SeedSessionAsync();
        var client = CreateClientAs(OwnerId);
        (await client.PostAsync($"/api/lilia/import-review/sessions/{sessionId}/hints/compute", null)).EnsureSuccessStatusCode();
        var hint = await FindingAsync(sessionId, "paragraph_as_heading");

        (await client.PostAsync($"/api/lilia/import-review/sessions/{sessionId}/hints/{hint}/apply", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var review = await db.ImportBlockReviews.AsNoTracking().FirstAsync(b => b.SessionId == sessionId && b.BlockId == "b-1");
        review.CurrentType.Should().Be("heading");
        review.CurrentContent!.RootElement.GetProperty("text").GetString().Should().Be("Key Findings");
        review.CurrentContent.RootElement.GetProperty("level").GetInt32().Should().Be(1);
        review.Status.Should().Be("edited");
        (await db.ImportStructuralFindings.AsNoTracking().FirstAsync(f => f.Id == hint)).Status.Should().Be("applied");
    }

    [Fact]
    public async Task Applying_an_empty_table_of_contents_hint_rejects_that_block_and_a_second_apply_changes_nothing()
    {
        var sessionId = await SeedSessionAsync();
        var client = CreateClientAs(OwnerId);
        (await client.PostAsync($"/api/lilia/import-review/sessions/{sessionId}/hints/compute", null)).EnsureSuccessStatusCode();
        var hint = await FindingAsync(sessionId, "spurious_toc");

        (await client.PostAsync($"/api/lilia/import-review/sessions/{sessionId}/hints/{hint}/apply", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/lilia/import-review/sessions/{sessionId}/hints/{hint}/apply", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        (await db.ImportBlockReviews.AsNoTracking().FirstAsync(b => b.SessionId == sessionId && b.BlockId == "b-3")).Status.Should().Be("rejected");
    }
}
