namespace Lilia.Import.Models;

/// <summary>
/// Represents a warning or issue encountered during DOCX import.
/// </summary>
public class ImportWarning
{
    /// <summary>
    /// Human-readable description of the warning.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Type/category of the warning.
    /// </summary>
    public ImportWarningType Type { get; set; }

    /// <summary>
    /// Optional: Index of the element where the warning occurred.
    /// </summary>
    public int? ElementIndex { get; set; }

    /// <summary>
    /// Optional: Additional context or details.
    /// </summary>
    public string? Details { get; set; }

    public ImportWarning() { }

    public ImportWarning(ImportWarningType type, string message, int? elementIndex = null, string? details = null)
    {
        Type = type;
        Message = message;
        ElementIndex = elementIndex;
        Details = details;
    }

    public override string ToString()
    {
        var location = ElementIndex.HasValue ? $" at element {ElementIndex}" : "";
        return $"[{Type}]{location}: {Message}";
    }
}
