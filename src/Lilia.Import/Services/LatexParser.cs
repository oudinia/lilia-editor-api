using System.Text.RegularExpressions;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;

namespace Lilia.Import.Services;

/// <summary>
/// Parser for LaTeX files that outputs the intermediate ImportDocument model.
/// </summary>
public class LatexParser : ILatexParser
{
    private static readonly string[] SupportedExtensions = [".tex"];

    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<ImportDocument> ParseAsync(string filePath, LatexImportOptions? options = null)
    {
        options ??= LatexImportOptions.Default;

        if (!File.Exists(filePath))
            throw new FileNotFoundException("LaTeX file not found", filePath);

        var content = await File.ReadAllTextAsync(filePath);
        return Parse(content, filePath, options);
    }

    private ImportDocument Parse(string content, string sourcePath, LatexImportOptions options)
    {
        var document = new ImportDocument
        {
            SourcePath = sourcePath,
            Title = Path.GetFileNameWithoutExtension(sourcePath)
        };

        // Extract document title from \title{}
        if (options.ExtractDocumentTitle)
        {
            var titleMatch = Regex.Match(content, @"\\title\{([^}]+)\}");
            if (titleMatch.Success)
            {
                document.Title = titleMatch.Groups[1].Value;
            }
        }

        // Extract author if present
        var authorMatch = Regex.Match(content, @"\\author\{([^}]+)\}");
        if (authorMatch.Success)
        {
            document.Metadata.Author = authorMatch.Groups[1].Value;
        }

        // Extract content between \begin{document} and \end{document} if requested
        var documentContent = content;
        if (options.OnlyDocumentContent)
        {
            var documentMatch = Regex.Match(content, @"\\begin\{document\}([\s\S]*?)\\end\{document\}", RegexOptions.Singleline);
            if (documentMatch.Success)
            {
                documentContent = documentMatch.Groups[1].Value;
            }
        }

        // Remove document setup commands
        documentContent = Regex.Replace(documentContent, @"\\maketitle\b", "");
        documentContent = Regex.Replace(documentContent, @"\\tableofcontents\b", "");
        documentContent = Regex.Replace(documentContent, @"\\newpage\b", "");
        documentContent = Regex.Replace(documentContent, @"\\clearpage\b", "");

        // Parse the content
        ParseContent(documentContent, document, options);

        return document;
    }

    private void ParseContent(string content, ImportDocument document, LatexImportOptions options)
    {
        var elementOrder = 0;
        var remaining = content;

        while (!string.IsNullOrWhiteSpace(remaining))
        {
            // Find the next structural element
            var matches = new List<(Match match, string type)>();

            // Sections
            var sectionMatch = Regex.Match(remaining, @"\\(section|subsection|subsubsection)\*?\{([^}]+)\}");
            if (sectionMatch.Success)
                matches.Add((sectionMatch, "section"));

            // Equation environments
            if (options.ConvertEquationEnvironments)
            {
                var eqEnvMatch = Regex.Match(remaining, @"\\begin\{(equation|align|gather|multline)\*?\}([\s\S]*?)\\end\{\1\*?\}", RegexOptions.Singleline);
                if (eqEnvMatch.Success)
                    matches.Add((eqEnvMatch, "equation_env"));
            }

            // Display math $$...$$ and \[...\]
            if (options.ConvertDisplayMath)
            {
                var displayMathMatch = Regex.Match(remaining, @"\$\$([\s\S]*?)\$\$", RegexOptions.Singleline);
                if (displayMathMatch.Success)
                    matches.Add((displayMathMatch, "displaymath_dollar"));

                var bracketMathMatch = Regex.Match(remaining, @"\\\[([\s\S]*?)\\\]", RegexOptions.Singleline);
                if (bracketMathMatch.Success)
                    matches.Add((bracketMathMatch, "displaymath_bracket"));
            }

            // Code environments
            if (options.ConvertCodeEnvironments)
            {
                var codeMatch = Regex.Match(remaining, @"\\begin\{(lstlisting|verbatim|minted)\}(?:\[[^\]]*\])?([\s\S]*?)\\end\{\1\}", RegexOptions.Singleline);
                if (codeMatch.Success)
                    matches.Add((codeMatch, "code"));
            }

            // Figure environments
            if (options.PreserveFigures)
            {
                var figureMatch = Regex.Match(remaining, @"\\begin\{figure\}(?:\[[^\]]*\])?([\s\S]*?)\\end\{figure\}", RegexOptions.Singleline);
                if (figureMatch.Success)
                    matches.Add((figureMatch, "figure"));
            }

            // Table environments
            if (options.ConvertTables)
            {
                var tableMatch = Regex.Match(remaining, @"\\begin\{table\}(?:\[[^\]]*\])?([\s\S]*?)\\end\{table\}", RegexOptions.Singleline);
                if (tableMatch.Success)
                    matches.Add((tableMatch, "table"));
            }

            // Find the first match
            if (matches.Count == 0)
            {
                // No more special elements - add remaining as paragraphs
                AddParagraphs(remaining, document, ref elementOrder);
                break;
            }

            var firstMatch = matches.OrderBy(m => m.match.Index).First();

            // Add text before the match as paragraphs
            if (firstMatch.match.Index > 0)
            {
                var textBefore = remaining[..firstMatch.match.Index];
                AddParagraphs(textBefore, document, ref elementOrder);
            }

            // Handle the matched element
            switch (firstMatch.type)
            {
                case "section":
                    var sectionType = firstMatch.match.Groups[1].Value;
                    var sectionTitle = firstMatch.match.Groups[2].Value;
                    var level = sectionType switch
                    {
                        "section" => 1,
                        "subsection" => 2,
                        "subsubsection" => 3,
                        _ => 1
                    };

                    if (level >= options.MinHeadingLevelForSection && level <= options.MaxHeadingLevelForSection)
                    {
                        document.Elements.Add(new ImportHeading
                        {
                            Order = elementOrder++,
                            Level = level,
                            Text = sectionTitle
                        });
                    }
                    else
                    {
                        document.Elements.Add(new ImportParagraph
                        {
                            Order = elementOrder++,
                            Text = sectionTitle,
                            Style = ParagraphStyle.Title,
                            Formatting = [new FormattingSpan { Start = 0, Length = sectionTitle.Length, Type = FormattingType.Bold }]
                        });
                    }
                    break;

                case "equation_env":
                    document.Elements.Add(new ImportEquation
                    {
                        Order = elementOrder++,
                        LatexContent = firstMatch.match.Groups[2].Value.Trim(),
                        ConversionSucceeded = true,
                        IsInline = false
                    });
                    break;

                case "displaymath_dollar":
                case "displaymath_bracket":
                    document.Elements.Add(new ImportEquation
                    {
                        Order = elementOrder++,
                        LatexContent = firstMatch.match.Groups[1].Value.Trim(),
                        ConversionSucceeded = true,
                        IsInline = false
                    });
                    break;

                case "code":
                    var language = firstMatch.match.Groups[1].Value == "minted" ? "python" : null;
                    document.Elements.Add(new ImportCodeBlock
                    {
                        Order = elementOrder++,
                        Text = firstMatch.match.Groups[2].Value.Trim(),
                        Language = language,
                        DetectionReason = CodeBlockDetectionReason.StyleName
                    });
                    break;

                case "figure":
                    // Extract includegraphics and caption
                    var figContent = firstMatch.match.Groups[1].Value;
                    var graphicsMatch = Regex.Match(figContent, @"\\includegraphics(?:\[[^\]]*\])?\{([^}]+)\}");
                    var captionMatch = Regex.Match(figContent, @"\\caption\{([^}]+)\}");

                    document.Elements.Add(new ImportImage
                    {
                        Order = elementOrder++,
                        Filename = graphicsMatch.Success ? graphicsMatch.Groups[1].Value : null,
                        AltText = captionMatch.Success ? captionMatch.Groups[1].Value : null,
                        Data = []
                    });
                    break;

                case "table":
                    var tableContent = firstMatch.match.Groups[1].Value;
                    var tabularMatch = Regex.Match(tableContent, @"\\begin\{tabular\}\{([^}]*)\}([\s\S]*?)\\end\{tabular\}", RegexOptions.Singleline);

                    if (tabularMatch.Success)
                    {
                        var table = ParseTabular(tabularMatch.Groups[2].Value);
                        table.Order = elementOrder++;
                        document.Elements.Add(table);
                    }
                    else
                    {
                        // Store raw table content
                        document.Elements.Add(new ImportParagraph
                        {
                            Order = elementOrder++,
                            Text = tableContent.Trim(),
                            Style = ParagraphStyle.Normal
                        });
                        document.Warnings.Add(new ImportWarning
                        {
                            Type = ImportWarningType.UnsupportedElement,
                            Message = "Could not parse table structure"
                        });
                    }
                    break;
            }

            // Continue with remaining content
            remaining = remaining[(firstMatch.match.Index + firstMatch.match.Length)..];
        }
    }

    private static void AddParagraphs(string content, ImportDocument document, ref int elementOrder)
    {
        // Split by double newlines to get paragraphs
        var paragraphs = Regex.Split(content, @"\n\s*\n")
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p));

        foreach (var para in paragraphs)
        {
            // Skip if it's just LaTeX commands
            if (Regex.IsMatch(para, @"^\\(label|ref|cite|newpage|clearpage|vspace|hspace|centering)\b"))
                continue;

            // Skip empty or comment-only content
            if (string.IsNullOrWhiteSpace(para) || para.StartsWith("%"))
                continue;

            // Detect inline formatting
            var formatting = ParseLatexFormatting(para);

            document.Elements.Add(new ImportParagraph
            {
                Order = elementOrder++,
                Text = para,
                Style = ParagraphStyle.Normal,
                Formatting = formatting
            });
        }
    }

    private static List<FormattingSpan> ParseLatexFormatting(string text)
    {
        var spans = new List<FormattingSpan>();

        // \textbf{...}
        foreach (Match match in Regex.Matches(text, @"\\textbf\{([^}]+)\}"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.Bold
            });
        }

        // \textit{...} or \emph{...}
        foreach (Match match in Regex.Matches(text, @"\\(textit|emph)\{([^}]+)\}"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.Italic
            });
        }

        // \underline{...}
        foreach (Match match in Regex.Matches(text, @"\\underline\{([^}]+)\}"))
        {
            spans.Add(new FormattingSpan
            {
                Start = match.Index,
                Length = match.Length,
                Type = FormattingType.Underline
            });
        }

        // \texttt{...}
        foreach (Match match in Regex.Matches(text, @"\\texttt\{([^}]+)\}"))
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

    private static ImportTable ParseTabular(string tabularContent)
    {
        var table = new ImportTable();
        var rows = tabularContent.Split(new[] { @"\\" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rowStr in rows)
        {
            var trimmedRow = rowStr.Trim();
            if (string.IsNullOrEmpty(trimmedRow) || trimmedRow.StartsWith("\\hline") || trimmedRow.StartsWith("\\cline"))
                continue;

            // Remove \hline at the end
            trimmedRow = Regex.Replace(trimmedRow, @"\\hline.*$", "").Trim();

            if (string.IsNullOrEmpty(trimmedRow))
                continue;

            var cells = trimmedRow.Split('&');
            var row = new List<ImportTableCell>();

            foreach (var cell in cells)
            {
                row.Add(new ImportTableCell
                {
                    Text = cell.Trim(),
                    Formatting = ParseLatexFormatting(cell.Trim())
                });
            }

            if (row.Count > 0)
                table.Rows.Add(row);
        }

        // Assume first row is header if table has multiple rows
        table.HasHeaderRow = table.Rows.Count > 1;

        return table;
    }
}
