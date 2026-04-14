using Lilia.Import.Models;

namespace Lilia.Import.Interfaces;

/// <summary>
/// Interface for exporting documents to DOCX format.
/// </summary>
public interface IDocxExportService
{
    /// <summary>
    /// Export a document to DOCX format.
    /// </summary>
    /// <param name="document">The document to export.</param>
    /// <param name="options">Optional export options.</param>
    /// <returns>The DOCX file as a byte array.</returns>
    Task<byte[]> ExportAsync(ExportDocument document, ExportOptions? options = null);
}

/// <summary>
/// Interface for converting LaTeX to OMML (Office Math Markup Language).
/// </summary>
public interface ILatexToOmmlConverter
{
    /// <summary>
    /// Convert LaTeX to OMML XML.
    /// </summary>
    /// <param name="latex">The LaTeX expression.</param>
    /// <returns>Tuple of (OMML XML, success, error message).</returns>
    (string Omml, bool Success, string? Error) Convert(string latex);
}

/// <summary>
/// Renders a LaTeX math fragment to a PNG image.
/// Used as fallback when OMML conversion fails for complex expressions.
/// Implemented in Lilia.Api using ILaTeXRenderService; injected as optional dependency.
/// </summary>
public interface IEquationImageRenderer
{
    /// <summary>
    /// Render a LaTeX fragment (e.g. <c>\frac{1}{2}</c>) to PNG bytes.
    /// Returns null when the renderer is unavailable or compilation fails.
    /// </summary>
    Task<byte[]?> RenderToPngAsync(string latexFragment, bool displayMode = true);
}
