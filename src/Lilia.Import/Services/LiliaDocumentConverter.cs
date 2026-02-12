using System.Security.Cryptography;
using System.Text;
using Lilia.Core.Models;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Converts an ImportDocument (intermediate representation) to Lilia's native Document format.
/// This is Stage 2 of the two-stage import process.
/// </summary>
public class LiliaDocumentConverter : IImportConverter
{
    private int _nextSectionId = 1;
    private int _nextBlockId = 1;
    private int _nextAssetId = 1;
    private readonly List<ImportWarning> _warnings = [];

    /// <summary>
    /// Convert an ImportDocument to Lilia's Document format.
    /// </summary>
    public ImportResult Convert(ImportDocument importDocument, ConversionOptions? options = null)
    {
        options ??= new ConversionOptions();

        _warnings.Clear();
        _nextSectionId = 1;
        _nextBlockId = 1;
        _nextAssetId = 1;

        try
        {
            var now = DateTime.UtcNow;

            // Create the document
            var document = new Document
            {
                Id = 1,
                Title = importDocument.Title ?? Path.GetFileNameWithoutExtension(importDocument.SourcePath ?? "Untitled"),
                CreatedAt = now,
                ModifiedAt = now,
                SchemaVersion = 1
            };

            // Build sections from headings and assign content to sections
            var sections = BuildSectionHierarchy(importDocument.Elements, options);
            document.Sections = sections.Where(s => s.ParentId == null).ToList();

            // Build statistics
            var statistics = new ImportStatistics
            {
                TotalElementsParsed = importDocument.Elements.Count,
                SectionsCreated = sections.Count,
                BlocksCreated = sections.Sum(s => CountBlocks(s)),
                EquationsFound = importDocument.Elements.Count(e => e.Type == ImportElementType.Equation),
                EquationsConverted = importDocument.Elements.OfType<ImportEquation>().Count(e => e.ConversionSucceeded),
                ImagesExtracted = importDocument.Elements.Count(e => e.Type == ImportElementType.Image),
                TablesExtracted = importDocument.Elements.Count(e => e.Type == ImportElementType.Table),
                CodeBlocksDetected = importDocument.Elements.Count(e => e.Type == ImportElementType.CodeBlock),
                TheoremsDetected = importDocument.Elements.Count(e => e.Type == ImportElementType.Theorem),
                BlockquotesDetected = importDocument.Elements.Count(e => e.Type == ImportElementType.Blockquote),
                BibliographyEntriesDetected = importDocument.Elements.Count(e => e.Type == ImportElementType.BibliographyEntry)
            };

            // Combine warnings from parsing and conversion
            var allWarnings = new List<ImportWarning>(importDocument.Warnings);
            allWarnings.AddRange(_warnings);

            return new ImportResult
            {
                Success = true,
                Document = document,
                Sections = sections,
                Warnings = allWarnings,
                IntermediateDocument = importDocument,
                Statistics = statistics
            };
        }
        catch (Exception ex)
        {
            return ImportResult.Failed($"Conversion failed: {ex.Message}", [.. importDocument.Warnings, .. _warnings]);
        }
    }

    /// <summary>
    /// Build the section hierarchy from headings and assign content blocks to sections.
    /// </summary>
    private List<Section> BuildSectionHierarchy(List<ImportElement> elements, ConversionOptions options)
    {
        var allSections = new List<Section>();

        // Track the current section stack by level (1-indexed)
        // Stack[level] = section at that level
        var sectionStack = new Dictionary<int, Section>();

        // Create a default section for content before the first heading
        var defaultSection = CreateSection("Content", null, 0);
        allSections.Add(defaultSection);
        var currentSection = defaultSection;
        var currentSectionLevel = 0;

        int blockOrder = 0;
        int totalBlockCount = 0;
        int sectionCount = 1; // Default section counts as 1
        bool wasTruncated = false;

        foreach (var element in elements)
        {
            // Check section limit
            if (options.MaxSections > 0 && sectionCount > options.MaxSections)
            {
                wasTruncated = true;
                break;
            }

            // Check block limit
            if (options.MaxBlocks > 0 && totalBlockCount >= options.MaxBlocks)
            {
                wasTruncated = true;
                break;
            }

            if (element is ImportHeading heading)
            {
                // Create a new section for this heading
                var level = heading.Level;

                // Apply max depth limit if configured
                if (options.MaxSectionDepth > 0 && level > options.MaxSectionDepth)
                {
                    // Treat as paragraph instead of section
                    var block = CreateParagraphBlock(heading.Text, heading.Formatting, blockOrder++, options);
                    currentSection.Blocks.Add(block);
                    totalBlockCount++;
                    continue;
                }

                // Find parent section (the most recent section with a lower level)
                Section? parent = null;
                for (int l = level - 1; l >= 1; l--)
                {
                    if (sectionStack.TryGetValue(l, out var candidate))
                    {
                        parent = candidate;
                        break;
                    }
                }

                var section = CreateSection(heading.Text, parent?.Id, allSections.Count);
                allSections.Add(section);
                sectionCount++;

                // Add as child to parent if exists
                if (parent != null)
                {
                    section.ParentId = parent.Id;
                    parent.Children.Add(section);
                }

                // Update the stack - clear all levels >= current level
                var keysToRemove = sectionStack.Keys.Where(k => k >= level).ToList();
                foreach (var key in keysToRemove)
                {
                    sectionStack.Remove(key);
                }
                sectionStack[level] = section;

                currentSection = section;
                currentSectionLevel = level;
                blockOrder = 0; // Reset block order for new section
            }
            else
            {
                // Convert element to block and add to current section
                var block = ConvertToBlock(element, blockOrder++, options);
                if (block != null)
                {
                    block.SectionId = currentSection.Id;
                    currentSection.Blocks.Add(block);
                    totalBlockCount++;
                }
            }
        }

        // Add truncation warning if content was limited
        if (wasTruncated)
        {
            _warnings.Add(new ImportWarning(
                ImportWarningType.ContentTruncated,
                $"Document was truncated. Express edition is limited to {options.MaxSections} sections or {options.MaxBlocks} blocks. Upgrade to Pro for unlimited imports."
            ));
        }

        // Remove default section if it's empty
        if (defaultSection.Blocks.Count == 0 && allSections.Count > 1)
        {
            allSections.Remove(defaultSection);
        }

        return allSections;
    }

    /// <summary>
    /// Create a new Section with the given parameters.
    /// </summary>
    private Section CreateSection(string title, int? parentId, int sortOrder)
    {
        return new Section
        {
            Id = _nextSectionId++,
            ParentId = parentId,
            Title = title,
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow,
            Blocks = [],
            Children = []
        };
    }

    /// <summary>
    /// Convert an ImportElement to a Lilia Block.
    /// </summary>
    private Block? ConvertToBlock(ImportElement element, int sortOrder, ConversionOptions options)
    {
        return element switch
        {
            ImportParagraph para => ConvertParagraph(para, sortOrder, options),
            ImportEquation eq => ConvertEquation(eq, sortOrder, options),
            ImportCodeBlock code => ConvertCodeBlock(code, sortOrder, options),
            ImportTable table => ConvertTable(table, sortOrder, options),
            ImportImage image => ConvertImage(image, sortOrder, options),
            ImportListItem listItem => ConvertListItem(listItem, sortOrder, options),
            ImportAbstract abs => ConvertAbstract(abs, sortOrder, options),
            ImportBlockquote bq => ConvertBlockquote(bq, sortOrder, options),
            ImportTheorem thm => ConvertTheorem(thm, sortOrder, options),
            ImportBibliographyEntry bib => ConvertBibliographyEntry(bib, sortOrder, options),
            ImportPageBreak => ConvertPageBreak(sortOrder),
            _ => null
        };
    }

    /// <summary>
    /// Convert an abstract element to a Block.
    /// </summary>
    private Block ConvertAbstract(ImportAbstract abs, int sortOrder, ConversionOptions options)
    {
        var content = options.ApplyFormattingAsLatex
            ? ApplyFormattingAsLatex(abs.Text, abs.Formatting)
            : abs.Text;

        return CreateBlock(BlockType.Abstract, content, sortOrder);
    }

    /// <summary>
    /// Convert a blockquote element to a Block.
    /// </summary>
    private Block ConvertBlockquote(ImportBlockquote blockquote, int sortOrder, ConversionOptions options)
    {
        var content = options.ApplyFormattingAsLatex
            ? ApplyFormattingAsLatex(blockquote.Text, blockquote.Formatting)
            : blockquote.Text;

        var formattedContent = $"\\begin{{quote}}\n{content}\n\\end{{quote}}";
        return CreateBlock(BlockType.Blockquote, formattedContent, sortOrder);
    }

    /// <summary>
    /// Convert a theorem element to a Block.
    /// </summary>
    private Block ConvertTheorem(ImportTheorem theorem, int sortOrder, ConversionOptions options)
    {
        var content = options.ApplyFormattingAsLatex
            ? ApplyFormattingAsLatex(theorem.Text, theorem.Formatting)
            : theorem.Text;

        var envName = theorem.EnvironmentType.ToString().ToLowerInvariant();

        var sb = new StringBuilder();
        sb.Append($"\\begin{{{envName}}}");

        if (!string.IsNullOrEmpty(theorem.Title))
        {
            sb.Append($"[{EscapeLatex(theorem.Title)}]");
        }

        sb.AppendLine();

        if (!string.IsNullOrEmpty(theorem.Label))
        {
            sb.AppendLine($"\\label{{{theorem.Label}}}");
        }

        sb.AppendLine(content);
        sb.Append($"\\end{{{envName}}}");

        return CreateBlock(BlockType.Theorem, sb.ToString(), sortOrder);
    }

    /// <summary>
    /// Convert a bibliography entry element to a Block.
    /// </summary>
    private Block ConvertBibliographyEntry(ImportBibliographyEntry entry, int sortOrder, ConversionOptions options)
    {
        var content = options.ApplyFormattingAsLatex
            ? ApplyFormattingAsLatex(entry.Text, entry.Formatting)
            : entry.Text;

        var label = entry.ReferenceLabel ?? $"ref{sortOrder}";
        var formattedContent = $"\\bibitem{{{label}}} {content}";

        return CreateBlock(BlockType.Bibliography, formattedContent, sortOrder);
    }

    /// <summary>
    /// Convert a page break element to a Block.
    /// </summary>
    private Block ConvertPageBreak(int sortOrder)
    {
        return CreateBlock(BlockType.PageBreak, "", sortOrder);
    }

    /// <summary>
    /// Convert a paragraph element to a Block.
    /// </summary>
    private Block ConvertParagraph(ImportParagraph para, int sortOrder, ConversionOptions options)
    {
        var content = options.ApplyFormattingAsLatex
            ? ApplyFormattingAsLatex(para.Text, para.Formatting)
            : para.Text;

        // Handle special paragraph styles
        content = para.Style switch
        {
            ParagraphStyle.Quote => $"\\begin{{quote}}\n{content}\n\\end{{quote}}",
            ParagraphStyle.Caption => $"\\caption{{{content}}}",
            _ => content
        };

        return CreateBlock(BlockType.Paragraph, content, sortOrder);
    }

    /// <summary>
    /// Create a paragraph block from plain text with formatting.
    /// </summary>
    private Block CreateParagraphBlock(string text, List<FormattingSpan> formatting, int sortOrder, ConversionOptions options)
    {
        var content = options.ApplyFormattingAsLatex
            ? ApplyFormattingAsLatex(text, formatting)
            : text;
        return CreateBlock(BlockType.Paragraph, content, sortOrder);
    }

    /// <summary>
    /// Convert an equation element to a Block.
    /// </summary>
    private Block ConvertEquation(ImportEquation eq, int sortOrder, ConversionOptions options)
    {
        string content;

        if (eq.ConversionSucceeded && !string.IsNullOrEmpty(eq.LatexContent))
        {
            // Use the converted LaTeX
            content = eq.IsInline ? $"${eq.LatexContent}$" : $"\\[\n{eq.LatexContent}\n\\]";
        }
        else
        {
            // Handle failed conversion based on options
            content = options.FailedEquationBehavior switch
            {
                FailedEquationBehavior.InsertPlaceholder =>
                    $"% Equation conversion failed: {eq.ConversionError ?? "Unknown error"}\n\\text{{[Equation]}}",
                FailedEquationBehavior.Skip => "",
                FailedEquationBehavior.InsertOmmlComment =>
                    $"% Original OMML:\n% {TruncateForComment(eq.OmmlXml)}",
                _ => "\\text{[Equation]}"
            };

            _warnings.Add(new ImportWarning
            {
                Type = ImportWarningType.EquationConversionFailed,
                Message = $"Equation conversion failed: {eq.ConversionError ?? "Unknown error"}",
                ElementIndex = eq.Order
            });
        }

        return CreateBlock(BlockType.Equation, content, sortOrder);
    }

    /// <summary>
    /// Convert a code block element to a Block.
    /// </summary>
    private Block ConvertCodeBlock(ImportCodeBlock code, int sortOrder, ConversionOptions options)
    {
        var language = code.Language ?? options.DefaultCodeLanguage;

        // Use verbatim environment for code
        var content = string.IsNullOrEmpty(language)
            ? $"\\begin{{verbatim}}\n{code.Text}\n\\end{{verbatim}}"
            : $"\\begin{{lstlisting}}[language={language}]\n{code.Text}\n\\end{{lstlisting}}";

        return CreateBlock(BlockType.Code, content, sortOrder);
    }

    /// <summary>
    /// Convert a table element to a Block with LaTeX tabular content.
    /// </summary>
    private Block ConvertTable(ImportTable table, int sortOrder, ConversionOptions options)
    {
        if (table.Rows.Count == 0)
        {
            return CreateBlock(BlockType.Table, "% Empty table", sortOrder);
        }

        var sb = new StringBuilder();
        var colCount = table.ColumnCount;

        // Build column specification (centered by default)
        var colSpec = string.Join("|", Enumerable.Repeat("c", colCount));

        sb.AppendLine($"\\begin{{tabular}}{{|{colSpec}|}}");
        sb.AppendLine("\\hline");

        for (int rowIdx = 0; rowIdx < table.Rows.Count; rowIdx++)
        {
            var row = table.Rows[rowIdx];
            var cells = new List<string>();

            foreach (var cell in row)
            {
                var cellContent = options.ApplyFormattingAsLatex
                    ? ApplyFormattingAsLatex(cell.Text, cell.Formatting)
                    : cell.Text;

                // Escape special characters for LaTeX
                cellContent = EscapeLatex(cellContent);
                cells.Add(cellContent);
            }

            sb.AppendLine(string.Join(" & ", cells) + " \\\\");

            // Add horizontal line after header row or after each row
            if (table.HasHeaderRow && rowIdx == 0)
            {
                sb.AppendLine("\\hline\\hline");
            }
            else
            {
                sb.AppendLine("\\hline");
            }
        }

        sb.AppendLine("\\end{tabular}");

        return CreateBlock(BlockType.Table, sb.ToString(), sortOrder);
    }

    /// <summary>
    /// Convert an image element to a Block with an Asset.
    /// Applies image optimization based on ConversionOptions.
    /// </summary>
    private Block ConvertImage(ImportImage image, int sortOrder, ConversionOptions options)
    {
        // Apply image optimization if enabled
        var imageData = image.Data;
        var mimeType = image.MimeType;

        if (options.ImageOptimization.EnableOptimization)
        {
            var optimizer = new ImageOptimizer(options.ImageOptimization);
            var result = optimizer.Optimize(image.Data, image.MimeType, image.Filename);

            if (result.WasOptimized)
            {
                imageData = result.OptimizedData;
                mimeType = result.OptimizedMimeType;

                // Add a warning noting the optimization (for user awareness)
                _warnings.Add(new ImportWarning
                {
                    Type = ImportWarningType.FormattingLost,
                    Message = $"Image optimized: {result.SizeSummary}",
                    Details = result.Reason
                });
            }
        }

        // Generate filename based on (possibly new) MIME type
        var extension = GetExtensionFromMimeType(mimeType);
        var filename = image.Filename ?? $"image_{_nextAssetId}{extension}";

        // Update filename extension if MIME type changed
        if (image.Filename != null && mimeType != image.MimeType)
        {
            var baseName = Path.GetFileNameWithoutExtension(image.Filename);
            filename = baseName + extension;
        }

        // Create asset with optimized data
        var asset = new Asset
        {
            Id = _nextAssetId++,
            Filename = filename,
            MimeType = mimeType,
            Data = imageData,
            Hash = options.GenerateAssetHashes ? ComputeHash(imageData) : "",
            CreatedAt = DateTime.UtcNow
        };

        // Build LaTeX figure content
        var sb = new StringBuilder();
        sb.AppendLine("\\begin{figure}[htbp]");
        sb.AppendLine("\\centering");

        // Include graphics with optional size
        var widthSpec = image.WidthPixels.HasValue
            ? $"width={image.WidthPixels.Value / 96.0:F2}in"
            : "width=0.8\\textwidth";
        sb.AppendLine($"\\includegraphics[{widthSpec}]{{{filename}}}");

        // Add caption if alt text available
        if (!string.IsNullOrEmpty(image.AltText))
        {
            sb.AppendLine($"\\caption{{{EscapeLatex(image.AltText)}}}");
        }

        sb.AppendLine("\\end{figure}");

        var block = CreateBlock(BlockType.Figure, sb.ToString(), sortOrder);
        block.Assets.Add(asset);

        return block;
    }

    /// <summary>
    /// Convert a list item element to a Block.
    /// </summary>
    private Block ConvertListItem(ImportListItem listItem, int sortOrder, ConversionOptions options)
    {
        var content = options.ApplyFormattingAsLatex
            ? ApplyFormattingAsLatex(listItem.Text, listItem.Formatting)
            : listItem.Text;

        // Format as LaTeX list item
        var indent = new string(' ', listItem.Level * 2);
        var env = listItem.IsNumbered ? "enumerate" : "itemize";

        var formattedContent = $"\\begin{{{env}}}\n{indent}\\item {content}\n\\end{{{env}}}";

        return CreateBlock(BlockType.Paragraph, formattedContent, sortOrder);
    }

    /// <summary>
    /// Create a new Block with the given parameters.
    /// </summary>
    private Block CreateBlock(BlockType type, string content, int sortOrder)
    {
        var now = DateTime.UtcNow;
        return new Block
        {
            Id = _nextBlockId++,
            BlockType = type,
            Content = content,
            SortOrder = sortOrder,
            CreatedAt = now,
            ModifiedAt = now,
            Assets = []
        };
    }

    /// <summary>
    /// Apply formatting spans to text as LaTeX commands.
    /// </summary>
    private static string ApplyFormattingAsLatex(string text, List<FormattingSpan> formatting)
    {
        if (formatting.Count == 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Sort formatting by start position, then by length (longer first for nesting)
        var sortedFormatting = formatting
            .OrderBy(f => f.Start)
            .ThenByDescending(f => f.Length)
            .ToList();

        // Build the formatted string using a character-by-character approach
        var result = new StringBuilder();
        var activeFormats = new List<FormattingSpan>();

        for (int i = 0; i < text.Length; i++)
        {
            // Check for formats ending at this position
            var endingFormats = activeFormats.Where(f => f.End == i).ToList();
            foreach (var format in endingFormats)
            {
                result.Append(GetLatexClosingTag(format.Type));
                activeFormats.Remove(format);
            }

            // Check for formats starting at this position
            var startingFormats = sortedFormatting.Where(f => f.Start == i).ToList();
            foreach (var format in startingFormats)
            {
                result.Append(GetLatexOpeningTag(format.Type, format.Value));
                activeFormats.Add(format);
            }

            // Append the character (escape if needed)
            result.Append(EscapeLatexChar(text[i]));
        }

        // Close any remaining formats
        foreach (var format in activeFormats.OrderByDescending(f => f.Start))
        {
            result.Append(GetLatexClosingTag(format.Type));
        }

        return result.ToString();
    }

    /// <summary>
    /// Get the LaTeX opening tag for a formatting type.
    /// </summary>
    private static string GetLatexOpeningTag(FormattingType type, string? value) => type switch
    {
        FormattingType.Bold => "\\textbf{",
        FormattingType.Italic => "\\textit{",
        FormattingType.Underline => "\\underline{",
        FormattingType.Strikethrough => "\\sout{",
        FormattingType.Superscript => "\\textsuperscript{",
        FormattingType.Subscript => "\\textsubscript{",
        FormattingType.Highlight => "\\hl{",
        FormattingType.FontColor when value != null => $"\\textcolor[HTML]{{{value}}}{{",
        FormattingType.FontSize when value != null => $"{{\\fontsize{{{value}}}{{}}\\selectfont ",
        FormattingType.FontFamily when value != null => $"{{\\fontfamily{{{value}}}\\selectfont ",
        _ => ""
    };

    /// <summary>
    /// Get the LaTeX closing tag for a formatting type.
    /// </summary>
    private static string GetLatexClosingTag(FormattingType type) => type switch
    {
        FormattingType.Bold or
        FormattingType.Italic or
        FormattingType.Underline or
        FormattingType.Strikethrough or
        FormattingType.Superscript or
        FormattingType.Subscript or
        FormattingType.Highlight or
        FormattingType.FontColor or
        FormattingType.FontSize or
        FormattingType.FontFamily => "}",
        _ => ""
    };

    /// <summary>
    /// Escape a single character for LaTeX.
    /// </summary>
    private static string EscapeLatexChar(char c) => c switch
    {
        '\\' => "\\textbackslash{}",
        '{' => "\\{",
        '}' => "\\}",
        '$' => "\\$",
        '%' => "\\%",
        '&' => "\\&",
        '#' => "\\#",
        '_' => "\\_",
        '^' => "\\textasciicircum{}",
        '~' => "\\textasciitilde{}",
        _ => c.ToString()
    };

    /// <summary>
    /// Escape a string for LaTeX.
    /// </summary>
    private static string EscapeLatex(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            sb.Append(EscapeLatexChar(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Truncate long text for inclusion in a comment.
    /// </summary>
    private static string TruncateForComment(string text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Remove newlines for single-line comment
        var singleLine = text.Replace("\n", " ").Replace("\r", "");

        if (singleLine.Length <= maxLength) return singleLine;
        return singleLine[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Get file extension from MIME type.
    /// </summary>
    private static string GetExtensionFromMimeType(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tiff",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        _ => ".bin"
    };

    /// <summary>
    /// Compute SHA256 hash of data.
    /// </summary>
    private static string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return System.Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Count total blocks in a section and its children.
    /// </summary>
    private static int CountBlocks(Section section)
    {
        return section.Blocks.Count + section.Children.Sum(CountBlocks);
    }
}
