using System.Text.RegularExpressions;
using Lilia.Import.Detection;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lilia.Import.Services;

/// <summary>
/// Parses PDF files via the MinerU sidecar and maps output to ImportElement types,
/// producing an ImportDocument compatible with the existing review pipeline.
/// </summary>
public partial class PdfImportService : IPdfParser
{
    private readonly IMineruClient _mineruClient;
    private readonly MineruOptions _mineruOptions;
    private readonly ILogger<PdfImportService> _logger;

    public PdfImportService(IMineruClient mineruClient, IOptions<MineruOptions> mineruOptions, ILogger<PdfImportService> logger)
    {
        _mineruClient = mineruClient;
        _mineruOptions = mineruOptions.Value;
        _logger = logger;
    }

    public bool CanParse(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ImportDocument> ParseAsync(string filePath, ImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[PDF] Starting parse of {FilePath}", filePath);

        var parseOptions = new MineruParseOptions
        {
            Language = _mineruOptions.Language,
            FormulaEnable = options?.PdfFormulaEnable ?? _mineruOptions.FormulaEnable,
            TableEnable = options?.PdfTableEnable ?? _mineruOptions.TableEnable
        };

        var allContentBlocks = new List<MineruContentBlock>();
        var allImages = new Dictionary<string, string>();
        var batchSize = _mineruOptions.BatchPageSize;

        if (batchSize > 0)
        {
            // Batched parsing: send sequential page-range requests
            var batchIndex = 0;
            var hasMorePages = true;

            while (hasMorePages)
            {
                var startPage = batchIndex * batchSize;
                var endPage = startPage + batchSize - 1;

                var batchOptions = new MineruParseOptions
                {
                    Language = parseOptions.Language,
                    FormulaEnable = parseOptions.FormulaEnable,
                    TableEnable = parseOptions.TableEnable,
                    StartPageId = startPage,
                    EndPageId = endPage
                };

                var batchResponse = await _mineruClient.ParsePdfAsync(filePath, batchOptions, cancellationToken);

                allContentBlocks.AddRange(batchResponse.ContentList);
                foreach (var (key, value) in batchResponse.Images)
                    allImages.TryAdd(key, value);

                _logger.LogInformation("[PDF] Batch {BatchIndex}: parsed pages {StartPage}-{EndPage}, got {BlockCount} blocks",
                    batchIndex + 1, startPage + 1, endPage + 1, batchResponse.ContentList.Count);

                // If MinerU returned fewer blocks than expected for a full batch, we've likely hit the end
                if (batchResponse.ContentList.Count == 0)
                {
                    hasMorePages = false;
                }
                else
                {
                    // Check if any block in this batch has a page index less than endPage
                    // If the last block's page is before endPage, we've reached the end of the document
                    var maxPageInBatch = batchResponse.ContentList
                        .Where(b => b.PageIdx.HasValue)
                        .Select(b => b.PageIdx!.Value)
                        .DefaultIfEmpty(-1)
                        .Max();

                    hasMorePages = maxPageInBatch >= endPage;
                }

                batchIndex++;
            }
        }
        else
        {
            // Single-request parsing (batching disabled)
            var response = await _mineruClient.ParsePdfAsync(filePath, parseOptions, cancellationToken);
            allContentBlocks = response.ContentList;
            allImages = response.Images;
        }

        var combinedResponse = new MineruParseResponse
        {
            ContentList = allContentBlocks,
            Images = allImages
        };

        var elements = new List<ImportElement>();
        var warnings = new List<ImportWarning>();
        var order = 0;

        foreach (var block in combinedResponse.ContentList)
        {
            var mapped = await MapBlockAsync(block, combinedResponse, options, cancellationToken);
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
    internal static List<ImportElement> PostProcess(List<ImportElement> elements, List<ImportWarning> warnings)
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

                // Detect Table of Contents / List of Figures / List of Tables headings
                var classifiedSection = SectionKeywordRegistry.Classify(heading.Text);
                if (classifiedSection is Models.SectionType.TableOfContents)
                {
                    currentSection = SectionType.TableOfContents;
                    // Don't emit the heading — the TOC block replaces it
                    continue;
                }

                if (classifiedSection is Models.SectionType.ListOfFigures
                    or Models.SectionType.ListOfTables)
                {
                    currentSection = SectionType.SkipSection;
                    // Don't emit the heading — list of figures/tables is auto-generated
                    continue;
                }

                // Any other heading resets the section
                currentSection = SectionType.None;
            }

            // Detect TOC-like paragraphs by content pattern (multiple dot-leader entries),
            // regardless of section context. This catches TOCs where the "Contents" heading
            // wasn't parsed as a heading, or where the heading text didn't match keywords.
            // Must run before embedded abstract detection (TOC text often contains "Abstract").
            if (element is ImportParagraph tocCandidate && currentSection != SectionType.TableOfContents)
            {
                if (LooksTocLikeParagraph(tocCandidate.Text))
                {
                    var detectedEntries = ParseTocParagraph(tocCandidate.Text);
                    if (detectedEntries.Count > 0)
                    {
                        // Check if the previous element is a heading or paragraph that looks
                        // like a TOC title and remove it (it becomes part of the TOC block)
                        if (result.Count > 0)
                        {
                            var lastText = result[^1] switch
                            {
                                ImportHeading h => h.Text,
                                ImportParagraph p => p.Text,
                                _ => null
                            };
                            if (lastText != null)
                            {
                                var prevClassified = SectionKeywordRegistry.Classify(lastText);
                                if (prevClassified is Models.SectionType.TableOfContents
                                    or Models.SectionType.ListOfFigures
                                    or Models.SectionType.ListOfTables)
                                {
                                    result.RemoveAt(result.Count - 1);
                                    order--;
                                }
                            }
                        }

                        result.Add(new ImportTableOfContents
                        {
                            Entries = detectedEntries,
                            Order = order++
                        });
                        currentSection = SectionType.TableOfContents;
                        continue;
                    }
                }
            }

            // Detect abstract keyword embedded in paragraph text (e.g. Mathpix merges title page + abstract)
            if (element is ImportParagraph embeddedParagraph && currentSection == SectionType.None)
            {
                var (keywordStart, afterKeyword) = FindAbstractKeywordBoundary(embeddedParagraph.Text);
                if (keywordStart >= 0)
                {
                    var beforeText = embeddedParagraph.Text[..keywordStart].Trim();
                    var afterText = embeddedParagraph.Text[afterKeyword..].Trim();

                    // Keep text before "Abstract" as a paragraph
                    if (!string.IsNullOrWhiteSpace(beforeText))
                    {
                        result.Add(new ImportParagraph { Text = beforeText, Order = order++ });
                    }

                    // Text after "Abstract" becomes an abstract block
                    if (!string.IsNullOrWhiteSpace(afterText))
                    {
                        result.Add(new ImportAbstract { Text = afterText, Order = order++ });
                    }

                    currentSection = SectionType.Abstract;
                    continue;
                }
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

                    case SectionType.TableOfContents:
                        var tocEntries = ParseTocParagraph(paragraph.Text);
                        if (tocEntries.Count > 0)
                        {
                            result.Add(new ImportTableOfContents
                            {
                                Entries = tocEntries,
                                Order = order++
                            });
                        }
                        // Stay in TOC section for multi-paragraph TOCs
                        continue;

                    case SectionType.SkipSection:
                        // Drop paragraphs in List of Figures/Tables sections
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

    internal static string ExtractTitle(List<ImportElement> elements)
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

    internal static string? ExtractReferenceLabel(string text)
    {
        var match = ReferenceLabelRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    internal static TheoremEnvironmentType ParseTheoremEnvironmentType(string keyword)
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

    internal static string GuessMimeType(string path)
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

    /// <summary>
    /// Searches for an abstract keyword (from SectionKeywordRegistry) embedded within paragraph text
    /// as a standalone word. Only matches if the keyword appears after some preceding text to avoid
    /// false positives on paragraphs that simply start with "Abstract".
    /// Returns (keywordStart, afterKeyword) indices, or (-1, -1) if not found.
    /// </summary>
    internal static (int KeywordStart, int AfterKeyword) FindAbstractKeywordBoundary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (-1, -1);

        var abstractKeywords = SectionKeywordRegistry.Keywords[Models.SectionType.Abstract];

        foreach (var keyword in abstractKeywords)
        {
            var searchIndex = 0;
            while (searchIndex < text.Length)
            {
                var idx = text.IndexOf(keyword, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    break;

                // Must appear after some preceding text (not at the very start)
                if (idx == 0)
                {
                    searchIndex = idx + keyword.Length;
                    continue;
                }

                var afterIdx = idx + keyword.Length;

                // Check word boundary: preceding char must be whitespace
                var prevChar = text[idx - 1];
                if (!char.IsWhiteSpace(prevChar))
                {
                    searchIndex = afterIdx;
                    continue;
                }

                // Check word boundary: following char must be whitespace, punctuation, or end of string
                if (afterIdx < text.Length)
                {
                    var nextChar = text[afterIdx];
                    if (!char.IsWhiteSpace(nextChar) && !char.IsPunctuation(nextChar))
                    {
                        searchIndex = afterIdx;
                        continue;
                    }
                }

                return (idx, afterIdx);
            }
        }

        return (-1, -1);
    }

    /// <summary>
    /// Determines if a paragraph looks like a Table of Contents based on its content.
    /// A TOC paragraph typically has 3+ dot-leader patterns (text ..... page_number).
    /// </summary>
    internal static bool LooksTocLikeParagraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Count dot-leader occurrences: "...." (3+ dots)
        var dotLeaderCount = TocEntryRegex().Matches(text).Count;

        // Require at least 3 entries to avoid false positives on a single "See page ..... 5"
        return dotLeaderCount >= 3;
    }

    /// <summary>
    /// Parses a flat TOC paragraph (with dot leaders) into structured TocEntry objects.
    /// Mathpix format: "Declaration ..... i Certificate ..... ii 1 Introduction ..... 1"
    /// </summary>
    internal static List<TocEntry> ParseTocParagraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var entries = new List<TocEntry>();
        var matches = TocEntryRegex().Matches(text);

        foreach (Match match in matches)
        {
            var entryText = match.Groups["text"].Value.Trim();
            var pageNumber = match.Groups["page"].Value.Trim();

            if (string.IsNullOrWhiteSpace(entryText))
                continue;

            var level = DetermineTocLevel(entryText);

            entries.Add(new TocEntry
            {
                Text = entryText,
                Level = level,
                PageNumber = pageNumber
            });
        }

        return entries;
    }

    /// <summary>
    /// Determines the TOC entry level from its numbering prefix.
    /// No number → level 1 (front matter); N → level 1 (chapter);
    /// N.N → level 2 (section); N.N.N → level 3 (subsection).
    /// </summary>
    private static int DetermineTocLevel(string entryText)
    {
        var numberMatch = TocNumberingRegex().Match(entryText);
        if (!numberMatch.Success)
            return 1; // Front matter or unnumbered entry

        var numbering = numberMatch.Value.Trim();
        var dotCount = numbering.Count(c => c == '.');
        return dotCount + 1; // "1" → 1, "1.1" → 2, "1.1.1" → 3
    }

    internal enum SectionType { None, Abstract, Bibliography, TableOfContents, SkipSection }

    [GeneratedRegex(@"(?<text>.+?)\s*\.{3,}\s*(?<page>[ivxlcdmIVXLCDM\d]+)")]
    internal static partial Regex TocEntryRegex();

    [GeneratedRegex(@"^\d+(\.\d+)*\s")]
    internal static partial Regex TocNumberingRegex();

    [GeneratedRegex(@"^\d+[\.\)]\s")]
    internal static partial Regex NumberedListRegex();

    [GeneratedRegex(@"^[\-\*\u2022\u2023\u25E6\u2043\u2219]\s*")]
    internal static partial Regex BulletListRegex();

    [GeneratedRegex(@"^(Theorem|Lemma|Proposition|Corollary|Conjecture|Definition|Example|Remark|Proof|Axiom)\s*(\d[\d\.]*)?[\.\:]\s*", RegexOptions.IgnoreCase)]
    internal static partial Regex TheoremPrefixRegex();

    [GeneratedRegex(@"^\[(\d+|[A-Za-z]+\d{2,4}[a-z]?)\]")]
    internal static partial Regex ReferenceLabelRegex();
}
