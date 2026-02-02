namespace Lilia.Import.Interfaces;

/// <summary>
/// Interface for converting Office Math Markup Language (OMML) to LaTeX.
/// </summary>
public interface IOmmlConverter
{
    /// <summary>
    /// Convert OMML XML to LaTeX string.
    /// </summary>
    /// <param name="ommlXml">The OMML XML content.</param>
    /// <returns>A tuple with the LaTeX string and success flag.</returns>
    (string latex, bool success, string? error) Convert(string ommlXml);
}
