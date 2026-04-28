namespace Lilia.Core.Entities;

/// <summary>
/// Catalog row for one shipped LaTeX → Typst translation rule. Mirrors
/// the LaTeX coverage architecture (see <see cref="LatexToken"/>) but
/// keyed by handler intent rather than token name — a single
/// <c>handler_key</c> can encode "all greek lowercase letters strip
/// leading backslash" instead of one row per letter.
///
/// Read by <c>TypstCoverageService</c> at startup, joined to
/// <c>import_telemetry_events</c> at report time so the admin coverage
/// page shows shipped-handlers vs. silent-fallback hits side by side.
/// </summary>
public class TypstTranslationHandler
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable lookup key. Convention: <c>category.specific-name</c>
    /// (e.g. <c>math.mathbb</c>, <c>matrix.pmatrix</c>,
    /// <c>spacing.quad</c>, <c>citation.cite-native</c>).
    /// </summary>
    public string HandlerKey { get; set; } = string.Empty;

    /// <summary>Coarse grouping for admin reports — math / matrix /
    /// spacing / citation / link / footnote / figure / heading / list.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The LaTeX shape we accept (regex-y, human-readable).</summary>
    public string SourcePattern { get; set; } = string.Empty;

    /// <summary>The Typst shape we emit.</summary>
    public string TypstEmit { get; set; } = string.Empty;

    /// <summary>CHECK: active / deprecated / planned.</summary>
    public string Status { get; set; } = "active";

    public DateTime ShippedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Commit SHA or version tag where the handler landed —
    /// helps trace which session iteration a handler came from when
    /// debugging a regression.</summary>
    public string? ShippedIn { get; set; }

    public string? Notes { get; set; }
}
