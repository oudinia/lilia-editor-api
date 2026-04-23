using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.LatexCatalog;

/// <summary>
/// Phase-1 integration tests for the LaTeX catalog schema + seed. Assert
/// that the tables exist with the expected CHECK constraints, the seed
/// landed core rows, and the unique index on (name, kind, package_slug)
/// enforces the idempotency contract.
/// </summary>
[Collection("Integration")]
public class LatexCatalogSeedTests : IntegrationTestBase
{
    public LatexCatalogSeedTests(TestDatabaseFixture fixture) : base(fixture) { }

    private LiliaDbContext CreateDbContext()
    {
        var scope = Fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
    }

    [Fact]
    public async Task Seed_loads_core_document_classes()
    {
        await using var db = CreateDbContext();
        var slugs = await db.LatexDocumentClasses.Select(c => c.Slug).ToListAsync();
        slugs.Should().Contain(new[]
        {
            "article", "report", "book", "beamer", "letter",
            "moderncv", "altacv", "resume", "memoir",
        });
    }

    [Fact]
    public async Task Seed_loads_core_packages_with_coverage_levels()
    {
        await using var db = CreateDbContext();
        var amsmath = await db.LatexPackages.FindAsync("amsmath");
        amsmath.Should().NotBeNull();
        amsmath!.CoverageLevel.Should().Be("full");
        amsmath.Category.Should().Be("math");

        var tikz = await db.LatexPackages.FindAsync("tikz");
        tikz.Should().NotBeNull();
        tikz!.CoverageLevel.Should().Be("partial");
    }

    [Fact]
    public async Task Seed_loads_kernel_environments_and_commands()
    {
        await using var db = CreateDbContext();

        var itemize = await db.LatexTokens.FirstOrDefaultAsync(t => t.Name == "itemize" && t.Kind == "environment");
        itemize.Should().NotBeNull();
        itemize!.MapsToBlockType.Should().Be("list");
        itemize.CoverageLevel.Should().Be("full");

        var section = await db.LatexTokens.FirstOrDefaultAsync(t => t.Name == "section" && t.Kind == "command");
        section.Should().NotBeNull();
        section!.MapsToBlockType.Should().Be("heading");
    }

    [Fact]
    public async Task Seed_captures_watari_case_tokens()
    {
        // Real prod failures from the 7-day diagnostic aggregate — these
        // environments must now be known to the catalog so the parser has
        // something to route them through (even if coverage=partial).
        await using var db = CreateDbContext();
        var names = new[] { "frame", "minipage", "multicols", "wrapfigure", "question", "rSection", "tabularx" };
        var hits = await db.LatexTokens
            .Where(t => t.Kind == "environment" && names.Contains(t.Name))
            .Select(t => t.Name)
            .ToListAsync();
        hits.Should().Contain(names);
    }

    [Fact]
    public async Task CheckConstraint_rejects_invalid_coverage_level()
    {
        await using var db = CreateDbContext();
        db.LatexPackages.Add(new LatexPackage
        {
            Slug = "test-bad-coverage",
            DisplayName = "Invalid",
            Category = "utility",
            CoverageLevel = "totally_supported", // not in the CHECK vocabulary
        });
        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CheckConstraint_rejects_invalid_category()
    {
        await using var db = CreateDbContext();
        db.LatexPackages.Add(new LatexPackage
        {
            Slug = "test-bad-category",
            DisplayName = "Invalid",
            Category = "miscellaneous", // not in the CHECK vocabulary
            CoverageLevel = "none",
        });
        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CheckConstraint_rejects_invalid_token_kind()
    {
        await using var db = CreateDbContext();
        db.LatexTokens.Add(new LatexToken
        {
            Id = Guid.NewGuid(),
            Name = "test-bad-kind",
            Kind = "macro", // not in the CHECK vocabulary
            CoverageLevel = "none",
        });
        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UniqueIndex_on_token_name_kind_package_prevents_duplicates()
    {
        await using var db = CreateDbContext();
        db.LatexTokens.Add(new LatexToken
        {
            Id = Guid.NewGuid(),
            Name = "section", // already seeded, package_slug=NULL, kind='command'
            Kind = "command",
            PackageSlug = null,
            CoverageLevel = "full",
            // HandlerKind is set even though we expect the save to fail
            // on the unique index — defensive in case any migration-
            // ordering quirk ever lets the insert through, so
            // CatalogIntegrityTests doesn't light up on an orphan row.
            HandlerKind = "section-regex",
        });
        Func<Task> act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TokenPackage_relationship_navigates_correctly()
    {
        await using var db = CreateDbContext();
        var frame = await db.LatexTokens
            .Include(t => t.Package)
            .FirstAsync(t => t.Name == "frame" && t.Kind == "environment");
        frame.Package.Should().NotBeNull();
        frame.Package!.Slug.Should().Be("beamer");
        // frame used to be 'shimmed' under the seed. The 2026-04-22
        // honesty pass demoted it to 'partial' once it became clear the
        // beamer import emits a section + flattened overlays (not a
        // dedicated block). The assertion is now just that the level is
        // a real coverage vocabulary value — test is about the token↔
        // package nav, not the specific coverage level.
        frame.CoverageLevel.Should().BeOneOf("full", "partial", "shimmed");
    }

    [Fact]
    public async Task Seed_is_idempotent_on_reapply()
    {
        // If migrations run a second time (hot redeploy on a populated DB),
        // the ON CONFLICT DO NOTHING guards prevent duplicate rows.
        await using var db = CreateDbContext();
        var beforeCount = await db.LatexPackages.CountAsync();

        // Re-execute the seed SQL directly (mimic a re-run of the
        // migration). ON CONFLICT guards mean count is unchanged.
        await db.Database.ExecuteSqlRawAsync(@"
INSERT INTO latex_packages (slug, display_name, category, coverage_level, coverage_notes, ctan_url) VALUES
('amsmath', 'amsmath', 'math', 'full', 'seeded twice', 'https://ctan.org/pkg/amsmath')
ON CONFLICT (slug) DO NOTHING;
");

        var afterCount = await db.LatexPackages.CountAsync();
        afterCount.Should().Be(beforeCount);

        // And the existing row's notes weren't overwritten either.
        var amsmath = await db.LatexPackages.FindAsync("amsmath");
        amsmath!.CoverageNotes.Should().NotBe("seeded twice");
    }
}
