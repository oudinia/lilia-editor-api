using System.Text.Json;

namespace Lilia.Core.Entities;

public class DraftBlock
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Type { get; set; } = string.Empty;
    public JsonDocument Content { get; set; } = JsonDocument.Parse("{}");
    public JsonDocument Metadata { get; set; } = JsonDocument.Parse("{}");
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsFavorite { get; set; }
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual User? User { get; set; }
}

public static class DraftBlockCategories
{
    public const string Equations = "equations";
    public const string Code = "code";
    public const string Tables = "tables";
    public const string Figures = "figures";
    public const string Templates = "templates";
    public const string Notes = "notes";
}
