using System.Text.Json;

namespace Lilia.Core.Entities;

public class Block
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string Type { get; set; } = string.Empty;
    public JsonDocument Content { get; set; } = JsonDocument.Parse("{}");
    public int SortOrder { get; set; }
    public Guid? ParentId { get; set; }
    public int Depth { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    // Navigation properties
    public virtual Document Document { get; set; } = null!;
    public virtual Block? Parent { get; set; }
    public virtual ICollection<Block> Children { get; set; } = new List<Block>();
    public virtual User? Creator { get; set; }
    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
}

public static class BlockTypes
{
    public const string Heading = "heading";
    public const string Paragraph = "paragraph";
    public const string Equation = "equation";
    public const string List = "list";
    public const string Code = "code";
    public const string Blockquote = "blockquote";
    public const string Figure = "figure";
    public const string Table = "table";
    public const string Theorem = "theorem";
    public const string Abstract = "abstract";
    public const string Bibliography = "bibliography";
    public const string TableOfContents = "tableOfContents";
    public const string PageBreak = "pageBreak";
    public const string ColumnBreak = "columnBreak";
    public const string Embed = "embed";
    public const string Footnote = "footnote";

    // Legacy aliases for backwards compatibility with existing data
    public const string Quote = "quote";
    public const string Image = "image";
    public const string Divider = "divider";

    public static readonly string[] All =
    [
        Paragraph, Heading, Equation, Figure, Code, List,
        Blockquote, Table, Theorem, Abstract, Bibliography,
        TableOfContents, PageBreak, ColumnBreak, Embed, Footnote
    ];
}
