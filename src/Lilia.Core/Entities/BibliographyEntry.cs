using System.Text.Json;

namespace Lilia.Core.Entities;

public class BibliographyEntry
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string CiteKey { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty; // article, book, inproceedings, etc.
    public JsonDocument Data { get; set; } = JsonDocument.Parse("{}"); // title, authors, year, etc.
    public string? FormattedText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Document Document { get; set; } = null!;
}

public static class BibliographyEntryTypes
{
    public const string Article = "article";
    public const string Book = "book";
    public const string InProceedings = "inproceedings";
    public const string PhdThesis = "phdthesis";
    public const string MastersThesis = "mastersthesis";
    public const string TechReport = "techreport";
    public const string Misc = "misc";
    public const string Online = "online";
}
