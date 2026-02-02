namespace Lilia.Import.Models;

/// <summary>
/// The intermediate representation of a DOCX document after parsing.
/// Contains all extracted elements with their formatting metadata,
/// ready for conversion to Lilia's native format.
/// </summary>
public class ImportDocument
{
    /// <summary>
    /// Path to the source DOCX file.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Document title (extracted from document properties or first heading).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// All elements in document order.
    /// </summary>
    public List<ImportElement> Elements { get; set; } = [];

    /// <summary>
    /// Warnings generated during parsing.
    /// </summary>
    public List<ImportWarning> Warnings { get; set; } = [];

    /// <summary>
    /// Document metadata.
    /// </summary>
    public ImportMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Get all elements of a specific type.
    /// </summary>
    public IEnumerable<T> GetElements<T>() where T : ImportElement
    {
        return Elements.OfType<T>();
    }

    /// <summary>
    /// Get count of elements by type.
    /// </summary>
    public Dictionary<ImportElementType, int> GetElementCounts()
    {
        return Elements
            .GroupBy(e => e.Type)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Check if there are any warnings of a specific type.
    /// </summary>
    public bool HasWarnings(ImportWarningType type)
    {
        return Warnings.Any(w => w.Type == type);
    }

    /// <summary>
    /// Get a summary of the imported document.
    /// </summary>
    public string GetSummary()
    {
        var counts = GetElementCounts();
        var parts = counts.Select(kv => $"{kv.Value} {kv.Key}(s)");
        var warningCount = Warnings.Count;
        return $"Imported: {string.Join(", ", parts)}. Warnings: {warningCount}";
    }
}

/// <summary>
/// Metadata extracted from the DOCX document properties.
/// </summary>
public class ImportMetadata
{
    /// <summary>
    /// Document author (dc:creator).
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Document subject (dc:subject).
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Document description (dc:description).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Keywords (cp:keywords).
    /// </summary>
    public string? Keywords { get; set; }

    /// <summary>
    /// Created date (dcterms:created).
    /// </summary>
    public DateTime? Created { get; set; }

    /// <summary>
    /// Modified date (dcterms:modified).
    /// </summary>
    public DateTime? Modified { get; set; }

    /// <summary>
    /// Application that created the document (e.g., "Microsoft Office Word", "Google Docs").
    /// </summary>
    public string? Application { get; set; }

    /// <summary>
    /// Application version.
    /// </summary>
    public string? AppVersion { get; set; }

    /// <summary>
    /// Whether this appears to be a Google Docs export.
    /// </summary>
    public bool IsGoogleDocsExport =>
        Application?.Contains("Google", StringComparison.OrdinalIgnoreCase) == true;
}
