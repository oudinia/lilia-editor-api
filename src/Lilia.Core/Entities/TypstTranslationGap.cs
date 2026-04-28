namespace Lilia.Core.Entities;

/// <summary>
/// Catalog row for a known LaTeX → Typst translation gap — a pattern
/// the exporter recognises as out of scope, so the document falls
/// back to pdflatex transparently.
///
/// Pairs with <see cref="TypstTranslationHandler"/> on the catalog
/// side and <c>import_telemetry_events</c> on the runtime side. The
/// admin coverage report joins all three to show: shipped handlers,
/// open gaps, and how many real fallbacks the gaps account for in
/// the last week.
/// </summary>
public class TypstTranslationGap
{
    public Guid Id { get; set; }

    /// <summary>Stable lookup key. Convention:
    /// <c>category.specific-name</c> (e.g.
    /// <c>math.two-letter-identifier</c>,
    /// <c>matrix.deeply-nested</c>). Matches the
    /// <c>token_or_env</c> emitted by silent_fallback events when
    /// the parser can identify which gap fired.</summary>
    public string GapKey { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    /// <summary>Sample LaTeX shape that triggers the gap.</summary>
    public string SamplePattern { get; set; } = string.Empty;

    /// <summary>Typst error string we observe at compile time.
    /// e.g. "unknown variable: ab". Lets us spot when the same
    /// gap reappears with a slight wording change.</summary>
    public string? TypstErrorShape { get; set; }

    /// <summary>CHECK: none / workaround / scheduled / shipped.
    /// "shipped" is set when a corresponding handler closes the
    /// gap; the row stays around as historical context rather than
    /// being deleted.</summary>
    public string MitigationStatus { get; set; } = "none";

    /// <summary>CHECK: info / warn / error.
    /// info = cosmetic-only, falls back fine.
    /// warn = falls back but degrades preview quality.
    /// error = blocks the doc from rendering at all.</summary>
    public string BlockingSeverity { get; set; } = "info";

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
