namespace Lilia.Core.Entities;

/// <summary>
/// Records every time a user inserts a LaTeX command / environment
/// from one of the editor's insertion surfaces (InsertionsPanel,
/// ⌘K palette, slash menu, package-manager inline list).
///
/// Distinct from <see cref="LatexTokenUsage"/> — that one tracks
/// *import-side* token frequency (what users put into their .tex
/// files), this one tracks *insert-side* frequency (what users pick
/// from our visual editor surfaces). The two together tell us:
///
///   - usage.import + insert  →  popular tokens (catalog priority)
///   - usage.import only      →  tokens users have but don't insert
///                                (already in their docs; maybe block-
///                                toolbar covers it)
///   - insert only            →  tokens users add via our UI that they
///                                wouldn't have written by hand —
///                                strong signal that the surface is
///                                value-creating, not just convenience
///
/// Drives content prioritisation for ongoing insert_template backfills:
/// the most-clicked tokens with no template yet are the next backlog.
/// </summary>
public class LatexInsertionEvent
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Token name without the leading backslash. Mirrors LatexToken.Name.</summary>
    public string TokenName { get; set; } = string.Empty;

    /// <summary>command / environment / declaration / length / counter.</summary>
    public string TokenKind { get; set; } = string.Empty;

    /// <summary>FK-shape; NULL for kernel tokens. Mirrors LatexToken.PackageSlug.</summary>
    public string? TokenPackageSlug { get; set; }

    /// <summary>
    /// Where the user clicked.
    ///   panel          — InsertionsPanel side panel
    ///   palette        — ⌘K command palette (Insert / Insert · recent)
    ///   slash          — slash menu in a paragraph block
    ///   package-modal  — per-package expansion in the LaTeX packages dialog
    /// </summary>
    public string Source { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public Guid? DocumentId { get; set; }

    /// <summary>True when the user had a non-empty selection at insert time.</summary>
    public bool WrappedSelection { get; set; }
}
