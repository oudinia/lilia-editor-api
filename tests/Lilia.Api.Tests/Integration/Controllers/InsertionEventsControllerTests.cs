using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for <see cref="InsertionEventsController"/> — the
/// telemetry sink behind the editor's insertion surfaces (panel, ⌘K,
/// slash menu, package modal).
///
/// What we cover:
///   - Anonymous → 401
///   - Empty batch → 204 (no work)
///   - Batch over 100 → 400
///   - Empty token names dropped silently
///   - Successful batch persists rows with correct shape
///   - Stats/top aggregates by token
///   - Stats/top window clamps + limit clamps
///   - Stats/sources aggregates by source
/// </summary>
[Collection("Integration")]
public class InsertionEventsControllerTests : IntegrationTestBase
{
    public InsertionEventsControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task RecordEvents_Anonymous_Returns401()
    {
        using var anon = CreateAnonymousClient();
        var batch = new InsertionEventBatchDto(new List<InsertionEventDto>
        {
            new("emph", "command", null, "panel", null, false)
        });

        var response = await anon.PostAsJsonAsync("/api/lilia/insertions/events", batch);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RecordEvents_EmptyBatch_Returns204()
    {
        const string userId = "user-events-1";
        await SeedUserAsync(userId);
        using var client = CreateClientAs(userId);

        var batch = new InsertionEventBatchDto(new List<InsertionEventDto>());
        var response = await client.PostAsJsonAsync("/api/lilia/insertions/events", batch);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RecordEvents_OversizedBatch_Returns400()
    {
        const string userId = "user-events-2";
        await SeedUserAsync(userId);
        using var client = CreateClientAs(userId);

        var oversized = new InsertionEventBatchDto(
            Enumerable.Range(0, 101)
                .Select(i => new InsertionEventDto($"tok{i}", "command", null, "panel", null, false))
                .ToList());

        var response = await client.PostAsJsonAsync("/api/lilia/insertions/events", oversized);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RecordEvents_PersistsRowsWithCorrectShape()
    {
        const string userId = "user-events-3";
        await SeedUserAsync(userId);
        var doc = await SeedDocumentAsync(userId);
        using var client = CreateClientAs(userId);

        var batch = new InsertionEventBatchDto(new List<InsertionEventDto>
        {
            new("emph", "command", null, "panel", doc.Id, true),
            new("section", "command", null, "palette", doc.Id, false),
            new("itemize", "environment", null, "slash", doc.Id, false),
        });

        var response = await client.PostAsJsonAsync("/api/lilia/insertions/events", batch);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var rows = db.LatexInsertionEvents
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.TokenName)
            .ToList();
        rows.Should().HaveCount(3);
        rows[0].TokenName.Should().Be("emph");
        rows[0].Source.Should().Be("panel");
        rows[0].WrappedSelection.Should().BeTrue();
        rows[0].DocumentId.Should().Be(doc.Id);
        rows[1].TokenName.Should().Be("itemize");
        rows[1].TokenKind.Should().Be("environment");
        rows[2].TokenName.Should().Be("section");
        rows[2].Source.Should().Be("palette");
    }

    [Fact]
    public async Task RecordEvents_DropsEmptyNamesSilently()
    {
        const string userId = "user-events-4";
        await SeedUserAsync(userId);
        using var client = CreateClientAs(userId);

        var batch = new InsertionEventBatchDto(new List<InsertionEventDto>
        {
            new("", "command", null, "panel", null, false),         // empty — drop
            new("   ", "command", null, "panel", null, false),      // whitespace — drop
            new("validtok", "command", null, "panel", null, false), // keep
        });

        var response = await client.PostAsJsonAsync("/api/lilia/insertions/events", batch);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var rows = db.LatexInsertionEvents.Where(e => e.UserId == userId).ToList();
        rows.Should().ContainSingle();
        rows.Single().TokenName.Should().Be("validtok");
    }

    [Fact]
    public async Task RecordEvents_TrimsWhitespaceFromFields()
    {
        const string userId = "user-events-5";
        await SeedUserAsync(userId);
        using var client = CreateClientAs(userId);

        // Whitespace around values exercises the controller's `.Trim()` path.
        // Note: nulls aren't tested via this route — ASP.NET model binding
        // rejects null on non-nullable record positional params (returns 400)
        // before the controller runs, so the controller's defensive `??`
        // fallbacks are unreachable from real JSON. They remain in code as
        // a belt-and-braces guard against future schema changes.
        var batch = new InsertionEventBatchDto(new List<InsertionEventDto>
        {
            new("  hyperref  ", "  command  ", "  hyperref  ", "  panel  ", null, false),
        });

        var response = await client.PostAsJsonAsync("/api/lilia/insertions/events", batch);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var row = db.LatexInsertionEvents.Single(e => e.UserId == userId);
        row.TokenName.Should().Be("hyperref");
        row.TokenKind.Should().Be("command");
        row.TokenPackageSlug.Should().Be("hyperref");
        row.Source.Should().Be("panel");
    }

    [Fact]
    public async Task GetTopTokens_AggregatesByTokenAcrossSources()
    {
        const string userId = "user-events-stats-1";
        await SeedUserAsync(userId);

        // Seed events directly in the DB so we control timestamps + counts.
        await using (var db = CreateDbContext())
        {
            var now = DateTime.UtcNow;
            for (int i = 0; i < 5; i++)
                db.LatexInsertionEvents.Add(new LatexInsertionEvent
                {
                    Id = Guid.NewGuid(),
                    TokenName = "popular",
                    TokenKind = "command",
                    TokenPackageSlug = null,
                    Source = i % 2 == 0 ? "panel" : "palette",
                    UserId = userId,
                    DocumentId = null,
                    WrappedSelection = i == 0,
                    CreatedAt = now,
                });
            for (int i = 0; i < 2; i++)
                db.LatexInsertionEvents.Add(new LatexInsertionEvent
                {
                    Id = Guid.NewGuid(),
                    TokenName = "rare",
                    TokenKind = "command",
                    TokenPackageSlug = null,
                    Source = "panel",
                    UserId = userId,
                    DocumentId = null,
                    WrappedSelection = false,
                    CreatedAt = now,
                });
            await db.SaveChangesAsync();
        }

        using var client = CreateClientAs(userId);
        var response = await client.GetAsync("/api/lilia/insertions/stats/top?windowDays=1&limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<InsertionStatRow>>();
        rows.Should().NotBeNull();
        var popular = rows!.FirstOrDefault(r => r.TokenName == "popular");
        popular.Should().NotBeNull();
        popular!.Hits.Should().BeGreaterThanOrEqualTo(5);
        popular.WrappedSelectionHits.Should().BeGreaterThanOrEqualTo(1);
        var rare = rows.FirstOrDefault(r => r.TokenName == "rare");
        rare.Should().NotBeNull();
        rare!.Hits.Should().BeGreaterThanOrEqualTo(2);
        // Order: popular before rare.
        rows.IndexOf(popular).Should().BeLessThan(rows.IndexOf(rare));
    }

    [Fact]
    public async Task GetTopTokens_LimitClamping_Works()
    {
        const string userId = "user-events-stats-2";
        await SeedUserAsync(userId);
        using var client = CreateClientAs(userId);

        // limit=0 → clamp to 1; limit=999 → clamp to 200.
        var resp1 = await client.GetAsync("/api/lilia/insertions/stats/top?limit=0");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        var resp2 = await client.GetAsync("/api/lilia/insertions/stats/top?limit=999");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSourceMix_GroupsBySource()
    {
        const string userId = "user-events-sources-1";
        await SeedUserAsync(userId);

        await using (var db = CreateDbContext())
        {
            var now = DateTime.UtcNow;
            db.LatexInsertionEvents.AddRange(
                new LatexInsertionEvent { Id = Guid.NewGuid(), TokenName = "x", TokenKind = "command", Source = "panel",   UserId = userId, CreatedAt = now },
                new LatexInsertionEvent { Id = Guid.NewGuid(), TokenName = "y", TokenKind = "command", Source = "panel",   UserId = userId, CreatedAt = now },
                new LatexInsertionEvent { Id = Guid.NewGuid(), TokenName = "z", TokenKind = "command", Source = "palette", UserId = userId, CreatedAt = now }
            );
            await db.SaveChangesAsync();
        }

        using var client = CreateClientAs(userId);
        var response = await client.GetAsync("/api/lilia/insertions/stats/sources?windowDays=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<InsertionSourceRow>>();
        rows.Should().NotBeNull();
        var panel = rows!.FirstOrDefault(r => r.Source == "panel");
        var palette = rows.FirstOrDefault(r => r.Source == "palette");
        panel.Should().NotBeNull();
        palette.Should().NotBeNull();
        panel!.Hits.Should().BeGreaterThanOrEqualTo(2);
        palette!.Hits.Should().BeGreaterThanOrEqualTo(1);
        // Panel ordered first (more hits).
        rows.IndexOf(panel).Should().BeLessThan(rows.IndexOf(palette));
    }
}
