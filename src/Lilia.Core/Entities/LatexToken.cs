namespace Lilia.Core.Entities;

/// <summary>
/// A single LaTeX token — command, environment, declaration, length, or
/// counter. The parser looks tokens up by (name, kind, package_slug) to
/// decide how to handle them; unknown tokens land here with
/// coverage_level = 'unsupported' so we get observability on what users
/// throw at us.
/// </summary>
public class LatexToken
{
    public Guid Id { get; set; }

    /// <summary>
    /// Token name without the leading backslash for commands. For
    /// environments this is the bare env name (e.g. "itemize"), NOT
    /// "begin{itemize}". <see cref="Kind"/> disambiguates.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// CHECK: command / environment / declaration / length / counter.
    /// declaration = scope-changing like \bfseries, \itshape.
    /// length = \parindent, \topmargin.
    /// counter = section counters, etc.
    /// </summary>
    public string Kind { get; set; } = "command";

    /// <summary>Null for kernel tokens (\section, \begin). Otherwise FK to latex_packages.</summary>
    public string? PackageSlug { get; set; }

    /// <summary>Total arguments including optional ones.</summary>
    public int? Arity { get; set; }

    /// <summary>Number of optional arguments (in square brackets).</summary>
    public int? OptionalArity { get; set; }

    /// <summary>True for environments and body-taking commands like \newcommand.</summary>
    public bool ExpectsBody { get; set; }

    /// <summary>
    /// What this token does, semantically — drives review-UI grouping and
    /// "per-type editor" targeting. e.g. "heading", "math", "list",
    /// "citation", "float", "font", "layout", "raw_latex".
    /// </summary>
    public string? SemanticCategory { get; set; }

    /// <summary>Target Lilia block type if this token becomes one, else null.</summary>
    public string? MapsToBlockType { get; set; }

    /// <summary>Same vocabulary as LatexPackage.CoverageLevel.</summary>
    public string CoverageLevel { get; set; } = "none";

    /// <summary>
    /// Name of the parser routing path that handles this token — the
    /// contract between the catalog and LatexParser.cs. Must be one of
    /// the kinds enumerated in CatalogIntegrityTests (CI fails on drift).
    /// Values: shim / algorithmic / section-regex / citation-regex /
    /// metadata-extract / inline-preserved / inline-code / inline-markdown
    /// / theorem-like / known-structural / pass-through / math-env /
    /// math-katex / parser-regex / catch-all-arg / passthrough.
    /// Null for coverage_level='unsupported'/'none' (no handler claimed).
    /// </summary>
    public string? HandlerKind { get; set; }

    public string? Notes { get; set; }

    /// <summary>When set, this token is a synonym of another (e.g. \citep → \cite). Parser resolves on load.</summary>
    public Guid? AliasOf { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual LatexPackage? Package { get; set; }
    public virtual LatexToken? Alias { get; set; }
}
