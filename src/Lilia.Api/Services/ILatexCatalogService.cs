namespace Lilia.Api.Services;

/// <summary>
/// In-memory projection of the LaTeX catalog. Loaded once at boot from
/// the latex_packages / latex_tokens / latex_document_classes tables,
/// then served synchronously so the parser never waits on a DB round
/// trip. Unknown tokens hit <see cref="ReportUnknownAsync"/> which
/// upserts with coverage_level='unsupported' — that's the observability
/// sink that tells us what users are actually throwing at us.
///
/// Seeded in the DB; extended at runtime by the parser. Admin surface
/// (Phase 3) can flip coverage_level once we've implemented handling.
/// </summary>
public interface ILatexCatalogService
{
    /// <summary>
    /// Lookup by (name, kind, packageSlug). packageSlug=null checks the
    /// kernel scope. Returns null for unknown — caller should call
    /// <see cref="ReportUnknownAsync"/> so the catalog self-populates.
    /// </summary>
    CatalogTokenEntry? LookupToken(string name, string kind, string? packageSlug = null);

    /// <summary>
    /// Package lookup by slug — drives the Package Inspector modal + the
    /// import-review Coverage tab.
    /// </summary>
    CatalogPackageEntry? LookupPackage(string slug);

    /// <summary>
    /// Document-class lookup — used at parse time to select the shim and
    /// default engine.
    /// </summary>
    CatalogDocumentClassEntry? LookupDocumentClass(string slug);

    /// <summary>
    /// Upsert an unknown token as coverage_level='unsupported' so we
    /// accumulate observability on format gaps. Idempotent — re-calling
    /// with the same key just updates updated_at. Returns the token id.
    /// </summary>
    Task<Guid> ReportUnknownAsync(string name, string kind, string? packageSlug, CancellationToken ct = default);

    /// <summary>
    /// Bulk-upsert usage rows for a session. <paramref name="tokens"/>
    /// comes from the parser's walk — one entry per distinct token seen
    /// with its count. Written to DB in one statement (DB-first rule).
    /// </summary>
    Task RecordUsageAsync(Guid sessionId, IEnumerable<CatalogTokenUsage> tokens, CancellationToken ct = default);

    /// <summary>
    /// Fleet-wide coverage stats. Used by the admin dashboard + the
    /// release smoke test. Returns counts grouped by coverage_level.
    /// </summary>
    Task<CatalogCoverageReport> GetCoverageReportAsync(TimeSpan window, CancellationToken ct = default);

    /// <summary>
    /// Per-session coverage breakdown — drives the Coverage tab on the
    /// import review page. Users see what their document contains, how
    /// we handle each token, and which pieces are unsupported.
    /// </summary>
    Task<SessionCoverage> GetSessionCoverageAsync(Guid sessionId, CancellationToken ct = default);
}

public sealed record CatalogTokenEntry(
    Guid Id,
    string Name,
    string Kind,
    string? PackageSlug,
    int? Arity,
    int? OptionalArity,
    bool ExpectsBody,
    string? SemanticCategory,
    string? MapsToBlockType,
    string CoverageLevel,
    Guid? AliasOf);

public sealed record CatalogPackageEntry(
    string Slug,
    string DisplayName,
    string Category,
    string CoverageLevel,
    string? CoverageNotes,
    string? CtanUrl);

public sealed record CatalogDocumentClassEntry(
    string Slug,
    string DisplayName,
    string Category,
    string CoverageLevel,
    string? DefaultEngine,
    string? ShimName);

public sealed record CatalogTokenUsage(Guid TokenId, int Count);

public sealed record CatalogCoverageReport(
    int TotalTokensSeen,
    int FullCount,
    int PartialCount,
    int ShimmedCount,
    int NoneCount,
    int UnsupportedCount,
    IReadOnlyList<(string Name, string Kind, string? Package, int Count)> TopUnsupported);

/// <summary>
/// Per-session coverage projection — powers the review UI's Coverage tab.
/// Totals roll up across the session; Tokens lists every distinct token
/// with its count so the UI can render a sortable table.
/// </summary>
public sealed record SessionCoverage(
    int TotalTokens,
    int DistinctTokens,
    int FullCount,
    int PartialCount,
    int ShimmedCount,
    int NoneCount,
    int UnsupportedCount,
    IReadOnlyList<SessionCoverageRow> Tokens);

public sealed record SessionCoverageRow(
    string Name,
    string Kind,
    string? PackageSlug,
    string? PackageDisplay,
    string CoverageLevel,
    string? MapsToBlockType,
    string? SemanticCategory,
    int Count,
    string? Notes);
