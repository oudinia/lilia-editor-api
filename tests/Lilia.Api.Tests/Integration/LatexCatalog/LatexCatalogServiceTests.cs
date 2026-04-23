using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.LatexCatalog;

/// <summary>
/// Phase-2 tests for the catalog service — exercises lookup, auto-insert
/// of unknown tokens, bulk usage upsert, and the coverage aggregate.
/// </summary>
[Collection("Integration")]
public class LatexCatalogServiceTests : IntegrationTestBase
{
    public LatexCatalogServiceTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<ILatexCatalogService> GetPreloadedCatalog()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILatexCatalogService>();
        if (svc is LatexCatalogService concrete)
        {
            await concrete.PreloadAsync();
        }
        return svc;
    }

    [Fact]
    public async Task LookupToken_returns_seeded_kernel_command()
    {
        var catalog = await GetPreloadedCatalog();
        var section = catalog.LookupToken("section", "command");
        section.Should().NotBeNull();
        section!.MapsToBlockType.Should().Be("heading");
        section.CoverageLevel.Should().Be("full");
    }

    [Fact]
    public async Task LookupToken_returns_null_for_unknown()
    {
        var catalog = await GetPreloadedCatalog();
        catalog.LookupToken("definitelyNotALatexCommand", "command").Should().BeNull();
    }

    [Fact]
    public async Task LookupToken_falls_back_from_package_to_kernel()
    {
        // Given a command that's seeded kernel-scoped only (e.g. section),
        // a package-scoped lookup should still find it.
        var catalog = await GetPreloadedCatalog();
        var section = catalog.LookupToken("section", "command", packageSlug: "graphicx");
        section.Should().NotBeNull();
        section!.PackageSlug.Should().BeNull();
    }

    [Fact]
    public async Task ReportUnknownAsync_inserts_and_is_idempotent()
    {
        var catalog = await GetPreloadedCatalog();
        var name = $"unknownCmd_{Guid.NewGuid():N}".Substring(0, 40);

        var id1 = await catalog.ReportUnknownAsync(name, "command", null);
        var id2 = await catalog.ReportUnknownAsync(name, "command", null);
        id1.Should().Be(id2);

        // Verify persistence + coverage_level
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var row = await db.LatexTokens.FirstAsync(t => t.Id == id1);
        row.Name.Should().Be(name);
        row.CoverageLevel.Should().Be("unsupported");

        // Next lookup hits the cache — no new row created.
        var cached = catalog.LookupToken(name, "command");
        cached.Should().NotBeNull();
        cached!.Id.Should().Be(id1);
    }

    [Fact]
    public async Task RecordUsageAsync_upserts_and_increments_counts()
    {
        var catalog = await GetPreloadedCatalog();

        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();

        // Create a dummy session row; usage rows FK-reference it.
        var userId = await EnsureTestUserAsync(db, "catalog-usage-test");
        var sessionId = Guid.NewGuid();
        db.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            OwnerId = userId,
            DocumentTitle = "usage test",
            Status = "in_progress",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var sectionId = catalog.LookupToken("section", "command")!.Id;
        var itemizeId = catalog.LookupToken("itemize", "environment")!.Id;

        await catalog.RecordUsageAsync(sessionId, new[]
        {
            new CatalogTokenUsage(sectionId, 3),
            new CatalogTokenUsage(itemizeId, 2),
        });

        // Re-apply — counts should sum, not duplicate.
        await catalog.RecordUsageAsync(sessionId, new[]
        {
            new CatalogTokenUsage(sectionId, 1),
        });

        var sectionUsage = await db.LatexTokenUsages
            .FirstAsync(u => u.SessionId == sessionId && u.TokenId == sectionId);
        sectionUsage.Count.Should().Be(4);

        var itemizeUsage = await db.LatexTokenUsages
            .FirstAsync(u => u.SessionId == sessionId && u.TokenId == itemizeId);
        itemizeUsage.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetCoverageReportAsync_groups_by_coverage_level()
    {
        var catalog = await GetPreloadedCatalog();

        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var userId = await EnsureTestUserAsync(db, "catalog-report-test");

        var sessionId = Guid.NewGuid();
        db.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            OwnerId = userId,
            DocumentTitle = "report test",
            Status = "in_progress",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var fullToken = catalog.LookupToken("section", "command")!.Id;
        var partialToken = catalog.LookupToken("minipage", "environment")!.Id;
        // \cvitem (moderncv) is shimmed at the seed layer — still shimmed
        // after today's honesty passes. \frame used to be shimmed but
        // was demoted to partial when we clarified that beamer imports
        // flatten overlays rather than rewriting via a class shim.
        var shimmedToken = catalog.LookupToken("cvitem", "command", "moderncv")!.Id;

        await catalog.RecordUsageAsync(sessionId, new[]
        {
            new CatalogTokenUsage(fullToken, 5),
            new CatalogTokenUsage(partialToken, 3),
            new CatalogTokenUsage(shimmedToken, 1),
        });

        var report = await catalog.GetCoverageReportAsync(TimeSpan.FromDays(1));
        report.FullCount.Should().BeGreaterThanOrEqualTo(5);
        report.PartialCount.Should().BeGreaterThanOrEqualTo(3);
        report.ShimmedCount.Should().BeGreaterThanOrEqualTo(1);
    }

    private static async Task<string> EnsureTestUserAsync(LiliaDbContext db, string email)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == $"{email}@test.local");
        if (existing != null) return existing.Id;
        var u = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = $"{email}@test.local",
            Name = email,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }
}
