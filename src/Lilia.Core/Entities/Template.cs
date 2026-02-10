using System.Text.Json;

namespace Lilia.Core.Entities;

public class Template
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Thumbnail { get; set; }
    public JsonDocument Content { get; set; } = JsonDocument.Parse("{}");
    public bool IsPublic { get; set; }
    public bool IsSystem { get; set; }
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User? User { get; set; }
}

public static class TemplateCategories
{
    public const string Academic = "academic";
    public const string Research = "research";
    public const string Report = "report";
    public const string Thesis = "thesis";
    public const string Article = "article";
    public const string Presentation = "presentation";
    public const string Layout = "layout";
    public const string Other = "other";
}
