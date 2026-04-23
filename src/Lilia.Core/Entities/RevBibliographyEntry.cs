using System.Text.Json;

namespace Lilia.Core.Entities;

/// <summary>
/// Mirror of <see cref="BibliographyEntry"/> inside the import domain
/// (FT-IMP-001). Parsed citations (from inline \cite trails, a .bib
/// attached to an Overleaf project, or Word reference fields) land
/// here before checkout. CiteKey is the user-visible identifier
/// (\cite{knuth1984}); Data is the full record as jsonb — authors,
/// title, year, DOI, etc.
/// </summary>
public class RevBibliographyEntry
{
    public Guid Id { get; set; }

    /// <summary>
    /// Owning rev-document. Cascade on doc purge.
    /// </summary>
    public Guid RevDocumentId { get; set; }
    public virtual RevDocument RevDocument { get; set; } = null!;

    public string CiteKey { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public JsonDocument Data { get; set; } = JsonDocument.Parse("{}");
    public string? FormattedText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
