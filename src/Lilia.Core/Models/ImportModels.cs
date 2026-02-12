namespace Lilia.Core.Models;

/// <summary>
/// Document model for import/export operations.
/// Different from Lilia.Core.Entities.Document which is the database entity.
/// </summary>
public class Document
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public int SchemaVersion { get; set; } = 1;

    public List<Section> Sections { get; set; } = [];
}

public class Section
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Block> Blocks { get; set; } = [];
    public List<Section> Children { get; set; } = [];
}

public enum BlockType
{
    Paragraph,
    Equation,
    Figure,
    Table,
    Code,
    PageBreak,
    Abstract,
    Blockquote,
    Theorem,
    Bibliography,
    List
}

public class Block
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public BlockType BlockType { get; set; }
    public string Content { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    public List<Asset> Assets { get; set; } = [];
}

public class Asset
{
    public int Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public byte[] Data { get; set; } = [];
    public string Hash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
