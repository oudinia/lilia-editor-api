using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Schema-level smoke tests for the FT-IMP-001 mirror tables
/// (`rev_documents`, `rev_blocks`). The dual-write from the parser
/// into these tables is a pending follow-up
/// (`lilia-docs/specs/import-ui-handoff-2026-04-24.md` §5 #1) — when
/// that lands, the production path will round-trip ImportBlockReview-
/// shaped data through these rows. These tests pin the round-trip
/// shape today so the future dual-write can't silently land against
/// a schema that doesn't accept it.
///
/// Deliberately scoped to schema / FK behaviour — no parser
/// integration, no service-layer calls. When the dual-write ships,
/// it will add its own end-to-end tests alongside these.
/// </summary>
[Collection("Integration")]
public class RevMirrorSchemaTests : IntegrationTestBase
{
    public RevMirrorSchemaTests(TestDatabaseFixture fixture) : base(fixture) { }

    private new LiliaDbContext CreateDbContext() =>
        Fixture.Factory.Services.CreateScope()
            .ServiceProvider.GetRequiredService<LiliaDbContext>();

    /// <summary>
    /// Seed an ImportReviewSession so the rev_documents.instance_id FK
    /// has something to point at. Mirrors the ownership shape of
    /// ImportFlowTests' helper without the block-review noise.
    /// </summary>
    private async Task<Guid> SeedSessionAsync(string userId)
    {
        await SeedUserAsync(userId);
        await using var db = CreateDbContext();

        var sessionId = Guid.NewGuid();
        db.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            OwnerId = userId,
            DocumentTitle = "rev-mirror-test.tex",
            SourceFormat = "tex",
            Status = "pending_review",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    [Fact]
    public async Task RevDocument_RoundTrips_WithAllColumns()
    {
        var sessionId = await SeedSessionAsync("test_user_rev_doc");

        var revDocId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            db.RevDocuments.Add(new RevDocument
            {
                Id = revDocId,
                InstanceId = sessionId,
                Title = "Mirror Title",
                Description = "Roundtrip check",
                SourceFormat = "tex",
                Metadata = JsonDocument.Parse("{\"k\":\"v\"}"),
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            var loaded = await db.RevDocuments.SingleAsync(d => d.Id == revDocId);
            loaded.InstanceId.Should().Be(sessionId);
            loaded.Title.Should().Be("Mirror Title");
            loaded.Description.Should().Be("Roundtrip check");
            loaded.SourceFormat.Should().Be("tex");
            loaded.Metadata.RootElement.GetProperty("k").GetString().Should().Be("v");
        }
    }

    [Fact]
    public async Task RevBlock_RoundTrips_WithAllColumns()
    {
        var sessionId = await SeedSessionAsync("test_user_rev_block");
        var revDocId = Guid.NewGuid();

        await using (var db = CreateDbContext())
        {
            db.RevDocuments.Add(new RevDocument
            {
                Id = revDocId,
                InstanceId = sessionId,
                Title = "T",
                SourceFormat = "tex",
            });
            await db.SaveChangesAsync();
        }

        var revBlockId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            db.RevBlocks.Add(new RevBlock
            {
                Id = revBlockId,
                RevDocumentId = revDocId,
                Type = "paragraph",
                Content = JsonDocument.Parse("{\"text\":\"Hello\"}"),
                Status = "kept",
                SortOrder = 0,
                Depth = 0,
                Confidence = 95,
                Warnings = JsonDocument.Parse("[]"),
                Metadata = JsonDocument.Parse("{}"),
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            var loaded = await db.RevBlocks.SingleAsync(b => b.Id == revBlockId);
            loaded.RevDocumentId.Should().Be(revDocId);
            loaded.Type.Should().Be("paragraph");
            loaded.Status.Should().Be("kept");
            loaded.Confidence.Should().Be(95);
            loaded.Content.RootElement.GetProperty("text").GetString().Should().Be("Hello");
        }
    }

    [Fact]
    public async Task RevBlocks_CascadeDelete_WithRevDocument()
    {
        // rev_blocks.rev_document_id → rev_documents.id uses Cascade
        // (RevBlockConfiguration.OnDelete(DeleteBehavior.Cascade)).
        // Verify that deleting a rev_document wipes its blocks — this
        // is load-bearing for the retention / cleanup job that will
        // clear old import instances.
        var sessionId = await SeedSessionAsync("test_user_rev_cascade");
        var revDocId = Guid.NewGuid();

        await using (var db = CreateDbContext())
        {
            db.RevDocuments.Add(new RevDocument
            {
                Id = revDocId,
                InstanceId = sessionId,
                Title = "Cascade",
                SourceFormat = "tex",
            });
            db.RevBlocks.Add(new RevBlock
            {
                RevDocumentId = revDocId,
                Type = "paragraph",
                Content = JsonDocument.Parse("{\"text\":\"one\"}"),
                SortOrder = 0,
            });
            db.RevBlocks.Add(new RevBlock
            {
                RevDocumentId = revDocId,
                Type = "paragraph",
                Content = JsonDocument.Parse("{\"text\":\"two\"}"),
                SortOrder = 1,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            (await db.RevBlocks.CountAsync(b => b.RevDocumentId == revDocId))
                .Should().Be(2);

            var revDoc = await db.RevDocuments.SingleAsync(d => d.Id == revDocId);
            db.RevDocuments.Remove(revDoc);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            (await db.RevBlocks.CountAsync(b => b.RevDocumentId == revDocId))
                .Should().Be(0, "cascade delete must drop the children");
            (await db.RevDocuments.CountAsync(d => d.Id == revDocId))
                .Should().Be(0);
        }
    }

    [Fact]
    public async Task RevDocuments_CascadeDelete_WithSession()
    {
        // rev_documents.instance_id → import_review_sessions.id uses
        // Cascade (RevDocumentConfiguration). Instance purge must
        // wipe the whole mirror for that instance.
        var sessionId = await SeedSessionAsync("test_user_rev_session_cascade");
        var revDocId = Guid.NewGuid();

        await using (var db = CreateDbContext())
        {
            db.RevDocuments.Add(new RevDocument
            {
                Id = revDocId,
                InstanceId = sessionId,
                Title = "Session cascade",
                SourceFormat = "tex",
            });
            db.RevBlocks.Add(new RevBlock
            {
                RevDocumentId = revDocId,
                Type = "heading",
                Content = JsonDocument.Parse("{\"text\":\"H\",\"level\":1}"),
                SortOrder = 0,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            var session = await db.ImportReviewSessions.SingleAsync(s => s.Id == sessionId);
            db.ImportReviewSessions.Remove(session);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            (await db.RevDocuments.CountAsync(d => d.Id == revDocId))
                .Should().Be(0, "session cascade must drop the rev-document");
            (await db.RevBlocks.CountAsync(b => b.RevDocumentId == revDocId))
                .Should().Be(0, "session cascade must drop rev-document's blocks");
        }
    }
}
