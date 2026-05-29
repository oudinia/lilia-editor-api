namespace Lilia.Core.Entities;

public class Formula
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LatexContent { get; set; } = string.Empty;
    public string? LmlContent { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Subcategory { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsFavorite { get; set; }
    public bool IsSystem { get; set; }
    public int UsageCount { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Lilia Math editor theme this formula belongs to — one of
    /// general / calculus / linalg / stats / discrete / sets / physics / cs.
    /// Null for legacy formulas not yet mapped to a theme.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Stable kebab-case identifier matching the reference catalog in
    /// lilia-docs/reference/math/data/formulas.json. Required for
    /// system-seeded formulas so re-seeding is idempotent; null for
    /// user-created formulas.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// JSON-serialised Lilia Math token list — what the editor's
    /// 'Load from library' modal hands back. Optional; when null the
    /// editor falls back to parsing LatexContent.
    /// </summary>
    public string? TokensJson { get; set; }

    // Navigation properties
    public virtual User? User { get; set; }
}

/// <summary>
/// Lilia Math editor themes — the 8 STEM presets surfaced by the
/// Common-panel theme rail. Mirrors `THEMES[].id` in
/// `lilia-web-editor/src/components/math-editor/commonThemes.ts`.
/// </summary>
public static class FormulaThemes
{
    public const string General  = "general";
    public const string Calculus = "calculus";
    public const string LinAlg   = "linalg";
    public const string Stats    = "stats";
    public const string Discrete = "discrete";
    public const string Sets     = "sets";
    public const string Physics  = "physics";
    public const string Cs       = "cs";

    public static readonly IReadOnlyList<string> All = new[]
    {
        General, Calculus, LinAlg, Stats, Discrete, Sets, Physics, Cs,
    };

    public static bool IsValid(string? theme) =>
        theme is not null && All.Contains(theme);
}

public static class FormulaCategories
{
    public const string Math = "math";
    public const string Physics = "physics";
    public const string Chemistry = "chemistry";
    public const string ComputerScience = "computer-science";
    public const string Statistics = "statistics";
    public const string Engineering = "engineering";
    public const string Other = "other";
}

public static class FormulaSubcategories
{
    // Math
    public const string Algebra = "algebra";
    public const string Calculus = "calculus";
    public const string Trigonometry = "trigonometry";
    public const string LinearAlgebra = "linear-algebra";
    public const string SetTheory = "set-theory";

    // Physics
    public const string Mechanics = "mechanics";
    public const string Electromagnetism = "electromagnetism";
    public const string Thermodynamics = "thermodynamics";
    public const string QuantumMechanics = "quantum-mechanics";
    public const string Relativity = "relativity";
    public const string Optics = "optics";

    // Chemistry
    public const string GeneralChemistry = "general-chemistry";
    public const string PhysicalChemistry = "physical-chemistry";

    // Statistics
    public const string Probability = "probability";
    public const string Distributions = "distributions";

    // CS
    public const string InformationTheory = "information-theory";
    public const string Algorithms = "algorithms";
}
