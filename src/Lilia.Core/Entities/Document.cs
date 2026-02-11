namespace Lilia.Core.Entities;

public class Document
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public Guid? TeamId { get; set; }
    public string Title { get; set; } = "Untitled";
    public string Language { get; set; } = "en";
    public string PaperSize { get; set; } = "a4";
    public string FontFamily { get; set; } = "serif";
    public int FontSize { get; set; } = 12;
    public int Columns { get; set; } = 1;
    public string ColumnSeparator { get; set; } = "none";
    public double ColumnGap { get; set; } = 1.5;
    public bool IsPublic { get; set; }
    public string? ShareLink { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastOpenedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string Status { get; set; } = "draft"; // draft, saved, published
    public DateTime? LastAutoSavedAt { get; set; }

    // Navigation properties
    public virtual User Owner { get; set; } = null!;
    public virtual Team? Team { get; set; }
    public virtual ICollection<Block> Blocks { get; set; } = new List<Block>();
    public virtual ICollection<BibliographyEntry> BibliographyEntries { get; set; } = new List<BibliographyEntry>();
    public virtual ICollection<DocumentLabel> DocumentLabels { get; set; } = new List<DocumentLabel>();
    public virtual ICollection<DocumentCollaborator> Collaborators { get; set; } = new List<DocumentCollaborator>();
    public virtual ICollection<DocumentGroup> DocumentGroups { get; set; } = new List<DocumentGroup>();
    public virtual ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public virtual ICollection<DocumentSnapshot> Snapshots { get; set; } = new List<DocumentSnapshot>();
}
