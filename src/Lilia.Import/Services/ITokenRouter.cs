namespace Lilia.Import.Services;

/// <summary>
/// Resolves a LaTeX token (command / environment name + kind) to the
/// parser-routing decision the catalog claims for it. Stage 3 of the
/// parser-reads-catalog migration
/// (lilia-docs/technical/latex-coverage-architecture.md) consumes this
/// from <see cref="LatexParser"/> instead of the module's hardcoded
/// HashSets / Dictionaries, so the catalog becomes the single source
/// of truth for dispatch.
///
/// Lives in Lilia.Import — not Lilia.Api — because the parser can't
/// take a dependency on the outer layer. The Lilia.Api project wires
/// up a <c>CatalogTokenRouter</c> implementation backed by
/// <c>ILatexCatalogService</c> and registers it in DI.
/// </summary>
public interface ITokenRouter
{
    /// <summary>
    /// Returns the parser handler that routes this token, matching
    /// values from the canonical whitelist enforced by
    /// CatalogIntegrityTests (section-regex / math-katex / theorem-like /
    /// known-structural / algorithmic / inline-preserved / …). Returns
    /// <c>null</c> when the catalog has no row for the token, or when
    /// the row is at coverage_level 'unsupported' / 'none'.
    /// </summary>
    /// <param name="name">
    /// The bare token name — "section", "cite", "itemize" — without
    /// the leading backslash for commands.
    /// </param>
    /// <param name="kind">
    /// "command" or "environment".
    /// </param>
    string? HandlerKindFor(string name, string kind);

    /// <summary>
    /// Returns the Lilia block type this token maps to, if any. Often
    /// null for inline-preserved and inline-markdown rows; set for
    /// environments that become a typed block (equation, figure,
    /// algorithm, theorem, code, heading, …).
    /// </summary>
    string? MapsToBlockTypeFor(string name, string kind);
}

/// <summary>
/// No-op default so callers who construct <see cref="LatexParser"/>
/// without DI — ad-hoc tests, the Lilia.Import library consumed
/// outside Lilia.Api — keep working. Returns null for every lookup,
/// which is the same as "no catalog entry" and triggers the parser's
/// fallback handling (hardcoded sets during the migration; the
/// unknown-env passthrough and inline catch-all afterwards).
/// </summary>
public sealed class NullTokenRouter : ITokenRouter
{
    public static readonly NullTokenRouter Instance = new();

    public string? HandlerKindFor(string name, string kind) => null;
    public string? MapsToBlockTypeFor(string name, string kind) => null;
}
