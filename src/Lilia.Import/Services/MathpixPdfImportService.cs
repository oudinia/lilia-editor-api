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
public partial class MathpixPdfImportService : IPdfParser
{
    private readonly IMathpixClient _mathpixClient;
    private readonly MathpixOptions _options;
    private readonly ILogger<MathpixPdfImportService> _logger;

    public MathpixPdfImportService(
        IMathpixClient mathpixClient,
        IOptions<MathpixOptions> options,
        ILogger<MathpixPdfImportService> logger)
    {
        _mathpixClient = mathpixClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool CanParse(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ImportDocument> ParseAsync(string filePath, ImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mathpix] Starting parse of {FilePath}", filePath);

        // Step 1: Submit PDF to Mathpix
        var pdfId = await _mathpixClient.SubmitPdfAsync(filePath, cancellationToken);

        // Step 2: Poll until complete, get Markdown
        var markdown = await _mathpixClient.WaitForCompletionAsync(pdfId, cancellationToken);

        _logger.LogInformation("[Mathpix] PDF {PdfId} completed, markdown length: {Length}", pdfId, markdown.Length);

        // Step 3: Parse Markdown into ImportElements
        var elements = await ParseMarkdownAsync(markdown, cancellationToken);

        var warnings = new List<ImportWarning>();

        // Step 4: Post-process for abstract/bibliography/theorem detection
        elements = PdfImportService.PostProcess(elements, warnings);

        // Step 5: Extract title
        var title = PdfImportService.ExtractTitle(elements);

        var doc = new ImportDocument
        {
            SourcePath = filePath,
            Title = title,
            Elements = elements,
            Warnings = warnings,
            Metadata = new ImportMetadata(),
            RawImportData = markdown
        };

        _logger.LogInformation("[Mathpix] Parsed {ElementCount} elements, title: \"{Title}\"", elements.Count, title);
        return doc;
    }

    internal async Task<List<ImportElement>> ParseMarkdownAsync(string markdown, CancellationToken ct = default)
    {
        var elements = new List<ImportElement>();
        var lines = markdown.Split('\n');
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

                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    try
                    {
                        imageData = await _mathpixClient.GetImageAsync(imageUrl, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Mathpix] Failed to fetch image: {Url}", imageUrl);
                    }
                }

                elements.Add(new ImportImage
                {
                    Data = imageData,
                    MimeType = mimeType,
                    Filename = !string.IsNullOrWhiteSpace(imageUrl) ? Path.GetFileName(new Uri(imageUrl, UriKind.RelativeOrAbsolute).LocalPath) : null,
                    AltText = !string.IsNullOrWhiteSpace(altText) ? altText : null,
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
                    LatexContent = latex,
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
                    LatexContent = latex,
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
