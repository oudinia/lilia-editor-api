namespace Lilia.Import.Models;

/// <summary>
/// Represents a formatting span within text content.
/// Tracks the start position, length, and type of formatting applied.
/// </summary>
public class FormattingSpan
{
    /// <summary>
    /// Start index within the text (0-based).
    /// </summary>
    public int Start { get; set; }

    /// <summary>
    /// Length of the formatted span in characters.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Type of formatting applied.
    /// </summary>
    public FormattingType Type { get; set; }

    /// <summary>
    /// Optional value for the formatting (e.g., color hex, font size, font name).
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// End index (exclusive) of the span.
    /// </summary>
    public int End => Start + Length;

    public FormattingSpan() { }

    public FormattingSpan(int start, int length, FormattingType type, string? value = null)
    {
        Start = start;
        Length = length;
        Type = type;
        Value = value;
    }

    /// <summary>
    /// Check if this span overlaps with another span.
    /// </summary>
    public bool Overlaps(FormattingSpan other)
    {
        return Start < other.End && End > other.Start;
    }

    /// <summary>
    /// Check if this span contains a position.
    /// </summary>
    public bool Contains(int position)
    {
        return position >= Start && position < End;
    }

    public override string ToString()
    {
        return $"{Type}[{Start}..{End}]{(Value != null ? $"={Value}" : "")}";
    }
}
