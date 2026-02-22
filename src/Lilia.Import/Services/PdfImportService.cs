using System.Text.RegularExpressions;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.Extensions.Logging;

namespace Lilia.Import.Services;

/// <summary>
/// Parses PDF files via the MinerU sidecar and maps output to ImportElement types,
/// producing an ImportDocument compatible with the existing review pipeline.
/// </summary>
public partial class PdfImportService : IPdfParser
{
    private readonly IMineruClient _mineruClient;
    private readonly ILogger<PdfImportService> _logger;

    public PdfImportService(IMineruClient mineruClient, ILogger<PdfImportService> logger)
    {
        _mineruClient = mineruClient;
        _logger = logger;
    }

    public bool CanParse(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ImportDocument> ParseAsync(string filePath, ImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[PDF] Starting parse of {FilePath}", filePath);

        var response = await _mineruClient.ParsePdfAsync(filePath, cancellationToken);

        var elements = new List<ImportElement>();
        var warnings = new List<ImportWarning>();
        var order = 0;

        foreach (var block in response.ContentList)
        {
            var mapped = await MapBlockAsync(block, response, options, cancellationToken);
            if (mapped != null)
            {
                foreach (var element in mapped)
                {
                    element.Order = order++;
                    elements.Add(element);
                }
            }
        }

        // Post-processing pass: detect abstract, bibliography, and theorem sections
        elements = PostProcess(elements, warnings);

        // Extract title from first heading or first text block
        var title = ExtractTitle(elements);

        var doc = new ImportDocument
        {
            SourcePath = filePath,
            Title = title,
            Elements = elements,
            Warnings = warnings,
            Metadata = new ImportMetadata()
        };

        _logger.LogInformation("[PDF] Parsed {ElementCount} elements, title: \"{Title}\"", elements.Count, title);
        return doc;
    }

    private async Task<List<ImportElement>?> MapBlockAsync(
        MineruContentBlock block,
        MineruParseResponse response,
        ImportOptions? options,
        CancellationToken cancellationToken)
    {
        switch (block.Type.ToLowerInvariant())
        {
            case "text":
                return [MapTextBlock(block)];

            case "equation":
                return [MapEquationBlock(block)];

            case "table":
                return [MapTableBlock(block)];

            case "image":
                var imageElement = await MapImageBlockAsync(block, response, cancellationToken);
                return imageElement != null ? [imageElement] : null;

            case "code":
                return [MapCodeBlock(block)];

            case "list":
                return MapListBlock(block);

            default:
                _logger.LogDebug("[PDF] Skipping unknown MinerU block type: {Type}", block.Type);
                return null;
        }
    }

    private static ImportElement MapTextBlock(MineruContentBlock block)
    {
        if (block.TextLevel.HasValue && block.TextLevel.Value >= 1)
        {
            return new ImportHeading
            {
                Level = block.TextLevel.Value,
                Text = block.Text.Trim()
            };
        }

        return new ImportParagraph
        {
            Text = block.Text.Trim()
        };
    }

    private static ImportEquation MapEquationBlock(MineruContentBlock block)
    {
        return new ImportEquation
        {
            LatexContent = block.Text.Trim(),
            ConversionSucceeded = true,
            IsInline = false
        };
    }

    private static ImportTable MapTableBlock(MineruContentBlock block)
    {
        if (!string.IsNullOrWhiteSpace(block.TableBody))
        {
            var (rows, hasHeader) = HtmlTableParser.Parse(block.TableBody);
            return new ImportTable
            {
                Rows = rows,
                HasHeaderRow = hasHeader
            };
        }

        // Fallback: single-cell table with text content
        return new ImportTable
        {
            Rows = [[new ImportTableCell { Text = block.Text.Trim() }]],
            HasHeaderRow = false
        };
    }

    private async Task<ImportImage?> MapImageBlockAsync(
        MineruContentBlock block,
        MineruParseResponse response,
        CancellationToken cancellationToken)
    {
        byte[] imageData = [];
        var mimeType = "image/png";

        // Try to get image from the base64 images dictionary first
        if (!string.IsNullOrWhiteSpace(block.ImgPath) && response.Images.TryGetValue(block.ImgPath, out var base64Data))
        {
            try
            {
                imageData = Convert.FromBase64String(base64Data);
                mimeType = GuessMimeType(block.ImgPath);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "[PDF] Failed to decode base64 image: {ImgPath}", block.ImgPath);
            }
        }
        // Fall back to fetching from MinerU HTTP endpoint
        else if (!string.IsNullOrWhiteSpace(block.ImgPath))
        {
            imageData = await _mineruClient.GetImageAsync(block.ImgPath, cancellationToken);
            mimeType = GuessMimeType(block.ImgPath);
        }

        return new ImportImage
        {
            Data = imageData,
            MimeType = mimeType,
            Filename = block.ImgPath != null ? Path.GetFileName(block.ImgPath) : null,
            AltText = block.ImageCaption?.Trim()
        };
    }

    private static ImportCodeBlock MapCodeBlock(MineruContentBlock block)
    {
        return new ImportCodeBlock
        {
            Text = block.Text.Trim(),
            DetectionReason = CodeBlockDetectionReason.StyleName
        };
    }

    private static List<ImportElement> MapListBlock(MineruContentBlock block)
    {
        var text = block.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = new List<ImportElement>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var isNumbered = NumberedListRegex().IsMatch(trimmed);
            var itemText = isNumbered
                ? NumberedListRegex().Replace(trimmed, "", 1).Trim()
                : BulletListRegex().Replace(trimmed, "", 1).Trim();

            items.Add(new ImportListItem
            {
                Text = itemText,
                Level = 0,
                IsNumbered = isNumbered
            });
        }

        return items;
    }

    /// <summary>
    /// Post-processing pass that detects abstract, bibliography, and theorem sections
    /// based on heading keywords (same heuristics as the DOCX parser).
    /// </summary>
    private static List<ImportElement> PostProcess(List<ImportElement> elements, List<ImportWarning> warnings)
    {
        var result = new List<ImportElement>();
        var currentSection = SectionType.None;
        var order = 0;

        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];

            // Track section context from headings
            if (element is ImportHeading heading)
            {
                var headingText = heading.Text.Trim().ToLowerInvariant();

                if (headingText is "abstract" or "summary")
                {
                    currentSection = SectionType.Abstract;
                    // Keep the heading but mark we're in abstract section
                    result.Add(heading);
                    heading.Order = order++;
                    continue;
                }

                if (headingText is "references" or "bibliography" or "works cited" or "literature")
                {
                    currentSection = SectionType.Bibliography;
                    result.Add(heading);
                    heading.Order = order++;
                    continue;
                }

                // Any other heading resets the section
                currentSection = SectionType.None;
            }

            // Convert paragraphs based on section context
            if (element is ImportParagraph paragraph)
            {
                switch (currentSection)
                {
                    case SectionType.Abstract:
                        result.Add(new ImportAbstract
                        {
                            Text = paragraph.Text,
                            Order = order++
                        });
                        continue;

                    case SectionType.Bibliography:
                        result.Add(new ImportBibliographyEntry
                        {
                            Text = paragraph.Text,
                            DetectionReason = BibliographyDetectionReason.SectionContext,
                            ReferenceLabel = ExtractReferenceLabel(paragraph.Text),
                            Order = order++
                        });
                        continue;
                }

                // Detect theorem-like environments from paragraph text
                var theoremMatch = TheoremPrefixRegex().Match(paragraph.Text);
                if (theoremMatch.Success)
                {
                    var envType = ParseTheoremEnvironmentType(theoremMatch.Groups[1].Value);
                    var number = theoremMatch.Groups[2].Success ? theoremMatch.Groups[2].Value : null;
                    var body = paragraph.Text[theoremMatch.Length..].Trim();
                    if (body.StartsWith('.') || body.StartsWith(':'))
                        body = body[1..].Trim();

                    result.Add(new ImportTheorem
                    {
                        Text = body,
                        EnvironmentType = envType,
                        Number = number,
                        DetectionReason = TheoremDetectionReason.ContentPattern,
                        Order = order++
                    });
                    continue;
                }
            }

            element.Order = order++;
            result.Add(element);
        }

        return result;
    }

    private static string ExtractTitle(List<ImportElement> elements)
    {
        // First heading is likely the title
        var firstHeading = elements.OfType<ImportHeading>().FirstOrDefault();
        if (firstHeading != null && !string.IsNullOrWhiteSpace(firstHeading.Text))
            return firstHeading.Text;

        // Fallback to first non-empty paragraph
        var firstParagraph = elements.OfType<ImportParagraph>().FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Text));
        if (firstParagraph != null)
        {
            var text = firstParagraph.Text;
            return text.Length > 100 ? text[..100] + "..." : text;
        }

        return "Imported PDF Document";
    }

    private static string? ExtractReferenceLabel(string text)
    {
        var match = ReferenceLabelRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static TheoremEnvironmentType ParseTheoremEnvironmentType(string keyword)
    {
        return keyword.ToLowerInvariant() switch
        {
            "theorem" => TheoremEnvironmentType.Theorem,
            "lemma" => TheoremEnvironmentType.Lemma,
            "proposition" => TheoremEnvironmentType.Proposition,
            "corollary" => TheoremEnvironmentType.Corollary,
            "conjecture" => TheoremEnvironmentType.Conjecture,
            "definition" => TheoremEnvironmentType.Definition,
            "example" => TheoremEnvironmentType.Example,
            "remark" => TheoremEnvironmentType.Remark,
            "proof" => TheoremEnvironmentType.Proof,
            "axiom" => TheoremEnvironmentType.Axiom,
            _ => TheoremEnvironmentType.Theorem
        };
    }

    private static string GuessMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }

    private enum SectionType { None, Abstract, Bibliography }

    [GeneratedRegex(@"^\d+[\.\)]\s")]
    private static partial Regex NumberedListRegex();

    [GeneratedRegex(@"^[\-\*\u2022\u2023\u25E6\u2043\u2219]\s*")]
    private static partial Regex BulletListRegex();

    [GeneratedRegex(@"^(Theorem|Lemma|Proposition|Corollary|Conjecture|Definition|Example|Remark|Proof|Axiom)\s*(\d[\d\.]*)?[\.\:]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TheoremPrefixRegex();

    [GeneratedRegex(@"^\[(\d+|[A-Za-z]+\d{2,4}[a-z]?)\]")]
    private static partial Regex ReferenceLabelRegex();
}
