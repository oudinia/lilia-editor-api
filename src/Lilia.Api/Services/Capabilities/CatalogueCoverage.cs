using Lilia.Core.Capabilities;

namespace Lilia.Api.Services.Capabilities;

/// <summary>
/// Translates the <c>coverage_level</c> string the catalogues already store
/// into a <see cref="Support"/> value.
/// </summary>
public static class CatalogueCoverage
{
    /// <summary>
    /// Map a stored level. An unrecognised value becomes
    /// <see cref="Support.Unknown"/>, not a guess.
    /// </summary>
    /// <remarks>
    /// <para>The column is a free-text string with no CHECK constraint, holding
    /// five values today: <c>full</c>, <c>shimmed</c>, <c>partial</c>,
    /// <c>none</c>, <c>unsupported</c>. Nothing stops a sixth being typed into
    /// it, and a hand-authored catalogue is exactly where that happens.</para>
    ///
    /// <para>Falling back to <c>None</c> would invent a pessimistic answer, and
    /// falling back to <c>Full</c> would invent an optimistic one. Unknown is
    /// the only honest reading of a value nobody has taught this code about,
    /// and it is reportable — the caller sees "I could not tell" rather than a
    /// verdict with no basis.</para>
    /// </remarks>
    public static Support FromLevel(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "full" => Support.Full,
        "shimmed" or "shim" => Support.Shimmed,
        "partial" => Support.Partial,
        "none" => Support.None,
        "unsupported" => Support.Impossible,
        _ => Support.Unknown,
    };

    /// <summary>
    /// Whether a catalogue verdict recorded for LaTeX says anything about
    /// Typst.
    /// </summary>
    /// <remarks>
    /// <para>It does not. These catalogues were written about the LaTeX
    /// toolchain — <c>latex_tokens</c>, <c>latex_packages</c>,
    /// <c>latex_document_classes</c>. Reporting "this package is <c>full</c>"
    /// against a Typst render would be a confident answer to a question the row
    /// was never about, since Typst has no packages in that sense at all.</para>
    ///
    /// <para>This is the honest form of the phase-2 compromise. The catalogues
    /// have one <c>coverage_level</c> with no target, so wrapping them can only
    /// answer for the family they were written about, and must say Unknown for
    /// the rest. That gap is the argument for collecting per-target data — and
    /// it stays visible instead of being papered over.</para>
    /// </remarks>
    public static bool AppliesTo(RenderTarget target) => target is not RenderTarget.Typst;
}
