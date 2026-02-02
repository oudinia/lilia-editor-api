using System.Diagnostics;
using Lilia.Import.Converters;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Service that orchestrates the full DOCX import process.
/// Combines parsing (Stage 1) and conversion (Stage 2) into a single workflow.
/// </summary>
public class DocxImportService : IDocxImportService
{
    private readonly IDocxParser _parser;
    private readonly IOmmlConverter _ommlConverter;
    private readonly IImportConverter _documentConverter;

    private static readonly string[] _supportedExtensions = [".docx"];

    /// <summary>
    /// Create a new DocxImportService with default implementations.
    /// </summary>
    public DocxImportService()
        : this(new DocxParser(new OmmlToLatexConverter()), new LiliaDocumentConverter())
    {
    }

    /// <summary>
    /// Create a new DocxImportService with custom implementations.
    /// </summary>
    public DocxImportService(IDocxParser parser, IImportConverter documentConverter)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _documentConverter = documentConverter ?? throw new ArgumentNullException(nameof(documentConverter));
        _ommlConverter = new OmmlToLatexConverter();
    }

    /// <summary>
    /// Create a new DocxImportService with all custom implementations.
    /// </summary>
    public DocxImportService(IDocxParser parser, IOmmlConverter ommlConverter, IImportConverter documentConverter)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _ommlConverter = ommlConverter ?? throw new ArgumentNullException(nameof(ommlConverter));
        _documentConverter = documentConverter ?? throw new ArgumentNullException(nameof(documentConverter));
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

    /// <inheritdoc/>
    public bool CanImport(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var extension = Path.GetExtension(filePath);
        return _supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<ImportResult> ImportAsync(
        string filePath,
        ImportOptions? importOptions = null,
        ConversionOptions? conversionOptions = null)
    {
        var stopwatch = Stopwatch.StartNew();

        // Validate file
        if (!CanImport(filePath))
        {
            return ImportResult.Failed($"File type not supported: {Path.GetExtension(filePath)}");
        }

        if (!File.Exists(filePath))
        {
            return ImportResult.Failed($"File not found: {filePath}");
        }

        try
        {
            // Stage 1: Parse DOCX to intermediate model
            var parseStart = Stopwatch.StartNew();
            var importDocument = await _parser.ParseAsync(filePath, importOptions);
            var parseTime = parseStart.Elapsed;

            // Stage 2: Convert to Lilia format
            var convertStart = Stopwatch.StartNew();
            var result = _documentConverter.Convert(importDocument, conversionOptions);
            var convertTime = convertStart.Elapsed;

            // Update statistics with timing info
            result.Statistics.ParseTime = parseTime;
            result.Statistics.ConvertTime = convertTime;

            return result;
        }
        catch (Exception ex)
        {
            return ImportResult.Failed($"Import failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ImportDocument> ParseAsync(string filePath, ImportOptions? options = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        return await _parser.ParseAsync(filePath, options);
    }

    /// <inheritdoc/>
    public ImportResult Convert(ImportDocument importDocument, ConversionOptions? options = null)
    {
        if (importDocument == null)
            throw new ArgumentNullException(nameof(importDocument));

        return _documentConverter.Convert(importDocument, options);
    }
}
