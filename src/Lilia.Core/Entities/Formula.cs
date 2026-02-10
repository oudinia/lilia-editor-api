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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User? User { get; set; }
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
