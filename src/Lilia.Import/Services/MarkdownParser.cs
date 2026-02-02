using System.Text.RegularExpressions;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Parser for Markdown files that outputs the intermediate ImportDocument model.
/// </summary>
public class MarkdownParser : IMarkdownParser
{
    private static readonly string[] SupportedExtensions = [".md", ".markdown"];

    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<ImportDocument> ParseAsync(string filePath, MarkdownImportOptions? options = null)
    {
        options ??= MarkdownImportOptions.Default;

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Markdown file not found", filePath);

        var content = await File.ReadAllTextAsync(filePath);
        return Parse(content, filePath, options);
    }

    private ImportDocument Parse(string content, string sourcePath, MarkdownImportOptions options)
    {
        var document = new ImportDocument
        {
            SourcePath = sourcePath,
            Title = Path.GetFileNameWithoutExtension(sourcePath)
        };

        var lines = content.Split('\n');
        var elementOrder = 0;
        var inCodeBlock = false;
        var codeBlockContent = new List<string>();
        var codeBlockLanguage = "";
        var inMathBlock = false;
        var mathBlockContent = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // Handle fenced code blocks
            if (line.StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeBlockLanguage = line.Length > 3 ? line[3..].Trim() : "";
                    codeBlockContent.Clear();
                    continue;
                }
                else
                {
                    inCodeBlock = false;
                    if (options.ConvertCodeBlocks)
                    {
                        document.Elements.Add(new ImportCodeBlock
                        {
                            Order = elementOrder++,
                            Text = string.Join("\n", codeBlockContent),
                            Language = string.IsNullOrEmpty(codeBlockLanguage) ? null : codeBlockLanguage,
                            DetectionReason = CodeBlockDetectionReason.StyleName
                        });
                    }
                    continue;
                }
            }

            if (inCodeBlock)
            {
                codeBlockContent.Add(line);
                continue;
            }

            // Handle display math blocks ($$...$$)
            if (line.StartsWith("$$") && options.ConvertDisplayMath)
            {
                if (!inMathBlock)
                {
                    // Check for single-line math
                    if (line.EndsWith("$$") && line.Length > 4)
                    {
                        var latex = line[2..^2].Trim();
                        document.Elements.Add(new ImportEquation
                        {
                            Order = elementOrder++,
                            LatexContent = latex,
                            ConversionSucceeded = true,
                            IsInline = false
                        });
                    }
                    else
                    {
                        inMathBlock = true;
                        mathBlockContent.Clear();
                        if (line.Length > 2)
                            mathBlockContent.Add(line[2..]);
                    }
                    continue;
                }
                else
                {
                    inMathBlock = false;
                    if (line.Length > 2 && line.EndsWith("$$"))
                        mathBlockContent.Add(line[..^2]);

                    document.Elements.Add(new ImportEquation
                    {
                        Order = elementOrder++,
                        LatexContent = string.Join("\n", mathBlockContent).Trim(),
                        ConversionSucceeded = true,
                        IsInline = false
                    });
                    continue;
                }
            }

            if (inMathBlock)
            {
                if (line.EndsWith("$$"))
                {
                    inMathBlock = false;
                    mathBlockContent.Add(line[..^2]);
                    document.Elements.Add(new ImportEquation
                    {
                        Order = elementOrder++,
                        LatexContent = string.Join("\n", mathBlockContent).Trim(),
                        ConversionSucceeded = true,
                        IsInline = false
                    });
                }
                else
                {
                    mathBlockContent.Add(line);
                }
                continue;
            }

            // Handle headings
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var title = headingMatch.Groups[2].Value.Trim();

                // First H1 can be document title
                if (level == 1 && options.FirstH1AsTitle && string.IsNullOrEmpty(document.Title) ||
                    level == 1 && options.FirstH1AsTitle && document.Title == Path.GetFileNameWithoutExtension(document.SourcePath))
                {
                    document.Title = title;
                    // Don't add as heading element, it's the document title
                    continue;
                }

                if (level >= options.MinHeadingLevelForSection && level <= options.MaxHeadingLevelForSection)
                {
                    document.Elements.Add(new ImportHeading
                    {
                        Order = elementOrder++,
                        Level = level,
                        Text = title,
                        Formatting = ParseInlineFormatting(title)
                    });
                }
                else
                {
                    // Treat as bold paragraph
                    document.Elements.Add(new ImportParagraph
                    {
                        Order = elementOrder++,
                        Text = title,
                        Style = ParagraphStyle.Title,
                        Formatting = [new FormattingSpan { Start = 0, Length = title.Length, Type = FormattingType.Bold }]
                    });
                }
                continue;
            }

            // Handle images
            var imageMatch = Regex.Match(line, @"^!\[([^\]]*)\]\(([^)]+)\)$");
            if (imageMatch.Success && options.ConvertImages)
            {
                var altText = imageMatch.Groups[1].Value;
                var imagePath = imageMatch.Groups[2].Value;

                document.Elements.Add(new ImportImage
                {
                    Order = elementOrder++,
                    AltText = string.IsNullOrEmpty(altText) ? null : altText,
                    Filename = imagePath,
                    Data = [] // Will be resolved later if it's a local file
                });
                continue;
            }

            // Handle tables (GFM tables)
            if (line.StartsWith("|") && options.ConvertTables)
            {
                var tableRows = new List<List<ImportTableCell>>();
                var headerRow = true;

                // Parse current line as first row
                var row = ParseTableRow(line);
                if (row != null)
                    tableRows.Add(row);

                // Continue parsing table rows
                while (i + 1 < lines.Length)
                {
                    var nextLine = lines[i + 1].TrimEnd('\r');
                    if (!nextLine.StartsWith("|"))
                        break;

                    i++;

                    // Skip separator row (|---|---|)
                    if (Regex.IsMatch(nextLine, @"^\|[\s\-:|]+\|$"))
                        continue;

                    row = ParseTableRow(nextLine);
                    if (row != null)
                        tableRows.Add(row);
                }

                if (tableRows.Count > 0)
                {
                    document.Elements.Add(new ImportTable
                    {
                        Order = elementOrder++,
                        Rows = tableRows,
                        HasHeaderRow = headerRow
                    });
                }
                continue;
            }

            // Handle blockquotes
            if (line.StartsWith(">"))
            {
                var quoteText = line.TrimStart('>').Trim();
                document.Elements.Add(new ImportParagraph
                {
                    Order = elementOrder++,
                    Text = quoteText,
                    Style = ParagraphStyle.Quote,
                    Formatting = ParseInlineFormatting(quoteText)
                });
                continue;
            }

            // Handle list items
            var bulletMatch = Regex.Match(line, @"^(\s*)([-*+])\s+(.+)$");
            if (bulletMatch.Success)
            {
                var indent = bulletMatch.Groups[1].Value.Length;
                var text = bulletMatch.Groups[3].Value;
                document.Elements.Add(new ImportListItem
                {
                    Order = elementOrder++,
                    Text = text,
                    Level = indent / 2, // Assume 2-space indent per level
                    IsNumbered = false,
                    ListMarker = "•",
                    Formatting = ParseInlineFormatting(text)
                });
                continue;
            }

            var numberedMatch = Regex.Match(line, @"^(\s*)(\d+)[.)]\s+(.+)$");
            if (numberedMatch.Success)
            {
                var indent = numberedMatch.Groups[1].Value.Length;
                var number = numberedMatch.Groups[2].Value;
                var text = numberedMatch.Groups[3].Value;
                document.Elements.Add(new ImportListItem
                {
                    Order = elementOrder++,
                    Text = text,
                    Level = indent / 2,
                    IsNumbered = true,
                    ListMarker = $"{number}.",
                    Formatting = ParseInlineFormatting(text)
                });
                continue;
            }

            // Handle regular paragraphs (skip empty lines)
            if (!string.IsNullOrWhiteSpace(line))
            {
                document.Elements.Add(new ImportParagraph
                {
                    Order = elementOrder++,
                    Text = line,
                    Style = ParagraphStyle.Normal,
                    Formatting = ParseInlineFormatting(line)
                });
            }
        }

        return document;
    }

    private static List<ImportTableCell>? ParseTableRow(string line)
    {
        // Simple table row parsing - split by |
        var cells = line.Trim('|').Split('|');
        var result = new List<ImportTableCell>();

        foreach (var cell in cells)
        {
            result.Add(new ImportTableCell
            {
                Text = cell.Trim(),
                Formatting = ParseInlineFormatting(cell.Trim())
            });
        }

        return result.Count > 0 ? result : null;
    }

    private static List<FormattingSpan> ParseInlineFormatting(string text)
    {
        var spans = new List<FormattingSpan>();

        // Bold: **text** or __text__
        foreach (Match match in Regex.Matches(text, @"\*\*([^*]+)\*\*|__([^_]+)__"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.Bold
            });
        }

        // Italic: *text* or _text_ (not within bold)
        foreach (Match match in Regex.Matches(text, @"(?<!\*)\*(?!\*)([^*]+)(?<!\*)\*(?!\*)|(?<!_)_(?!_)([^_]+)(?<!_)_(?!_)"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.Italic
            });
        }

        // Code: `text`
        foreach (Match match in Regex.Matches(text, @"`([^`]+)`"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.FontFamily,
                Value = "monospace"
            });
        }

        return spans;
    }
}
