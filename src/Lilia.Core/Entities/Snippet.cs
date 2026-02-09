namespace Lilia.Core.Entities;

public class Snippet
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LatexContent { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<string> RequiredPackages { get; set; } = new();
    public string? Preamble { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsFavorite { get; set; }
    public bool IsSystem { get; set; }
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User? User { get; set; }
}

public static class SnippetCategories
{
    public const string Tables = "tables";
    public const string Figures = "figures";
    public const string Academic = "academic";
    public const string Code = "code";
    public const string Structure = "structure";
}

public static class SnippetBlockTypes
{
    public const string Table = "table";
    public const string Figure = "figure";
    public const string Theorem = "theorem";
    public const string Code = "code";
    public const string List = "list";
    public const string Blockquote = "blockquote";
    public const string Abstract = "abstract";
    public const string TableOfContents = "tableOfContents";
    public const string PageBreak = "pageBreak";
}
