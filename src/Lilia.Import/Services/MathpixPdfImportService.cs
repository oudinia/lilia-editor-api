using System.Text.RegularExpressions;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lilia.Import.Services;

/// <summary>
/// Parses PDF files via the Mathpix Convert API and maps Markdown output to ImportElement types,
/// producing an ImportDocument compatible with the existing review pipeline.
/// </summary>
public partial class MathpixPdfImportService : IPdfParser, IScanImageParser, IMathpixCharge
{
    private readonly IMathpixClient _mathpixClient;
    private readonly MathpixOptions _options;
    private readonly ILogger<MathpixPdfImportService> _logger;
    private readonly ILatexProjectExtractor _projectExtractor;

    public MathpixPdfImportService(
        IMathpixClient mathpixClient,
        IOptions<MathpixOptions> options,
        ILogger<MathpixPdfImportService> logger,
        ILatexProjectExtractor? projectExtractor = null)
    {
        _mathpixClient = mathpixClient;
        _options = options.Value;
        _logger = logger;
        _projectExtractor = projectExtractor ?? new LatexProjectExtractor();
    }

    /// <inheritdoc />
    public MathpixCharge? LastCharge { get; private set; }

    public bool CanParse(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ImportDocument> ParseAsync(string filePath, ImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mathpix] Starting parse of {FilePath}", filePath);

        // The tex.zip wait's deadline counts from here, the upload included (PDF import via tex.zip, 19 Nov 2026).
        var started = System.Diagnostics.Stopwatch.StartNew();

        // Step 1: Submit PDF to Mathpix
        LastCharge = null;
        var pdfId = await _mathpixClient.SubmitPdfAsync(filePath, cancellationToken);
        // Mathpix has the PDF and reads it whatever happens here next: from now on the import costs its pages, even if
        // it fails (PDF and scan import, 20 Nov 2026).
        LastCharge = new MathpixCharge(null);

        try
        {
            return await ReadResultAsync(pdfId, filePath, started, cancellationToken);
        }
        catch when (LastCharge is { Pages: null })
        {
            // A failed import still says how many pages Mathpix read, when Mathpix knows, before the PDF is deleted.
            try
            {
                LastCharge = new MathpixCharge((await _mathpixClient.GetStatusAsync(pdfId, CancellationToken.None)).NumPages);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Mathpix] Could not read the page count of failed PDF {PdfId}", pdfId);
            }
            throw;
        }
        finally
        {
            // Nothing stays at Mathpix once Lilia has the result, or once the import failed: the source, page images
            // and outputs are deleted (PDF import via tex.zip, 19 Nov 2026). After the Markdown's images are fetched,
            // since the delete removes them. Not cancellable: a cancelled import still deletes.
            if (_options.DeleteAfterImport)
            {
                try
                {
                    await _mathpixClient.DeletePdfAsync(pdfId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Mathpix] Could not delete PDF {PdfId} at Mathpix", pdfId);
                }
            }
        }
    }

    private async Task<ImportDocument> ReadResultAsync(string pdfId, string filePath, System.Diagnostics.Stopwatch started, CancellationToken cancellationToken)
    {
        // Step 2: Poll until complete, get Markdown
        var markdown = await _mathpixClient.WaitForCompletionAsync(pdfId, cancellationToken);

        _logger.LogInformation("[Mathpix] PDF {PdfId} completed, markdown length: {Length}", pdfId, markdown.Length);

        // The pages Mathpix read (and charges): what the plan's PDF pages count (the launch bot's #65, 11 Nov 2026:
        // an estimate from the file counted a 2-page PDF as 21).
        int? pages = null;
        try
        {
            pages = (await _mathpixClient.GetStatusAsync(pdfId, cancellationToken)).NumPages;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[Mathpix] Could not read the page count of {PdfId}", pdfId);
        }
        LastCharge = new MathpixCharge(pages);

        // Step 2b: the LaTeX project (tex.zip), imported as an Overleaf zip is; the Markdown below when it fails or
        // takes too long (PDF import via tex.zip, 19 Nov 2026).
        // The import runs inside the web request, which Cloudflare cuts at 100 s: the tex.zip wait ends by
        // TexZipDeadlineSeconds from the upload, and a Markdown that took that long leaves it no time at all.
        var left = TimeSpan.FromSeconds(_options.TexZipDeadlineSeconds) - started.Elapsed;
        if (_options.RequestTexZip && left <= TimeSpan.Zero)
            _logger.LogWarning("[Mathpix] PDF {PdfId}: {Seconds:F0}s already spent, no time left for the tex.zip; using the Markdown",
                pdfId, started.Elapsed.TotalSeconds);
        else if (_options.RequestTexZip && await TryReadTexZipAsync(pdfId, left, cancellationToken) is { } project)
        {
            _logger.LogInformation("[Mathpix] PDF {PdfId} imported from its tex.zip ({Bytes} bytes)", pdfId, project.Zip.Length);
            return new ImportDocument
            {
                SourcePath = filePath,
                Metadata = new ImportMetadata(),
                RawImportData = markdown,
                PageCount = pages,
                LatexProjectZip = project.Zip,
                LatexSource = project.Tex,
            };
        }

        // Step 3: Parse Markdown into ImportElements
        var elements = await ParseMarkdownAsync(markdown, cancellationToken);

        var warnings = new List<ImportWarning>();

        // Step 4: Post-process for abstract/bibliography/theorem detection
        elements = HeadingLevels.Promote(PdfImportService.PostProcess(elements, warnings));

        // Step 5: Extract title
        var title = PdfImportService.ExtractTitle(elements);

        var doc = new ImportDocument
        {
            SourcePath = filePath,
            Title = title,
            Elements = elements,
            Warnings = warnings,
            Metadata = new ImportMetadata(),
            RawImportData = markdown,
            PageCount = pages,
        };

        _logger.LogInformation("[Mathpix] Parsed {ElementCount} elements, title: \"{Title}\"", elements.Count, title);
        return doc;
    }

    /// <summary>
    /// One scanned page (PDF and scan import, 20 Nov 2026), wrapped in a one-page PDF (<see cref="ImagePdf"/>) and read as a
    /// PDF is: its tex.zip, or its Markdown, with the figures Mathpix cropped. One page for the plan.
    /// </summary>
    public async Task<ImportDocument> ParseImageAsync(string filePath, string contentType, CancellationToken cancellationToken = default)
    {
        // Through /v3/pdf as a one-page PDF (figures in imported scans, 20 Nov 2026): /v3/text, the route until then, doesn't
        // crop figures ("does not crop sub-regions or upload them to our CDN", docs.mathpix.com/reference/post-v3-text), and
        // the launch bot's handwritten page lost its two sketches. /v3/pdf crops them into its tex.zip and its Markdown.
        LastCharge = null;
        var pdf = ImagePdf.Wrap(await File.ReadAllBytesAsync(filePath, cancellationToken), contentType)
            ?? throw new LiliaValidationException(
                "Lilia couldn't read this image: is it a complete JPEG or PNG file?", LiliaErrorCodes.ValidationFailed);
        var pdfPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        try
        {
            await File.WriteAllBytesAsync(pdfPath, pdf, cancellationToken);
            var document = await ParseAsync(pdfPath, null, cancellationToken);
            document.SourcePath = filePath;
            document.PageCount = 1;
            return document;
        }
        finally
        {
            try { File.Delete(pdfPath); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The PDF's tex.zip and its flattened .tex, or null (logged) when the conversion failed, timed out, or the zip
    /// isn't a LaTeX project the extractor reads (PDF import via tex.zip, 19 Nov 2026).
    /// </summary>
    private async Task<(byte[] Zip, string Tex)?> TryReadTexZipAsync(string pdfId, TimeSpan maxWait, CancellationToken ct)
    {
        try
        {
            var zip = await _mathpixClient.GetTexZipAsync(pdfId, ct, maxWait);
            if (zip is not { Length: > 0 })
            {
                _logger.LogWarning("[Mathpix] PDF {PdfId}: the tex.zip is empty; using the Markdown", pdfId);
                return null;
            }
            var extracted = _projectExtractor.Extract(zip);
            if (string.IsNullOrWhiteSpace(extracted.InlinedTex))
            {
                _logger.LogWarning("[Mathpix] PDF {PdfId}: the tex.zip has no .tex to read; using the Markdown", pdfId);
                return null;
            }
            // A figure without a caption is a bare \includegraphics in Mathpix's LaTeX: set it in a figure environment, so it
            // is read as a figure block and its image stored, not its id printed as text (20 Nov 2026).
            return (zip, MathpixFigures.AsFigureEnvironments(extracted.InlinedTex));
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "[Mathpix] PDF {PdfId}: no usable tex.zip; using the Markdown", pdfId);
            return null;
        }
    }

    internal async Task<List<ImportElement>> ParseMarkdownAsync(string markdown, CancellationToken ct = default)
    {
        var elements = new List<ImportElement>();
        // An image link in the middle of a line is a figure of its own, never a paragraph's text (20 Nov 2026).
        var lines = MathpixFigures.ImagesOnTheirOwnLines(markdown).Split('\n');
        var order = 0;
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Display equation: $$ ... $$
            if (line.TrimStart().StartsWith("$$"))
            {
                var (equation, newIndex) = ParseDisplayEquation(lines, i);
                if (equation != null)
                {
                    equation.Order = order++;
                    elements.Add(equation);
                    i = newIndex;
                    continue;
                }
            }

            // Heading: # ... (ATX headings)
            var headingMatch = HeadingRegex().Match(line);
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value.Trim();
                elements.Add(new ImportHeading { Level = level, Text = text, Order = order++ });
                i++;
                continue;
            }

            // Fenced code block: ``` ... ```
            if (line.TrimStart().StartsWith("```"))
            {
                var (codeBlock, newIndex) = ParseCodeBlock(lines, i);
                codeBlock.Order = order++;
                elements.Add(codeBlock);
                i = newIndex;
                continue;
            }

            // Image: ![alt](url)
            var imageMatch = ImageRegex().Match(line);
            if (imageMatch.Success)
            {
                var altText = imageMatch.Groups[1].Value;
                var imageUrl = imageMatch.Groups[2].Value;

                byte[] imageData = [];
                var mimeType = PdfImportService.GuessMimeType(imageUrl);

                // A figure Mathpix cropped, from its CDN only (figures in imported scans, 20 Nov 2026). Anything else is never
                // fetched; a figure that couldn't be fetched stays a figure, empty and flagged in the review, never text.
                if (MathpixClient.IsMathpixImage(imageUrl))
                {
                    try
                    {
                        imageData = await _mathpixClient.GetImageAsync(imageUrl, ct);
                        mimeType = MathpixClient.ImageType(imageData) ?? mimeType;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "[Mathpix] Failed to fetch a cropped figure");
                        imageData = [];
                    }
                }
                else
                {
                    _logger.LogWarning("[Mathpix] An image link not on cdn.mathpix.com was not fetched");
                }

                elements.Add(new ImportImage
                {
                    Data = imageData,
                    MimeType = mimeType,
                    Filename = MathpixClient.IsMathpixImage(imageUrl) ? Path.GetFileName(new Uri(imageUrl).LocalPath) : null,
                    // The alt text is Mathpix's (a chemical diagram's SMILES); never the address or the figure's id.
                    AltText = !string.IsNullOrWhiteSpace(altText) && !MathpixFigures.HasRawReference(altText) ? altText : null,
                    Order = order++
                });
                i++;
                continue;
            }

            // Pipe table: | col | col |
            if (line.TrimStart().StartsWith('|') && i + 1 < lines.Length && SeparatorRowRegex().IsMatch(lines[i + 1]))
            {
                var (table, newIndex) = ParsePipeTable(lines, i);
                table.Order = order++;
                elements.Add(table);
                i = newIndex;
                continue;
            }

            // LaTeX table: \begin{tabular}
            if (line.TrimStart().StartsWith("\\begin{tabular"))
            {
                var (table, newIndex) = ParseLatexTable(lines, i);
                table.Order = order++;
                elements.Add(table);
                i = newIndex;
                continue;
            }

            // Unordered list item: - item or * item
            if (BulletItemRegex().IsMatch(line))
            {
                var (items, newIndex) = ParseListItems(lines, i, numbered: false);
                foreach (var item in items)
                {
                    item.Order = order++;
                    elements.Add(item);
                }
                i = newIndex;
                continue;
            }

            // Ordered list item: 1. item
            if (NumberedItemRegex().IsMatch(line))
            {
                var (items, newIndex) = ParseListItems(lines, i, numbered: true);
                foreach (var item in items)
                {
                    item.Order = order++;
                    elements.Add(item);
                }
                i = newIndex;
                continue;
            }

            // Default: paragraph (accumulate consecutive non-empty, non-special lines)
            var paragraphText = ParseParagraph(lines, ref i);
            if (!string.IsNullOrWhiteSpace(paragraphText))
            {
                elements.Add(new ImportParagraph { Text = paragraphText.Trim(), Order = order++ });
            }
        }

        return elements;
    }

    /// <summary>
    /// The PDF's own equation number (Mathpix writes it as <c>\tag{1}</c>) comes off: Lilia numbers the equation, and
    /// both showed, "(1)" beside Lilia's own (the launch bot's #65, 11 Nov 2026).
    /// </summary>
    internal static string WithoutTag(string latex) =>
        System.Text.RegularExpressions.Regex.Replace(latex, @"\\tag\*?\s*\{[^{}]*\}", "").Trim();

    private static (ImportEquation?, int) ParseDisplayEquation(string[] lines, int startIndex)
    {
        var line = lines[startIndex].TrimStart();

        // Single-line: $$ equation $$
        if (line.StartsWith("$$") && line.Length > 2)
        {
            var rest = line[2..];
            var endIdx = rest.IndexOf("$$", StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                var latex = rest[..endIdx].Trim();
                return (new ImportEquation
                {
                    LatexContent = WithoutTag(latex),
                    ConversionSucceeded = true,
                    IsInline = false
                }, startIndex + 1);
            }
        }

        // Multi-line: $$ ... $$
        var content = new List<string>();
        var firstLine = line[2..].Trim();
        if (!string.IsNullOrEmpty(firstLine))
            content.Add(firstLine);

        for (var j = startIndex + 1; j < lines.Length; j++)
        {
            var current = lines[j];
            var trimmed = current.TrimEnd();

            if (trimmed.EndsWith("$$"))
            {
                var last = trimmed[..^2].Trim();
                if (!string.IsNullOrEmpty(last))
                    content.Add(last);

                var latex = string.Join("\n", content).Trim();
                return (new ImportEquation
                {
                    LatexContent = WithoutTag(latex),
                    ConversionSucceeded = true,
                    IsInline = false
                }, j + 1);
            }

            content.Add(current);
        }

        // No closing $$ found — treat as paragraph
        return (null, startIndex + 1);
    }

    private static (ImportCodeBlock, int) ParseCodeBlock(string[] lines, int startIndex)
    {
        var firstLine = lines[startIndex].TrimStart();
        var language = firstLine.Length > 3 ? firstLine[3..].Trim() : null;
        var codeLines = new List<string>();

        for (var j = startIndex + 1; j < lines.Length; j++)
        {
            if (lines[j].TrimStart().StartsWith("```"))
            {
                return (new ImportCodeBlock
                {
                    Text = string.Join("\n", codeLines),
                    Language = !string.IsNullOrWhiteSpace(language) ? language : null,
                    DetectionReason = CodeBlockDetectionReason.StyleName
                }, j + 1);
            }
            codeLines.Add(lines[j]);
        }

        // No closing ``` found
        return (new ImportCodeBlock
        {
            Text = string.Join("\n", codeLines),
            Language = !string.IsNullOrWhiteSpace(language) ? language : null,
            DetectionReason = CodeBlockDetectionReason.StyleName
        }, lines.Length);
    }

    private static (ImportTable, int) ParsePipeTable(string[] lines, int startIndex)
    {
        var rows = new List<List<ImportTableCell>>();
        var hasHeader = false;
        var i = startIndex;

        // Parse header row
        if (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
        {
            rows.Add(ParsePipeRow(lines[i]));
            i++;
        }

        // Skip separator row (|---|---|)
        if (i < lines.Length && SeparatorRowRegex().IsMatch(lines[i]))
        {
            hasHeader = true;
            i++;
        }

        // Parse data rows
        while (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
        {
            rows.Add(ParsePipeRow(lines[i]));
            i++;
        }

        return (new ImportTable { Rows = rows, HasHeaderRow = hasHeader }, i);
    }

    private static List<ImportTableCell> ParsePipeRow(string line)
    {
        var cells = new List<ImportTableCell>();
        var trimmed = line.Trim();

        // Remove leading and trailing pipes
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];

        var parts = trimmed.Split('|');
        foreach (var part in parts)
        {
            cells.Add(new ImportTableCell { Text = part.Trim() });
        }

        return cells;
    }

    private static (ImportTable, int) ParseLatexTable(string[] lines, int startIndex)
    {
        // Collect the full LaTeX table and create a single-cell fallback
        var tableLines = new List<string>();

        for (var j = startIndex; j < lines.Length; j++)
        {
            tableLines.Add(lines[j]);
            if (lines[j].TrimStart().StartsWith("\\end{tabular"))
            {
                return (new ImportTable
                {
                    Rows = [[new ImportTableCell { Text = string.Join("\n", tableLines) }]],
                    HasHeaderRow = false
                }, j + 1);
            }
        }

        return (new ImportTable
        {
            Rows = [[new ImportTableCell { Text = string.Join("\n", tableLines) }]],
            HasHeaderRow = false
        }, lines.Length);
    }

    private static (List<ImportListItem>, int) ParseListItems(string[] lines, int startIndex, bool numbered)
    {
        var items = new List<ImportListItem>();
        var i = startIndex;
        var pattern = numbered ? NumberedItemRegex() : BulletItemRegex();

        while (i < lines.Length)
        {
            var match = pattern.Match(lines[i]);
            if (!match.Success)
                break;

            var text = lines[i][match.Length..].Trim();
            var indent = lines[i].Length - lines[i].TrimStart().Length;
            var level = indent / 2; // Approximate nesting level from indentation

            items.Add(new ImportListItem
            {
                Text = text,
                Level = level,
                IsNumbered = numbered
            });
            i++;
        }

        return (items, i);
    }

    private static string ParseParagraph(string[] lines, ref int index)
    {
        var paragraphLines = new List<string>();

        while (index < lines.Length)
        {
            var line = lines[index];

            // Stop at empty lines
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                break;
            }

            // Stop at special block starters
            if (line.TrimStart().StartsWith('#') ||
                line.TrimStart().StartsWith("$$") ||
                line.TrimStart().StartsWith("```") ||
                line.TrimStart().StartsWith("![") ||
                line.TrimStart().StartsWith("\\begin{tabular") ||
                (line.TrimStart().StartsWith('|') && index + 1 < lines.Length && SeparatorRowRegex().IsMatch(lines[index + 1])) ||
                BulletItemRegex().IsMatch(line) ||
                NumberedItemRegex().IsMatch(line))
            {
                break;
            }

            paragraphLines.Add(line);
            index++;
        }

        return string.Join(" ", paragraphLines);
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^)]+)\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"^\|[\s\-:]+\|")]
    private static partial Regex SeparatorRowRegex();

    [GeneratedRegex(@"^\s*[\-\*\+]\s+")]
    private static partial Regex BulletItemRegex();

    [GeneratedRegex(@"^\s*\d+[\.\)]\s+")]
    private static partial Regex NumberedItemRegex();
}
