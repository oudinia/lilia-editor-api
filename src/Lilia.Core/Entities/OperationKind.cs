namespace Lilia.Core.Entities;

/// <summary>
/// What an operation weighs (the owner, 11 Nov 2026: "categorize them… an import of a document of many pages does not
/// count the same as asking for a title… pondering the tasks by complexity, kind of like agility"). Points on a
/// story-point scale (0, 1, 2, 3, 5, 8, 13), plus, for work that grows with its size (an import), points per unit
/// (per 10 pages, say). Kept in the database so the admin console can show and adjust them without a deploy; a
/// recorded operation keeps the points it was given.
/// </summary>
public class OperationKind
{
    /// <summary>The code an operation is recorded under, e.g. export.pdf, edit.small, import.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>help | read | edit | document | check | export | import | ai.</summary>
    public string Category { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public int Points { get; set; }

    /// <summary>Extra points per <see cref="UnitSize"/> units (pages, blocks), null when the size doesn't matter.</summary>
    public int? PointsPerUnit { get; set; }
    public int? UnitSize { get; set; }
    public string? UnitName { get; set; }

    public bool Active { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The points for one operation of this kind and size.</summary>
    public int PointsFor(int? units) =>
        Points + (PointsPerUnit is int per && UnitSize is int size and > 0 && units is int u and > 0
            ? per * (int)Math.Ceiling(u / (double)size)
            : 0);
}
