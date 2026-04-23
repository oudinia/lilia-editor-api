using Lilia.Api.Services;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Public-facing LaTeX coverage surface — fuels lilia-editor.com/latex-
/// coverage (Batch F of the Q2 plan). Read-only, no auth, CDN-cacheable.
/// Exposes structural coverage + package metadata only; per-session
/// usage data stays private in <see cref="LatexCoverageController"/>.
/// </summary>
[ApiController]
[Route("api/public/latex-coverage")]
[AllowAnonymous]
public class PublicCoverageController : ControllerBase
{
    private readonly ILatexCatalogService _catalog;
    private readonly LiliaDbContext _db;

    public PublicCoverageController(ILatexCatalogService catalog, LiliaDbContext db)
    {
        _catalog = catalog;
        _db = db;
    }

    /// <summary>
    /// Top-level SLO badge + counters by coverage level + package counts
    /// by category. Cached by the CDN for 15 minutes — the catalog
    /// doesn't mutate often enough to warrant a shorter window.
    /// </summary>
    [HttpGet("summary")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<PublicCoverageSummaryDto>> Summary(CancellationToken ct)
    {
        var tokens = await _db.LatexTokens
            .GroupBy(t => t.CoverageLevel)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Tok(string level) => tokens.FirstOrDefault(x => x.Level == level)?.Count ?? 0;

        var packages = await _db.LatexPackages
            .GroupBy(p => p.CoverageLevel)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Pkg(string level) => packages.FirstOrDefault(x => x.Level == level)?.Count ?? 0;

        // EF 10 can't translate GroupBy → record-constructor projections;
        // the earlier two GroupBy queries work because they project into
        // anonymous types. Mirror that pattern here and build the DTO in
        // memory post-materialisation.
        var categoryRaw = await _db.LatexPackages
            .GroupBy(p => p.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var categories = categoryRaw
            .OrderByDescending(x => x.Count)
            .Select(x => new CategoryBreakdownDto(x.Category, x.Count))
            .ToList();

        // Handler-kind breakdown for the facet chip row on the public
        // page. Only covered rows (full / partial / shimmed) count —
        // unsupported rows have no handler_kind.
        var handlerKindRaw = await _db.LatexTokens
            .Where(t => t.HandlerKind != null
                     && (t.CoverageLevel == "full"
                      || t.CoverageLevel == "partial"
                      || t.CoverageLevel == "shimmed"))
            .GroupBy(t => t.HandlerKind!)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var handlerKinds = handlerKindRaw
            .OrderByDescending(x => x.Count)
            .Select(x => new HandlerKindBreakdownDto(x.Kind, x.Count))
            .ToList();

        var totalCovered = Tok("full") + Tok("partial") + Tok("shimmed");
        var totalTokens = tokens.Sum(x => x.Count);
        var sloPercent = totalTokens == 0 ? 0 : Math.Round(100.0 * totalCovered / totalTokens, 1);

        var classCount = await _db.LatexDocumentClasses.CountAsync(ct);

        return Ok(new PublicCoverageSummaryDto(
            TotalTokens: totalTokens,
            TotalPackages: packages.Sum(x => x.Count),
            TotalClasses: classCount,
            CoveragePercent: sloPercent,
            Tokens: new CoverageCountsDto(Tok("full"), Tok("partial"), Tok("shimmed"), Tok("none"), Tok("unsupported")),
            Packages: new CoverageCountsDto(Pkg("full"), Pkg("partial"), Pkg("shimmed"), Pkg("none"), Pkg("unsupported")),
            Categories: categories,
            HandlerKinds: handlerKinds));
    }

    /// <summary>
    /// Searchable + filterable package list. No pagination yet — catalog
    /// is &lt;100 packages at launch.
    /// </summary>
    // VaryByQueryKeys requires AddResponseCaching + UseResponseCaching, which
    // we don't register — the public CDN keys on URL + query automatically,
    // so the attribute would only buy in-process caching we don't want.
    [HttpGet("packages")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<List<PublicPackageDto>>> Packages(
        [FromQuery] string? category = null,
        [FromQuery] string? coverageLevel = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        var query = _db.LatexPackages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category == category);
        if (!string.IsNullOrWhiteSpace(coverageLevel)) query = query.Where(p => p.CoverageLevel == coverageLevel);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Slug, needle) || EF.Functions.ILike(p.DisplayName, needle));
        }

        var rows = await query
            .OrderBy(p => p.Category).ThenBy(p => p.Slug)
            .Select(p => new PublicPackageDto(
                p.Slug, p.DisplayName, p.Category, p.CoverageLevel, p.CoverageNotes, p.CtanUrl))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Per-package detail — metadata + the list of tokens it ships.
    /// Drives the detail panel on the public coverage page.
    /// </summary>
    [HttpGet("packages/{slug}")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<PublicPackageDetailDto>> PackageDetail(string slug, CancellationToken ct)
    {
        var pkg = await _db.LatexPackages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (pkg is null) return NotFound();

        var tokens = await _db.LatexTokens.AsNoTracking()
            .Where(t => t.PackageSlug == slug)
            .OrderBy(t => t.Kind).ThenBy(t => t.Name)
            .Select(t => new PublicTokenDto(
                t.Name, t.Kind, t.CoverageLevel, t.MapsToBlockType, t.SemanticCategory, t.Notes))
            .ToListAsync(ct);

        return Ok(new PublicPackageDetailDto(
            Slug: pkg.Slug,
            DisplayName: pkg.DisplayName,
            Category: pkg.Category,
            CoverageLevel: pkg.CoverageLevel,
            CoverageNotes: pkg.CoverageNotes,
            CtanUrl: pkg.CtanUrl,
            Tokens: tokens));
    }

    /// <summary>
    /// "How we measure coverage" — drives the collapsible status card
    /// on the public page. Describes the rollout stages that back the
    /// coverage claims, how many CI assertions are enforcing them, and
    /// the current catalog snapshot. Deliberately NOT exposing
    /// internal audit buckets or engineer-estimate trust percentages.
    /// </summary>
    [HttpGet("implementation-status")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<PublicImplementationStatusDto>> ImplementationStatus(CancellationToken ct)
    {
        // Catalog snapshot drives two fields: the coverage counts and the
        // handler-kind distribution. Only cover coverage level != 'none'
        // in the totals so the 'none' bucket (reserved for tokens with
        // zero handling) doesn't inflate the denominator.
        var coverageCounts = await _db.LatexTokens
            .GroupBy(t => t.CoverageLevel)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Cov(string level) => coverageCounts.FirstOrDefault(x => x.Level == level)?.Count ?? 0;

        var handlerKindRaw = await _db.LatexTokens
            .Where(t => t.HandlerKind != null
                     && (t.CoverageLevel == "full"
                      || t.CoverageLevel == "partial"
                      || t.CoverageLevel == "shimmed"))
            .GroupBy(t => t.HandlerKind!)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var handlerKinds = handlerKindRaw
            .OrderByDescending(x => x.Count)
            .Select(x => new HandlerKindBreakdownDto(x.Kind, x.Count))
            .ToList();

        var totalTokens = coverageCounts.Sum(x => x.Count);

        // Stages & test tallies are hardcoded — they describe
        // engineering milestones, not catalog state. Update this list
        // as stages land or shift.
        var stages = new List<ImplementationStageDto>
        {
            new(
                Id: "truthful-catalog",
                Title: "Every token we list is catalogued with a handler",
                Status: "shipped",
                ProgressPercent: 100,
                Detail: $"{Cov("full") + Cov("partial") + Cov("shimmed")} tokens at coverage level full, partial, or shimmed — each declares which parser routing path handles it."),
            new(
                Id: "ci-contract",
                Title: "Build breaks if the catalog drifts from the parser",
                Status: "shipped",
                ProgressPercent: 100,
                Detail: "Integration test asserts every covered token has a handler kind from a whitelisted set. Any new value fails the build until reviewed."),
            new(
                Id: "catalog-driven-dispatch",
                Title: "Parser reads routing from the catalog instead of hardcoded lists",
                Status: "in_progress",
                ProgressPercent: 10,
                Detail: "The catalog cache now carries handler_kind; the parser refactor to consult it is the next active work."),
            new(
                Id: "end-to-end-fixtures",
                Title: "One fixture per handler kind, asserted in CI",
                Status: "planned",
                ProgressPercent: 0,
                Detail: "Will prove every 'full' claim end-to-end — parse a canonical sample, assert the rendered output shape."),
            new(
                Id: "typst-oracle",
                Title: "Independent validation via a Typst compiler",
                Status: "planned",
                ProgressPercent: 0,
                Detail: "Second opinion on every 'full' claim — if Typst can't round-trip it, we demote. Stretch goal, not the critical path."),
        };

        // Hand-counted from tests/Lilia.Api.Tests/Integration/LatexCatalog/
        // CatalogFixtureTests. Update when adding/removing fixtures.
        // Current: 11 fixtures covering 10 handler kinds
        // (section-regex, citation-regex, known-structural × 2,
        //  theorem-like, algorithmic, math-katex, math-env,
        //  inline-markdown, inline-preserved, metadata-extract,
        //  inline-code). Remaining to reach 16: shim, pass-through,
        //  parser-regex, passthrough, inline-catch-all.
        var tests = new ImplementationTestsDto(
            CiAssertions: 3,
            CiAssertionsDescription: "Catalog integrity: every covered row has a handler kind, all handler kinds are whitelisted, no row is unclassified.",
            PerHandlerFixtures: 10,
            PerHandlerFixturesTarget: 16,
            PerHandlerFixturesDescription: "Canonical fixture per handler kind that parses through the pipeline and checks the output block type.");

        var snapshot = new CatalogSnapshotDto(
            TotalTokens: totalTokens,
            Full: Cov("full"),
            Partial: Cov("partial"),
            Shimmed: Cov("shimmed"),
            Unsupported: Cov("unsupported"),
            None: Cov("none"),
            HandlerKinds: handlerKinds);

        return Ok(new PublicImplementationStatusDto(
            MeasuredAt: DateTime.UtcNow,
            Stages: stages,
            Tests: tests,
            CatalogSnapshot: snapshot));
    }

    /// <summary>
    /// Token-level search — "does Lilia handle \section / \paragraph /
    /// \frac ...". Drives the keyword-search panel on the public
    /// Coverage page so typing "section" returns the \section row
    /// instead of zero package hits.
    ///
    /// Query params:
    ///   q              — substring match on name / maps_to_block_type / handler_kind
    ///   kind           — command | environment
    ///   coverageLevel  — full | partial | shimmed | none | unsupported
    ///   handlerKind    — section-regex | math-katex | citation-regex | ...
    ///   package        — restrict to one package slug (or 'kernel' for null)
    ///   limit / offset — default 50 / 0; limit capped at 200
    /// </summary>
    [HttpGet("tokens")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<PublicTokenSearchResultDto>> Tokens(
        [FromQuery] string? q = null,
        [FromQuery] string? kind = null,
        [FromQuery] string? coverageLevel = null,
        [FromQuery] string? handlerKind = null,
        [FromQuery] string? package = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = 50;
        if (limit > 200) limit = 200;
        if (offset < 0) offset = 0;

        var query = _db.LatexTokens.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(t => t.Kind == kind);
        if (!string.IsNullOrWhiteSpace(coverageLevel)) query = query.Where(t => t.CoverageLevel == coverageLevel);
        if (!string.IsNullOrWhiteSpace(handlerKind)) query = query.Where(t => t.HandlerKind == handlerKind);

        if (!string.IsNullOrWhiteSpace(package))
        {
            // 'kernel' is how the UI refers to rows with a null package_slug.
            query = package.Equals("kernel", StringComparison.OrdinalIgnoreCase)
                ? query.Where(t => t.PackageSlug == null)
                : query.Where(t => t.PackageSlug == package);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Name, needle)
                || (t.MapsToBlockType != null && EF.Functions.ILike(t.MapsToBlockType, needle))
                || (t.HandlerKind != null && EF.Functions.ILike(t.HandlerKind, needle)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.Kind).ThenBy(t => t.Name).ThenBy(t => t.PackageSlug)
            .Skip(offset).Take(limit)
            .Select(t => new PublicTokenRowDto(
                t.Name,
                t.Kind,
                t.PackageSlug,
                t.CoverageLevel,
                t.MapsToBlockType,
                t.HandlerKind,
                t.SemanticCategory,
                t.Notes))
            .ToListAsync(ct);

        return Ok(new PublicTokenSearchResultDto(total, offset, limit, items));
    }

    // Kernel sectioning commands available under each document-class
    // category. Article-likes get \section → \subparagraph (5 levels);
    // book-likes add \part and \chapter on top. Presentation (beamer)
    // replaces \chapter with \frame; letter has no sectioning.
    //
    // Hardcoded here rather than on a new column because the set is
    // small, slow-moving, and derivable from the class category. If
    // this grows fancy we'll promote it to a `valid_sections text[]`
    // column on latex_document_classes and a migration.
    private static readonly string[] ArticleSectioning =
        { "section", "subsection", "subsubsection", "paragraph", "subparagraph" };
    private static readonly string[] BookSectioning =
        { "part", "chapter", "section", "subsection", "subsubsection", "paragraph", "subparagraph" };
    private static readonly string[] BeamerSectioning =
        { "section", "subsection", "frame" };
    private static readonly Dictionary<string, string[]> SectioningByCategory = new()
    {
        ["article"] = ArticleSectioning,
        ["report"] = BookSectioning,     // report has \chapter
        ["book"] = BookSectioning,
        ["memoir"] = BookSectioning,
        ["presentation"] = BeamerSectioning,
        ["letter"] = Array.Empty<string>(),  // letter uses \opening / \closing, no sectioning
        ["cv"] = Array.Empty<string>(),      // cv classes define their own; surfaced via class-specific tokens
        ["other"] = Array.Empty<string>(),
    };

    /// <summary>
    /// Per-class detail — fuels the class-detail drawer. Carries the
    /// kernel sectioning commands relevant to the class's category plus
    /// every token in the catalog attributed to a package with the
    /// same slug (moderncv / altacv / resume / beamer etc. — these ship
    /// as packages + classes sharing a name).
    /// </summary>
    [HttpGet("classes/{slug}")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<PublicClassDetailDto>> ClassDetail(string slug, CancellationToken ct)
    {
        var cls = await _db.LatexDocumentClasses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug == slug, ct);
        if (cls is null) return NotFound();

        // Sectioning commands relevant for this class's category. We
        // look up the kernel rows so the response carries their real
        // coverage_level (e.g. \chapter is 'partial' because it's not
        // in the parser's section regex — that's genuinely useful for
        // a book user to see).
        var sectioningNames = SectioningByCategory.TryGetValue(cls.Category, out var names)
            ? names
            : Array.Empty<string>();
        List<PublicSectioningCommandDto> sectioning = new();
        if (sectioningNames.Length > 0)
        {
            var rows = await _db.LatexTokens.AsNoTracking()
                .Where(t => t.Kind == "command"
                         && t.PackageSlug == null
                         && sectioningNames.Contains(t.Name))
                .ToListAsync(ct);
            // Preserve the canonical order of sectioningNames rather
            // than DB order so \part → \chapter → \section → … reads
            // top-down for users.
            sectioning = sectioningNames
                .Select(n => rows.FirstOrDefault(r => r.Name == n))
                .Where(r => r is not null)
                .Select(r => new PublicSectioningCommandDto(
                    r!.Name, r.CoverageLevel, r.MapsToBlockType, r.Notes))
                .ToList();
        }

        // Class-specific tokens — classes whose slug matches a package
        // slug (beamer, moderncv, altacv, resume, exam, memoir,
        // tufte-book) carry tokens like \cvitem / \rSection / \frame.
        var classTokens = await _db.LatexTokens.AsNoTracking()
            .Where(t => t.PackageSlug == slug)
            .OrderBy(t => t.Kind).ThenBy(t => t.Name)
            .Select(t => new PublicTokenRowDto(
                t.Name,
                t.Kind,
                t.PackageSlug,
                t.CoverageLevel,
                t.MapsToBlockType,
                t.HandlerKind,
                t.SemanticCategory,
                t.Notes))
            .ToListAsync(ct);

        return Ok(new PublicClassDetailDto(
            Slug: cls.Slug,
            DisplayName: cls.DisplayName,
            Category: cls.Category,
            CoverageLevel: cls.CoverageLevel,
            DefaultEngine: cls.DefaultEngine,
            Notes: cls.Notes,
            Sectioning: sectioning,
            ClassSpecificTokens: classTokens));
    }

    /// <summary>
    /// Document classes — articled / beamer / moderncv etc. Small list,
    /// returned ungated so the coverage page can render a "classes we
    /// support" section.
    /// </summary>
    [HttpGet("classes")]
    [ResponseCache(Duration = 900, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<List<PublicClassDto>>> Classes(CancellationToken ct)
    {
        var rows = await _db.LatexDocumentClasses.AsNoTracking()
            .OrderBy(c => c.Category).ThenBy(c => c.Slug)
            .Select(c => new PublicClassDto(
                c.Slug, c.DisplayName, c.Category, c.CoverageLevel, c.DefaultEngine, c.Notes))
            .ToListAsync(ct);
        return Ok(rows);
    }
}

public sealed record PublicCoverageSummaryDto(
    int TotalTokens,
    int TotalPackages,
    int TotalClasses,
    double CoveragePercent,
    CoverageCountsDto Tokens,
    CoverageCountsDto Packages,
    List<CategoryBreakdownDto> Categories,
    List<HandlerKindBreakdownDto> HandlerKinds);

public sealed record CoverageCountsDto(int Full, int Partial, int Shimmed, int None, int Unsupported);

public sealed record CategoryBreakdownDto(string Category, int Count);

public sealed record HandlerKindBreakdownDto(string Kind, int Count);

public sealed record PublicPackageDto(string Slug, string DisplayName, string Category, string CoverageLevel, string? Notes, string? CtanUrl);

public sealed record PublicPackageDetailDto(
    string Slug,
    string DisplayName,
    string Category,
    string CoverageLevel,
    string? CoverageNotes,
    string? CtanUrl,
    List<PublicTokenDto> Tokens);

public sealed record PublicTokenDto(string Name, string Kind, string CoverageLevel, string? MapsToBlockType, string? SemanticCategory, string? Notes);

public sealed record PublicClassDto(string Slug, string DisplayName, string Category, string CoverageLevel, string? DefaultEngine, string? Notes);

public sealed record PublicSectioningCommandDto(
    string Name,
    string CoverageLevel,
    string? MapsToBlockType,
    string? Notes);

public sealed record PublicClassDetailDto(
    string Slug,
    string DisplayName,
    string Category,
    string CoverageLevel,
    string? DefaultEngine,
    string? Notes,
    List<PublicSectioningCommandDto> Sectioning,
    List<PublicTokenRowDto> ClassSpecificTokens);

// Token search carries PackageSlug + HandlerKind so the UI can show
// which package (or "kernel") the token belongs to and link the row
// back to a package drawer.
public sealed record PublicTokenRowDto(
    string Name,
    string Kind,
    string? PackageSlug,
    string CoverageLevel,
    string? MapsToBlockType,
    string? HandlerKind,
    string? SemanticCategory,
    string? Notes);

public sealed record PublicTokenSearchResultDto(
    int Total,
    int Offset,
    int Limit,
    List<PublicTokenRowDto> Items);

// Implementation-status surface. Engineering milestones, CI tallies, and
// the catalog-state-of-the-world, translated into user-friendly copy.
public sealed record ImplementationStageDto(
    string Id,
    string Title,
    string Status,          // shipped | in_progress | planned
    int ProgressPercent,    // 0..100
    string Detail);

public sealed record ImplementationTestsDto(
    int CiAssertions,
    string CiAssertionsDescription,
    int PerHandlerFixtures,
    int PerHandlerFixturesTarget,
    string PerHandlerFixturesDescription);

public sealed record CatalogSnapshotDto(
    int TotalTokens,
    int Full,
    int Partial,
    int Shimmed,
    int Unsupported,
    int None,
    List<HandlerKindBreakdownDto> HandlerKinds);

public sealed record PublicImplementationStatusDto(
    DateTime MeasuredAt,
    List<ImplementationStageDto> Stages,
    ImplementationTestsDto Tests,
    CatalogSnapshotDto CatalogSnapshot);
