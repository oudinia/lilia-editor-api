using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.LatexCatalog;

/// <summary>
/// Stage-2 catalog integrity tests — enforce the contract between
/// latex_tokens and LatexParser.cs. Every row claiming a non-trivial
/// coverage level must declare which parser-handler routes it.
///
/// If this test fails, the catalog is lying about what Lilia handles.
/// Either (a) fix the parser to add the missing handler, or (b) demote
/// the row's coverage_level. Never silently widen ValidHandlerKinds
/// to make a red test green.
///
/// See lilia-docs/technical/latex-coverage-architecture.md for the
/// target architecture this test guards.
/// </summary>
[Collection("Integration")]
public class CatalogIntegrityTests : IntegrationTestBase
{
    public CatalogIntegrityTests(TestDatabaseFixture fixture) : base(fixture) { }

    /// <summary>
    /// The canonical set of parser handler kinds. Adding to this set
    /// is a contract change — it implies a new handler in
    /// LatexParser.cs (or a new dispatch path in the catalog-driven
    /// refactor). Every value here should be documented in the
    /// architecture doc and backed by a registered handler.
    /// </summary>
    private static readonly HashSet<string> ValidHandlerKinds = new(StringComparer.Ordinal)
    {
        // Preamble-time rewrite into a canonical form (commit 9afe55c).
        "shim",

        // ParseAlgorithmicLines — typed Require / Ensure / State / ... lines.
        "algorithmic",

        // \section | \subsection | \subsubsection | \paragraph | \subparagraph.
        "section-regex",

        // citePattern covering cite / citep / citet / citealp / citealt /
        // parencite / textcite / footcite / autocite / nocite.
        "citation-regex",

        // MatchBalanced + StripBalancedCommand for document metadata.
        "metadata-extract",

        // PreservedInlineCommands — kept verbatim for downstream renderers.
        "inline-preserved",

        // CodeDisplayInlineCommands — wrapped in backticks.
        "inline-code",

        // MarkdownInlineWrappers — textbf / textit / emph / underline.
        "inline-markdown",

        // TheoremEnvironments dict — mapped to ImportTheorem with a typed
        // TheoremEnvironmentType.
        "theorem-like",

        // KnownEnvironments set — structurally handled (equation, figure,
        // table, lstlisting, algorithm, itemize, ...).
        "known-structural",

        // PassThroughEnvironments — wrapper dropped, body re-parsed.
        "pass-through",

        // KaTeX math environments (cases / matrix / array / subequations).
        "math-env",

        // KaTeX math commands — Greek, operators, arrows, fractions, ...
        "math-katex",

        // Specific-regex handling for commands that survive import without
        // full block-type mapping (booktabs rules, beamer frame decorations,
        // documentclass, usepackage, etc.).
        "parser-regex",

        // Unknown-environment passthrough (LatexParser line ~1088):
        // content survives as ImportLatexPassthrough but preview doesn't
        // render visually.
        "passthrough",

        // Inline catch-all in NormaliseInlineCommands: unknown \cmd{arg}
        // → arg extracted, command name dropped.
        "inline-catch-all",
    };

    private LiliaDbContext CreateDbContext()
    {
        var scope = Fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
    }

    [Fact]
    public async Task Every_covered_token_declares_a_handler_kind()
    {
        await using var db = CreateDbContext();

        var violations = await db.LatexTokens.AsNoTracking()
            .Where(t => t.CoverageLevel == "full"
                     || t.CoverageLevel == "partial"
                     || t.CoverageLevel == "shimmed")
            .Where(t => t.HandlerKind == null)
            .Select(t => new { t.Kind, t.Name, t.PackageSlug, t.CoverageLevel })
            .ToListAsync();

        violations.Should().BeEmpty(
            "every full / partial / shimmed token must declare a handler_kind — " +
            "the column is the contract between the catalog and LatexParser.cs. " +
            "Rows missing it: {0}",
            string.Join(", ", violations.Select(v =>
                $"{v.Kind} '{v.Name}' ({v.PackageSlug ?? "kernel"}) @ {v.CoverageLevel}")));
    }

    [Fact]
    public async Task Handler_kind_values_are_all_whitelisted()
    {
        await using var db = CreateDbContext();

        var distinct = await db.LatexTokens.AsNoTracking()
            .Where(t => t.HandlerKind != null)
            .Select(t => t.HandlerKind!)
            .Distinct()
            .ToListAsync();

        var unknown = distinct.Where(k => !ValidHandlerKinds.Contains(k)).ToList();

        unknown.Should().BeEmpty(
            "handler_kind values must come from the whitelist in CatalogIntegrityTests.ValidHandlerKinds. " +
            "Unknown values observed: {0}. If adding a genuinely new handler path, register it in both " +
            "LatexParser.cs and this test (and update lilia-docs/technical/latex-coverage-architecture.md).",
            string.Join(", ", unknown));
    }

    [Fact]
    public async Task No_row_is_UNCLASSIFIED()
    {
        // The backfill migration (20260423063047_AddTokenHandlerKind) labels
        // any full/partial/shimmed row that didn't match a specific handler
        // set as 'UNCLASSIFIED' so it shows up here. Those rows are the
        // follow-up queue — each needs an explicit handler or a demotion.
        await using var db = CreateDbContext();

        var unclassified = await db.LatexTokens.AsNoTracking()
            .Where(t => t.HandlerKind == "UNCLASSIFIED")
            .Select(t => new { t.Kind, t.Name, t.PackageSlug, t.CoverageLevel })
            .ToListAsync();

        unclassified.Should().BeEmpty(
            "'UNCLASSIFIED' handler_kind means the backfill couldn't place the row in any " +
            "known handler category. Either assign a real handler_kind or demote coverage_level to " +
            "'unsupported'. Rows: {0}",
            string.Join(", ", unclassified.Select(v =>
                $"{v.Kind} '{v.Name}' ({v.PackageSlug ?? "kernel"}) @ {v.CoverageLevel}")));
    }

    [Fact]
    public async Task Parser_HashSets_have_no_catalog_orphans()
    {
        // Mirrors the boot-time audit wired into Program.cs. Runs every
        // hardcoded HashSet member in LatexParser through the router and
        // asserts each has a catalog row (non-null handler_kind). Keeps
        // the catalog in lockstep with the parser's dispatch reality:
        // adding a token to KnownEnvironments / TheoremEnvironments /
        // PassThroughEnvironments / PreservedInlineCommands /
        // CodeDisplayInlineCommands / MarkdownInlineWrappers without
        // also cataloguing it fails the build.
        //
        // Program.cs's PreloadAsync is skipped in the Testing
        // environment, so the test primes the cache itself before
        // the parser-level audit consults it.
        using var scope = Fixture.Factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ILatexCatalogService>();
        if (catalog is Lilia.Api.Services.LatexCatalogService concrete)
        {
            await concrete.PreloadAsync();
        }

        var parser = (Lilia.Import.Services.LatexParser)
            scope.ServiceProvider.GetRequiredService<Lilia.Import.Interfaces.ILatexParser>();

        var orphans = parser.FindCatalogOrphans();

        orphans.Should().BeEmpty(
            "LatexParser HashSet members must have a matching catalog row. " +
            "If this test fails, either add a catalog migration covering the new tokens " +
            "or remove them from the HashSet. Orphans: {0}",
            string.Join(", ", orphans));
    }
}
