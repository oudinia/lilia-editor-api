using Lilia.Import.Models;
using Lilia.Import.Services;
using M = DocumentFormat.OpenXml.Math;

namespace Lilia.Import.Detection;

/// <summary>
/// Registry of all default element detection rules.
/// Encodes the exact behavior of the original DocxParser.ParseParagraph as composable rules.
/// </summary>
public static class DefaultRuleRegistry
{
    /// <summary>
    /// Build the complete set of default detection rules based on the given import options.
    /// </summary>
    public static List<ElementDetectionRule> BuildRules(ImportOptions options)
    {
        var rules = new List<ElementDetectionRule>();

        // === Priority 0-99: Page breaks ===
        rules.Add(PageBreakRule(options));

        // === Priority 100-199: Headings ===
        // Title/Subtitle rules must come BEFORE heading.custom (110), because
        // heading.custom matches StyleId containing "title" which would steal
        // "Title"-styled paragraphs (including abstract headings).
        rules.Add(HeadingStyleRule(options));
        rules.Add(AbstractTitleStyleRule(options));   // Priority 105: Title/Subtitle + abstract keyword → consumed
        rules.Add(TitleSubtitleOtherRule());           // Priority 106: remaining Title/Subtitle → paragraph
        rules.Add(HeadingCustomStyleRule());
        rules.Add(HeadingOutlineRule(options));
        if (options.DetectHeadingsByFormatting)
        {
            rules.Add(HeadingFormattingRule());
        }

        // === Priority 300-399: Lists ===
        rules.Add(ListRule());

        // === Priority 400-499: Equations ===
        rules.Add(EquationRule());

        // === Priority 500-599: Section-context types ===
        if (options.DetectAbstractByStyle)
        {
            rules.Add(AbstractStyleRule(options));
        }
        rules.Add(AbstractSectionRule());

        // === Priority 520: Bibliography entries ===
        if (options.DetectBibliographyEntries)
        {
            rules.Add(BibliographySectionRule());
        }

        // === Priority 600-699: Semantic types (theorem, blockquote) ===
        if (options.DetectTheoremEnvironments)
        {
            rules.Add(TheoremStyleRule(options));
            rules.Add(TheoremContentRule());
        }
        if (options.DetectBlockquotesByStyle)
        {
            rules.Add(BlockquoteStyleRule(options));
        }
        if (options.DetectBlockquotesByIndent)
        {
            rules.Add(BlockquoteIndentRule());
            rules.Add(BlockquoteBorderRule());
        }

        // === Priority 700-799: Code blocks ===
        if (options.DetectCodeByStyle)
        {
            rules.Add(CodeStyleRule(options));
        }
        if (options.DetectCodeByFont)
        {
            rules.Add(CodeFontRule(options));
        }
        if (options.DetectCodeByShading)
        {
            rules.Add(CodeShadingRule());
        }

        // === Priority 800-899: Images ===
        if (options.ExtractImages)
        {
            rules.Add(ImageRule());
        }

        // === Priority 900-999: Fallback ===
        rules.Add(ParagraphFallbackRule());

        return rules;
    }

    // ========================================================================
    // Page break rules (0-99)
    // ========================================================================

    private static ElementDetectionRule PageBreakRule(ImportOptions options)
    {
        return new ElementDetectionRule
        {
            Id = "pagebreak",
            Name = "Page Break",
            Priority = 10,
            TargetType = ImportElementType.PageBreak,
            Condition = new OpenXmlCondition { HasPageBreaks = true },
            CreateElements = (analysis, parser) =>
            {
                var results = new List<ImportElement>();

                // Add page break element(s)
                var pageBreaks = analysis.RawParagraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Break>()
                    .Where(br => br.Type?.Value == DocumentFormat.OpenXml.Wordprocessing.BreakValues.Page)
                    .ToList();

                foreach (var _ in pageBreaks)
                {
                    results.Add(new ImportPageBreak { Order = parser.NextElementOrder() });
                }

                // If paragraph has content besides the page break, handle it
                if (!string.IsNullOrWhiteSpace(analysis.Text))
                {
                    if (SectionKeywordRegistry.IsAbstractKeyword(analysis.Text) && options.DetectAbstractByStyle)
                    {
                        // Don't emit a heading — the AbstractBlock component renders its own title.
                        // The OnMatch handler below sets tracker.InAbstractSection = true.
                    }
                    else
                    {
                        // Create a paragraph or heading from the remaining content
                        var contentElement = parser.CreateParagraphElement(analysis.RawParagraph, analysis.StyleId);
                        if (contentElement != null)
                        {
                            results.Add(contentElement);
                        }
                    }
                }

                return results.Count > 0 ? results : null;
            },
            OnMatch = (analysis, tracker) =>
            {
                if (!string.IsNullOrWhiteSpace(analysis.Text))
                {
                    if (SectionKeywordRegistry.IsAbstractKeyword(analysis.Text) && options.DetectAbstractByStyle)
                    {
                        tracker.InAbstractSection = true;
                        tracker.OnHeadingEncountered(analysis.Text, 1);
                    }
                    else
                    {
                        // Check if the remaining content is a heading that ends abstract
                        var headingLevel = GetHeadingLevelFromAnalysis(analysis, options);
                        if (headingLevel.HasValue)
                        {
                            tracker.InAbstractSection = false;
                            tracker.OnHeadingEncountered(analysis.Text, headingLevel.Value);
                        }
                    }
                }
            }
        };
    }

    // ========================================================================
    // Heading rules (100-199)
    // ========================================================================

    private static ElementDetectionRule HeadingStyleRule(ImportOptions options)
    {
        return new ElementDetectionRule
        {
            Id = "heading.style",
            Name = "Heading by Style",
            Priority = 100,
            TargetType = ImportElementType.Heading,
            Condition = new DetectionConditionFunc(analysis =>
            {
                if (string.IsNullOrEmpty(analysis.StyleId)) return false;
                if (!System.Text.RegularExpressions.Regex.IsMatch(analysis.StyleId, @"^Heading\d+$")) return false;
                var levelStr = analysis.StyleId.Substring(7);
                if (!int.TryParse(levelStr, out var level) || level < 1 || level > 9) return false;
                return level >= options.MinHeadingLevelForSection && level <= options.MaxHeadingLevelForSection;
            }, "Heading style with level in configured range"),
            CreateElements = (analysis, parser) =>
            {
                var level = int.Parse(analysis.StyleId!.Substring(7));

                return
                [
                    new ImportHeading
                    {
                        Order = parser.NextElementOrder(),
                        Level = level,
                        Text = analysis.Text,
                        Formatting = analysis.Formatting,
                        StyleId = analysis.StyleId
                    }
                ];
            },
            OnMatch = (analysis, tracker) =>
            {
                var level = int.Parse(analysis.StyleId!.Substring(7));
                tracker.OnHeadingEncountered(analysis.Text, level);
            }
        };
    }

    private static ElementDetectionRule HeadingCustomStyleRule()
    {
        return new ElementDetectionRule
        {
            Id = "heading.custom",
            Name = "Heading by Custom Style",
            Priority = 110,
            TargetType = ImportElementType.Heading,
            Condition = new CompositeCondition(CompositeCondition.CompositeMode.Or,
                CompositeCondition.And(
                    new StyleMatchCondition("title", StyleMatchMode.Contains),
                    new NotCondition(new StyleMatchCondition("subtitle", StyleMatchMode.Contains))
                ),
                new StyleMatchCondition("section", StyleMatchMode.Contains),
                new StyleMatchCondition("chapter", StyleMatchMode.Contains)
            ),
            CreateElements = (analysis, parser) =>
            {
                var level = 1;
                var lowerStyle = analysis.StyleId!.ToLowerInvariant();

                if (lowerStyle.Contains("section") || lowerStyle.Contains("chapter"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(analysis.StyleId!, @"\d+");
                    if (match.Success && int.TryParse(match.Value, out var lvl) && lvl >= 1 && lvl <= 6)
                        level = lvl;
                }

                return
                [
                    new ImportHeading
                    {
                        Order = parser.NextElementOrder(),
                        Level = level,
                        Text = analysis.Text,
                        Formatting = analysis.Formatting,
                        StyleId = analysis.StyleId
                    }
                ];
            },
            OnMatch = (analysis, tracker) =>
            {
                tracker.OnHeadingEncountered(analysis.Text, 1);
            }
        };
    }

    private static ElementDetectionRule HeadingOutlineRule(ImportOptions options)
    {
        return new ElementDetectionRule
        {
            Id = "heading.outline",
            Name = "Heading by Outline Level",
            Priority = 120,
            TargetType = ImportElementType.Heading,
            Condition = new DetectionConditionFunc(analysis =>
            {
                if (!analysis.OutlineLevel.HasValue) return false;
                var level = analysis.OutlineLevel.Value + 1;
                return level >= options.MinHeadingLevelForSection && level <= options.MaxHeadingLevelForSection;
            }, "OutlineLevel in range"),
            CreateElements = (analysis, parser) =>
            {
                var level = analysis.OutlineLevel!.Value + 1;
                return
                [
                    new ImportHeading
                    {
                        Order = parser.NextElementOrder(),
                        Level = level,
                        Text = analysis.Text,
                        Formatting = analysis.Formatting,
                        StyleId = analysis.StyleId
                    }
                ];
            },
            OnMatch = (analysis, tracker) =>
            {
                var level = analysis.OutlineLevel!.Value + 1;
                tracker.OnHeadingEncountered(analysis.Text, level);
            }
        };
    }

    private static ElementDetectionRule HeadingFormattingRule()
    {
        return new ElementDetectionRule
        {
            Id = "heading.formatting",
            Name = "Heading by Formatting Heuristics",
            Priority = 130,
            TargetType = ImportElementType.Heading,
            Condition = new DetectionConditionFunc(analysis =>
            {
                return DetectHeadingByFormatting(analysis) != null;
            }, "Formatting-based heading heuristics"),
            CreateElements = (analysis, parser) =>
            {
                var level = DetectHeadingByFormatting(analysis);
                if (!level.HasValue) return null;

                return
                [
                    new ImportHeading
                    {
                        Order = parser.NextElementOrder(),
                        Level = level.Value,
                        Text = analysis.Text,
                        Formatting = analysis.Formatting,
                        StyleId = analysis.StyleId
                    }
                ];
            },
            OnMatch = (analysis, tracker) =>
            {
                var level = DetectHeadingByFormatting(analysis);
                if (level.HasValue)
                {
                    tracker.OnHeadingEncountered(analysis.Text, level.Value);
                }
            }
        };
    }

    // ========================================================================
    // Title/Subtitle abstract reclassification (200-299)
    // ========================================================================

    private static ElementDetectionRule AbstractTitleStyleRule(ImportOptions options)
    {
        return new ElementDetectionRule
        {
            Id = "abstract.title-style",
            Name = "Abstract via Title/Subtitle Style",
            Priority = 105,
            TargetType = ImportElementType.Abstract,
            Condition = CompositeCondition.And(
                CompositeCondition.Or(
                    new StyleMatchCondition("Title", StyleMatchMode.Exact),
                    new StyleMatchCondition("Subtitle", StyleMatchMode.Exact)
                ),
                new DetectionConditionFunc(a => SectionKeywordRegistry.IsAbstractKeyword(a.Text), "Text is abstract keyword")
            ),
            // Return empty list = consumed without output.
            // The AbstractBlock component renders its own "ABSTRACT" title,
            // so we don't need to emit a separate heading.
            CreateElements = (_, _) => [],
            OnMatch = (analysis, tracker) =>
            {
                tracker.InAbstractSection = true;
                tracker.OnHeadingEncountered(analysis.Text, 1);
            }
        };
    }

    private static ElementDetectionRule TitleSubtitleOtherRule()
    {
        return new ElementDetectionRule
        {
            Id = "title-subtitle.other",
            Name = "Title/Subtitle (non-abstract)",
            Priority = 106,
            TargetType = ImportElementType.Paragraph,
            Condition = CompositeCondition.Or(
                new StyleMatchCondition("Title", StyleMatchMode.Exact),
                new StyleMatchCondition("Subtitle", StyleMatchMode.Exact)
            ),
            CreateElements = (analysis, parser) =>
            {
                // Title/Subtitle paragraphs that aren't abstract headings — treat as normal paragraphs
                if (string.IsNullOrWhiteSpace(analysis.Text))
                    return null;

                return
                [
                    new ImportParagraph
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        Formatting = parser.ShouldPreserveFormatting() ? analysis.Formatting : [],
                        StyleId = analysis.StyleId,
                        Style = analysis.StyleId?.Equals("Title", StringComparison.OrdinalIgnoreCase) == true
                            ? ParagraphStyle.Title
                            : ParagraphStyle.Subtitle
                    }
                ];
            },
            OnMatch = (_, tracker) =>
            {
                if (tracker.InAbstractSection)
                    tracker.EndAbstractSection();
            }
        };
    }

    // ========================================================================
    // List rules (300-399)
    // ========================================================================

    private static ElementDetectionRule ListRule()
    {
        return new ElementDetectionRule
        {
            Id = "list",
            Name = "List Item",
            Priority = 300,
            TargetType = ImportElementType.ListItem,
            Condition = new NumberingCondition { HasNumbering = true },
            CreateElements = (analysis, parser) =>
            {
                var listItem = parser.CreateListItemElement(
                    analysis.RawParagraph,
                    analysis.NumberingProperties!,
                    analysis.MainDocumentPart);
                return listItem != null ? [listItem] : null;
            },
            OnMatch = (_, tracker) =>
            {
                tracker.EndAbstractSection();
            }
        };
    }

    // ========================================================================
    // Equation rules (400-499)
    // ========================================================================

    private static ElementDetectionRule EquationRule()
    {
        return new ElementDetectionRule
        {
            Id = "equation",
            Name = "Equation",
            Priority = 400,
            TargetType = ImportElementType.Equation,
            Condition = new DetectionConditionFunc(analysis =>
            {
                return analysis.HasMathElements && analysis.MathElements.Count > 0
                    && parser_IsEquationOnlyParagraph(analysis);
            }, "Paragraph contains only math elements"),
            CreateElements = (analysis, parser) =>
            {
                return parser.CreateEquationElements(analysis.MathElements);
            }
        };
    }

    private static bool parser_IsEquationOnlyParagraph(ParagraphAnalysis analysis)
    {
        // Check if the paragraph contains only math and whitespace
        return string.IsNullOrWhiteSpace(analysis.Text) || analysis.MathElements.Count > 0;
    }

    // ========================================================================
    // Abstract rules (500-599)
    // ========================================================================

    private static ElementDetectionRule AbstractStyleRule(ImportOptions options)
    {
        return new ElementDetectionRule
        {
            Id = "abstract.style",
            Name = "Abstract by Style",
            Priority = 500,
            TargetType = ImportElementType.Abstract,
            Condition = new StyleSetCondition(options.AbstractStylePatterns),
            CreateElements = (analysis, parser) =>
            {
                if (string.IsNullOrWhiteSpace(analysis.Text))
                    return null;

                return
                [
                    new ImportAbstract
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        Formatting = parser.ShouldPreserveFormatting() ? analysis.Formatting : [],
                        StyleId = analysis.StyleId
                    }
                ];
            }
        };
    }

    private static ElementDetectionRule AbstractSectionRule()
    {
        return new ElementDetectionRule
        {
            Id = "abstract.section",
            Name = "Abstract by Section Context",
            Priority = 510,
            TargetType = ImportElementType.Abstract,
            Condition = new SectionContextCondition(inAbstractSection: true),
            CreateElements = (analysis, parser) =>
            {
                if (string.IsNullOrWhiteSpace(analysis.Text))
                    return null;

                return
                [
                    new ImportAbstract
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        Formatting = parser.ShouldPreserveFormatting() ? analysis.Formatting : [],
                        StyleId = analysis.StyleId
                    }
                ];
            }
        };
    }

    // ========================================================================
    // Code block rules (700-799)
    // ========================================================================

    private static ElementDetectionRule CodeStyleRule(ImportOptions options)
    {
        return new ElementDetectionRule
        {
            Id = "code.style",
            Name = "Code Block by Style",
            Priority = 700,
            TargetType = ImportElementType.CodeBlock,
            Condition = new StyleSetCondition(options.CodeStylePatterns),
            CreateElements = (analysis, parser) =>
            {
                return
                [
                    new ImportCodeBlock
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        StyleId = analysis.StyleId,
                        DetectionReason = CodeBlockDetectionReason.StyleName,
                        FontFamily = analysis.FontFamily
                    }
                ];
            }
        };
    }

    private static ElementDetectionRule CodeFontRule(ImportOptions options)
    {
        return new ElementDetectionRule
        {
            Id = "code.font",
            Name = "Code Block by Monospace Font",
            Priority = 710,
            TargetType = ImportElementType.CodeBlock,
            Condition = new FontMatchCondition(options.MonospaceFonts),
            CreateElements = (analysis, parser) =>
            {
                return
                [
                    new ImportCodeBlock
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        StyleId = analysis.StyleId,
                        DetectionReason = CodeBlockDetectionReason.MonospaceFont,
                        FontFamily = analysis.FontFamily
                    }
                ];
            }
        };
    }

    private static ElementDetectionRule CodeShadingRule()
    {
        return new ElementDetectionRule
        {
            Id = "code.shading",
            Name = "Code Block by Shading",
            Priority = 720,
            TargetType = ImportElementType.CodeBlock,
            Condition = new ShadingCondition(),
            CreateElements = (analysis, parser) =>
            {
                return
                [
                    new ImportCodeBlock
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        StyleId = analysis.StyleId,
                        DetectionReason = CodeBlockDetectionReason.Shading,
                        FontFamily = analysis.FontFamily
                    }
                ];
            }
        };
    }

    // ========================================================================
    // Image rules (800-899)
    // ========================================================================

    private static ElementDetectionRule ImageRule()
    {
        return new ElementDetectionRule
        {
            Id = "image",
            Name = "Image",
            Priority = 800,
            TargetType = ImportElementType.Image,
            Condition = new OpenXmlCondition { HasDrawings = true },
            CreateElements = (analysis, parser) =>
            {
                var images = parser.ExtractImagesFromParagraph(analysis.RawParagraph, analysis.MainDocumentPart);
                if (images.Count == 0)
                    return null;

                foreach (var img in images)
                {
                    img.Order = parser.NextElementOrder();
                }

                // If paragraph has only images, return just the images
                if (string.IsNullOrWhiteSpace(analysis.Text))
                {
                    return images;
                }

                // Otherwise, include both the paragraph and images
                var results = new List<ImportElement>();
                var paragraph = parser.CreateParagraphElement(analysis.RawParagraph, analysis.StyleId);
                if (paragraph != null)
                {
                    results.Add(paragraph);
                }
                results.AddRange(images);
                return results;
            }
        };
    }

    // ========================================================================
    // Fallback (900-999)
    // ========================================================================

    private static ElementDetectionRule ParagraphFallbackRule()
    {
        return new ElementDetectionRule
        {
            Id = "paragraph",
            Name = "Paragraph (Fallback)",
            Priority = 999,
            TargetType = ImportElementType.Paragraph,
            Condition = new AlwaysTrueCondition(),
            CreateElements = (analysis, parser) =>
            {
                var paragraph = parser.CreateParagraphElement(analysis.RawParagraph, analysis.StyleId);
                // Return [paragraph] if non-empty, or [] for empty paragraphs (consumed, not "unmatched")
                return paragraph != null ? [paragraph] : [];
            }
        };
    }

    // ========================================================================
    // Bibliography rules (520)
    // ========================================================================

    private static ElementDetectionRule BibliographySectionRule()
    {
        return new ElementDetectionRule
        {
            Id = "bibliography.section",
            Name = "Bibliography Entry by Section Context",
            Priority = 520,
            TargetType = ImportElementType.BibliographyEntry,
            Condition = CompositeCondition.And(
                new SectionContextCondition(allowedSections: [SectionType.References]),
                new DetectionConditionFunc(analysis =>
                {
                    if (string.IsNullOrWhiteSpace(analysis.Text)) return false;
                    var text = analysis.Text.Trim();

                    // Numbered pattern: [1], [2], etc.
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\[\d+\]"))
                        return true;

                    // Numbered pattern: 1. or 2. at start
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\.\s"))
                        return true;

                    // Author-year pattern: starts with capitalized word(s) followed by year in parentheses or comma
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^[A-Z][a-z]+.*(?:\(\d{4}\)|\,\s*\d{4})"))
                        return true;

                    // Hanging indent (common for references)
                    if (analysis.IndentLeftTwips >= 360 && text.Length > 20)
                        return true;

                    return false;
                }, "Text matches bibliography entry patterns")
            ),
            CreateElements = (analysis, parser) =>
            {
                if (string.IsNullOrWhiteSpace(analysis.Text))
                    return null;

                // Extract reference label
                string? label = null;
                var labelMatch = System.Text.RegularExpressions.Regex.Match(analysis.Text.Trim(), @"^\[(\d+)\]");
                if (labelMatch.Success)
                {
                    label = labelMatch.Groups[1].Value;
                }

                return
                [
                    new ImportBibliographyEntry
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        Formatting = parser.ShouldPreserveFormatting() ? analysis.Formatting : [],
                        StyleId = analysis.StyleId,
                        ReferenceLabel = label,
                        DetectionReason = BibliographyDetectionReason.SectionContext
                    }
                ];
            }
        };
    }

    // ========================================================================
    // Theorem rules (600-699)
    // ========================================================================

    private static ElementDetectionRule TheoremStyleRule(ImportOptions options)
    {
        return new ElementDetectionRule
        {
            Id = "theorem.style",
            Name = "Theorem by Style",
            Priority = 600,
            TargetType = ImportElementType.Theorem,
            Condition = new StyleSetCondition(options.TheoremStylePatterns),
            CreateElements = (analysis, parser) =>
            {
                if (string.IsNullOrWhiteSpace(analysis.Text))
                    return null;

                var envType = ClassifyTheoremStyle(analysis.StyleId);

                return
                [
                    new ImportTheorem
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        Formatting = parser.ShouldPreserveFormatting() ? analysis.Formatting : [],
                        StyleId = analysis.StyleId,
                        EnvironmentType = envType,
                        DetectionReason = TheoremDetectionReason.StyleName
                    }
                ];
            }
        };
    }

    private static ElementDetectionRule TheoremContentRule()
    {
        return new ElementDetectionRule
        {
            Id = "theorem.content",
            Name = "Theorem by Content Pattern",
            Priority = 610,
            TargetType = ImportElementType.Theorem,
            Condition = CompositeCondition.And(
                new ContentPatternCondition(
                    @"^(Theorem|Lemma|Proposition|Corollary|Conjecture|Definition|Example|Remark|Proof|Axiom|Assumption)\s*(\d[\d.]*)?\.?\s",
                    ContentPatternCondition.MatchMode.StartsWith,
                    ignoreCase: true),
                new DetectionConditionFunc(analysis =>
                {
                    // First run should be bold (the "Theorem N." label)
                    return analysis.Runs.Count > 0 && analysis.Runs[0].RunProperties?.Bold != null;
                }, "First run is bold")
            ),
            CreateElements = (analysis, parser) =>
            {
                if (string.IsNullOrWhiteSpace(analysis.Text))
                    return null;

                var match = System.Text.RegularExpressions.Regex.Match(
                    analysis.Text,
                    @"^(Theorem|Lemma|Proposition|Corollary|Conjecture|Definition|Example|Remark|Proof|Axiom|Assumption)\s*(\d[\d.]*)?\.\s*(.*)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

                var envType = TheoremEnvironmentType.Theorem;
                string? number = null;
                var bodyText = analysis.Text;

                if (match.Success)
                {
                    envType = ClassifyTheoremKeyword(match.Groups[1].Value);
                    number = match.Groups[2].Success ? match.Groups[2].Value : null;
                    bodyText = match.Groups[3].Value;
                }

                return
                [
                    new ImportTheorem
                    {
                        Order = parser.NextElementOrder(),
                        Text = bodyText,
                        Formatting = parser.ShouldPreserveFormatting() ? analysis.Formatting : [],
                        StyleId = analysis.StyleId,
                        EnvironmentType = envType,
                        Number = number,
                        DetectionReason = TheoremDetectionReason.ContentPattern
                    }
                ];
            }
        };
    }

    // ========================================================================
    // Blockquote rules (620-640)
    // ========================================================================

    private static ElementDetectionRule BlockquoteStyleRule(ImportOptions options)
    {
        return new ElementDetectionRule
        {
            Id = "blockquote.style",
            Name = "Blockquote by Style",
            Priority = 620,
            TargetType = ImportElementType.Blockquote,
            Condition = new StyleSetCondition(options.BlockquoteStylePatterns),
            CreateElements = (analysis, parser) =>
            {
                if (string.IsNullOrWhiteSpace(analysis.Text))
                    return null;

                return
                [
                    new ImportBlockquote
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        Formatting = parser.ShouldPreserveFormatting() ? analysis.Formatting : [],
                        StyleId = analysis.StyleId,
                        DetectionReason = BlockquoteDetectionReason.StyleName
                    }
                ];
            }
        };
    }

    private static ElementDetectionRule BlockquoteIndentRule()
    {
        return new ElementDetectionRule
        {
            Id = "blockquote.indent",
            Name = "Blockquote by Indent + Italic",
            Priority = 630,
            TargetType = ImportElementType.Blockquote,
            Condition = CompositeCondition.And(
                new IndentCondition { MinIndentTwips = 720 },
                new NumberingCondition { HasNumbering = false },
                new FormattingCondition { Italic = true }
            ),
            CreateElements = (analysis, parser) =>
            {
                if (string.IsNullOrWhiteSpace(analysis.Text))
                    return null;

                return
                [
                    new ImportBlockquote
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        Formatting = parser.ShouldPreserveFormatting() ? analysis.Formatting : [],
                        StyleId = analysis.StyleId,
                        DetectionReason = BlockquoteDetectionReason.IndentItalic
                    }
                ];
            }
        };
    }

    private static ElementDetectionRule BlockquoteBorderRule()
    {
        return new ElementDetectionRule
        {
            Id = "blockquote.border",
            Name = "Blockquote by Left Border",
            Priority = 640,
            TargetType = ImportElementType.Blockquote,
            Condition = CompositeCondition.And(
                new IndentCondition { HasLeftBorder = true },
                new NumberingCondition { HasNumbering = false }
            ),
            CreateElements = (analysis, parser) =>
            {
                if (string.IsNullOrWhiteSpace(analysis.Text))
                    return null;

                return
                [
                    new ImportBlockquote
                    {
                        Order = parser.NextElementOrder(),
                        Text = analysis.Text,
                        Formatting = parser.ShouldPreserveFormatting() ? analysis.Formatting : [],
                        StyleId = analysis.StyleId,
                        DetectionReason = BlockquoteDetectionReason.LeftBorder
                    }
                ];
            }
        };
    }

    // ========================================================================
    // Helper: Heading detection by formatting (mirrors original DetectHeadingByFormatting)
    // ========================================================================

    internal static int? DetectHeadingByFormatting(ParagraphAnalysis analysis)
    {
        var text = analysis.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 200)
            return null;

        var allBold = analysis.AllBold;
        var fontSize = analysis.FontSizePoints;

        // Pattern: Numbered section headings like "1. Introduction" or "1.1 Methods"
        var numberedPattern = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+(?:\.\d+)*)\s*\.?\s+([A-Z])");
        if (numberedPattern.Success)
        {
            var numberPart = numberedPattern.Groups[1].Value;
            var dotCount = numberPart.Count(c => c == '.');
            var level = Math.Min(dotCount + 1, 6);

            if (dotCount >= 1 && text.Length < 100)
            {
                return level;
            }

            if (allBold || (fontSize.HasValue && fontSize.Value >= 11))
            {
                return level;
            }
        }

        // Pattern: Roman numeral sections
        var romanPattern = System.Text.RegularExpressions.Regex.Match(text, @"^([IVXLC]+)\.\s+\w", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (romanPattern.Success && (allBold || (fontSize.HasValue && fontSize.Value >= 11)))
        {
            var roman = romanPattern.Groups[1].Value.ToUpperInvariant();
            return roman.Length <= 2 ? 1 : 2;
        }

        // Pattern: All caps short text
        if (text.Length <= 50 && text == text.ToUpperInvariant() && text.Any(char.IsLetter))
        {
            if (allBold || (fontSize.HasValue && fontSize.Value >= 12))
                return 1;
        }

        // Bold text with larger font size
        if (allBold && fontSize.HasValue && fontSize.Value >= 14)
            return 1;
        if (allBold && fontSize.HasValue && fontSize.Value >= 12)
            return 2;

        return null;
    }

    /// <summary>
    /// Helper to determine heading level from analysis (used by page break rule).
    /// </summary>
    private static int? GetHeadingLevelFromAnalysis(ParagraphAnalysis analysis, ImportOptions options)
    {
        if (!string.IsNullOrEmpty(analysis.StyleId))
        {
            if (analysis.StyleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
            {
                var levelStr = analysis.StyleId.Substring(7);
                if (int.TryParse(levelStr, out var level) && level >= 1 && level <= 9)
                {
                    if (level >= options.MinHeadingLevelForSection && level <= options.MaxHeadingLevelForSection)
                        return level;
                }
            }
        }

        if (analysis.OutlineLevel.HasValue)
        {
            var level = analysis.OutlineLevel.Value + 1;
            if (level >= options.MinHeadingLevelForSection && level <= options.MaxHeadingLevelForSection)
                return level;
        }

        if (options.DetectHeadingsByFormatting)
            return DetectHeadingByFormatting(analysis);

        return null;
    }

    // ========================================================================
    // Theorem classification helpers
    // ========================================================================

    private static TheoremEnvironmentType ClassifyTheoremStyle(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return TheoremEnvironmentType.Theorem;

        var lower = styleId.ToLowerInvariant();
        return ClassifyTheoremLower(lower);
    }

    private static TheoremEnvironmentType ClassifyTheoremKeyword(string keyword)
    {
        return ClassifyTheoremLower(keyword.ToLowerInvariant());
    }

    private static TheoremEnvironmentType ClassifyTheoremLower(string lower)
    {
        if (lower.Contains("lemma")) return TheoremEnvironmentType.Lemma;
        if (lower.Contains("proposition")) return TheoremEnvironmentType.Proposition;
        if (lower.Contains("corollary")) return TheoremEnvironmentType.Corollary;
        if (lower.Contains("conjecture")) return TheoremEnvironmentType.Conjecture;
        if (lower.Contains("definition")) return TheoremEnvironmentType.Definition;
        if (lower.Contains("example")) return TheoremEnvironmentType.Example;
        if (lower.Contains("remark")) return TheoremEnvironmentType.Remark;
        if (lower.Contains("proof")) return TheoremEnvironmentType.Proof;
        if (lower.Contains("algorithm")) return TheoremEnvironmentType.Algorithm;
        if (lower.Contains("exercise")) return TheoremEnvironmentType.Exercise;
        if (lower.Contains("solution")) return TheoremEnvironmentType.Solution;
        if (lower.Contains("axiom")) return TheoremEnvironmentType.Axiom;
        if (lower.Contains("assumption")) return TheoremEnvironmentType.Assumption;
        if (lower.Contains("note")) return TheoremEnvironmentType.Note;
        return TheoremEnvironmentType.Theorem;
    }
}

/// <summary>
/// A simple inline detection condition using a delegate, for one-off conditions.
/// </summary>
internal class DetectionConditionFunc : DetectionCondition
{
    private readonly Func<ParagraphAnalysis, bool> _func;
    private readonly string _description;

    public DetectionConditionFunc(Func<ParagraphAnalysis, bool> func, string description)
    {
        _func = func;
        _description = description;
    }

    public override bool Evaluate(ParagraphAnalysis analysis) => _func(analysis);
    public override string Description => _description;
}
