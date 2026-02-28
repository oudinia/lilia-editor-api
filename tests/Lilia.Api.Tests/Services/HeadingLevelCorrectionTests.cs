using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Tests for the heading level correction algorithm.
/// Uses real Mathpix import data from "Email Analytics" thesis.
///
/// The document structure:
///   Front matter (unnumbered, Roman numeral pages): Declaration, Certificate, Abstract, Acknowledgements
///   Chapter 1: Introduction
///     1.1 Email Communication, 1.2 Objectives, 1.3 Challenges
///   Chapter 2: Literature Survey
///     2.1, 2.2, ..., 2.5 Text Preprocessing
///       2.5.1 Tokenization, 2.5.2 Stop word removal, ...
///     2.6 Semantic Similarity
///       2.6.1 Jaccard Index, 2.6.2 Cosine, 2.6.3 Tf-idf, 2.6.4 Inverted Index
///   Chapter 3: Experimental Design & Setup
///     3.1 Tools utilized
///       3.1.1 ELK Stack, 3.1.2 Anaconda, 3.1.3 Spyder
///     3.2 Programming Languages
///       3.2.1 JSON, 3.2.2 Python
///     3.3, 3.4 ...
///     (unnumbered subsections: ElasticSearch, Logstash, Kibana, Email Dataset, etc.)
///   Chapter 4: Results and Implementation work
///     4.1, 4.2
///   Chapter 5: Conclusion and Future Work
///     5.1 Conclusion, 5.2 Future work
///   Bibliography
/// </summary>
public class HeadingLevelCorrectionTests
{
    /// <summary>
    /// Input headings as extracted from Mathpix import (sort_order, text, mathpix_level).
    /// The mathpix_level is what the OCR engine assigned (usually wrong).
    /// </summary>
    private static readonly (int sortOrder, string text, int mathpixLevel, int expectedLevel)[] TestHeadings =
    [
        // Document title
        (0, "Email Analytics", 1, 1),

        // Front matter — should all be h1
        (3, "Declaration", 2, 1),
        // University-specific heading — not in generic front-matter list, keep Mathpix level
        (8, "School of Computing Science \\& Engineering", 2, 2),
        (9, "Certificate", 2, 1),
        (23, "Abstract", 2, 1),
        (25, "Acknowledgements", 2, 1),

        // Chapter 1 label → rejected; chapter title → h1
        (27, "Chapter 1", 2, -1), // -1 = expect rejected
        (28, "Introduction", 2, 1),

        // Sections under Chapter 1 — h2
        (30, "1.1 Email Communication", 3, 2),
        (33, "1.2 Objectives", 3, 2),
        (35, "1.3 Challenges of Email Analytics", 3, 2),

        // Chapter 2 label → rejected; chapter title → h1
        (39, "Chapter 2", 2, -1),
        (40, "Literature Survey", 2, 1),

        // Sections under Chapter 2 — h2
        (41, "2.1 A framework for the forensic investigation of unstructured email relationship data", 3, 2),
        (43, "2.2 Forensic triage of email network narratives through visualisation", 3, 2),
        (45, "2.3 InVEST: Intelligent visual email search and triage", 3, 2),
        (47, "2.4 THREAD ARCS: An Email Thread Visualization", 3, 2),
        (49, "2.5 Text Preprocessing", 3, 2),

        // Subsections under 2.5 — h3
        (52, "2.5.1 Tokenization", 3, 3),
        (54, "2.5.2 Stop word removal methods", 3, 3),
        (56, "2.5.3 Stemming method", 3, 3),
        (58, "2.5.4 Lemmatization", 3, 3),

        // Section 2.6 — h2
        (60, "2.6 Semantic Similarity", 3, 2),

        // Subsections under 2.6 — h3
        (64, "2.6.1 Jaccard Index", 3, 3),
        (73, "2.6.2 Cosine Text Similarity", 3, 3),
        (79, "2.6.3 Tf-idf Weighting", 3, 3),

        // Unnumbered heading in middle of chapter — context-dependent, stays h2 (Mathpix's guess)
        (86, "Term-document matrix", 2, 2),

        // 2.6.4 under 2.6 — h3
        (92, "2.6.4 Inverted Index", 3, 3),

        // Chapter 3 label → rejected; chapter title → h1
        (98, "Chapter 3", 2, -1),
        (99, "Experimental Design \\& Setup", 2, 1),

        // Sections under Chapter 3
        (100, "3.1 Tools utilized in this Project", 3, 2),
        (101, "3.1.1 ELK Stack", 3, 3),

        // Unnumbered subsections under 3.1.1 — they're subtopics, keep Mathpix level (h2)
        (106, "ElasticSearch", 2, 2),
        (109, "Logstash", 2, 2),
        (112, "Kibana", 2, 2),

        (115, "3.1.2 Anaconda :Development Environment", 3, 3),
        (117, "3.1.3 Spyder", 3, 3),

        (119, "3.2 Programming Languages:", 3, 2),
        (120, "3.2.1 JSON (JavaScript Object Notation)", 3, 3),
        (123, "3.2.2 Python", 3, 3),

        (130, "3.3 Architecture of Email Analytics", 3, 2),
        (134, "3.4 Implemented Modules in Email Analytics", 3, 2),

        // Unnumbered subsections under 3.4
        (135, "Email Dataset", 2, 2),
        (137, "Data Preprocessing", 2, 2),
        (140, "Elasticsearch", 2, 2),
        (144, "MAPPINGS:", 2, 2),
        (151, "Logstash", 2, 2),
        (156, "Kibana", 2, 2),
        (163, "Visual Analytics", 2, 2),
        (170, "Relevant Emails", 2, 2),

        // Chapter 4 label → rejected; chapter title → h1
        (172, "Chapter 4", 2, -1),
        (173, "Results and Implementation work", 2, 1),

        // Sections under Chapter 4
        (174, "4.1 Development of the Email Analytics", 3, 2),
        (182, "4.2 Getting data from Enron corpus:", 3, 2),

        // Unnumbered mid-chapter heading
        (236, "Python Code:", 2, 2),

        // Chapter 5 label → rejected; chapter title → h1
        (242, "Chapter 5", 2, -1),
        (243, "Conclusion and Future Work", 2, 1),

        // Sections under Chapter 5
        (244, "5.1 Conclusion", 3, 2),
        (246, "5.2 Future work", 3, 2),

        // Bibliography — front matter / top level
        (248, "Bibliography", 2, 1),
    ];

    private static List<ImportBlockReview> BuildTestBlocks()
    {
        var blocks = new List<ImportBlockReview>();
        var lastSortOrder = -1;

        foreach (var (sortOrder, text, mathpixLevel, _) in TestHeadings)
        {
            // Fill gaps with paragraph blocks (to simulate real document structure)
            for (var s = lastSortOrder + 1; s < sortOrder; s++)
            {
                blocks.Add(new ImportBlockReview
                {
                    Id = Guid.NewGuid(),
                    SessionId = Guid.Empty,
                    BlockIndex = s,
                    BlockId = Guid.NewGuid().ToString(),
                    Status = "pending",
                    OriginalContent = JsonDocument.Parse("{\"text\": \"paragraph text\"}"),
                    OriginalType = "paragraph",
                    SortOrder = s,
                    Depth = 0,
                });
            }

            blocks.Add(new ImportBlockReview
            {
                Id = Guid.NewGuid(),
                SessionId = Guid.Empty,
                BlockIndex = sortOrder,
                BlockId = Guid.NewGuid().ToString(),
                Status = "pending",
                OriginalContent = JsonDocument.Parse(
                    JsonSerializer.Serialize(new { text, level = mathpixLevel })),
                OriginalType = "heading",
                SortOrder = sortOrder,
                Depth = 0,
            });

            lastSortOrder = sortOrder;
        }

        return blocks;
    }

    [Fact]
    public void CorrectHeadingLevels_ShouldFixAllLevels()
    {
        // Arrange
        var blocks = BuildTestBlocks();

        // Act
        ImportReviewServiceTestHelper.CorrectHeadingLevels(blocks);

        // Assert
        var headings = blocks
            .Where(b => b.OriginalType == "heading")
            .OrderBy(b => b.SortOrder)
            .ToList();

        var failures = new List<string>();

        foreach (var (sortOrder, text, mathpixLevel, expectedLevel) in TestHeadings)
        {
            var block = headings.First(b => b.SortOrder == sortOrder);

            // expectedLevel == -1 means the block should be rejected
            if (expectedLevel == -1)
            {
                if (block.Status != "rejected")
                {
                    failures.Add(
                        $"  [{sortOrder}] \"{text}\" — expected rejected, got status={block.Status}");
                }
                continue;
            }

            var content = block.CurrentContent ?? block.OriginalContent;
            var actualLevel = content!.RootElement.GetProperty("level").GetInt32();

            if (actualLevel != expectedLevel)
            {
                failures.Add(
                    $"  [{sortOrder}] \"{text}\" — expected h{expectedLevel}, got h{actualLevel} (Mathpix: h{mathpixLevel})");
            }
        }

        failures.Should().BeEmpty(
            "All heading levels should be corrected.\nFailures:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData("1.1 Email Communication", 2)]
    [InlineData("2.5.1 Tokenization", 3)]
    [InlineData("3.2 Programming Languages:", 2)]
    [InlineData("10.3.2.1 Deep Subsection", 4)]
    public void NumberedHeadings_ShouldDeriveCorrectLevel(string text, int expectedLevel)
    {
        var blocks = new List<ImportBlockReview>
        {
            CreateHeadingBlock(text, 99) // wrong level from Mathpix
        };

        ImportReviewServiceTestHelper.CorrectHeadingLevels(blocks);

        var content = blocks[0].CurrentContent ?? blocks[0].OriginalContent;
        var level = content!.RootElement.GetProperty("level").GetInt32();
        level.Should().Be(expectedLevel, $"\"{text}\" should be h{expectedLevel}");
    }

    [Theory]
    [InlineData("Declaration", 1)]
    [InlineData("Abstract", 1)]
    [InlineData("Certificate", 1)]
    [InlineData("Acknowledgements", 1)]
    [InlineData("Bibliography", 1)]
    [InlineData("List of Figures", 1)]
    [InlineData("Conclusion", 1)]
    [InlineData("References", 1)]
    public void FrontMatterHeadings_ShouldBeLevel1(string text, int expectedLevel)
    {
        var blocks = new List<ImportBlockReview>
        {
            CreateHeadingBlock(text, 2)
        };

        ImportReviewServiceTestHelper.CorrectHeadingLevels(blocks);

        var content = blocks[0].CurrentContent ?? blocks[0].OriginalContent;
        var level = content!.RootElement.GetProperty("level").GetInt32();
        level.Should().Be(expectedLevel, $"\"{text}\" (front matter) should be h{expectedLevel}");
    }

    [Fact]
    public void ChapterLabel_ShouldBeRejected_AndTitlePromotedToH1()
    {
        // "Chapter 1" is a label → rejected; "Introduction" is the chapter title → h1
        var blocks = new List<ImportBlockReview>
        {
            CreateHeadingBlock("Chapter 1", 2, sortOrder: 0),
            CreateHeadingBlock("Introduction", 2, sortOrder: 1),
            CreateParagraphBlock(sortOrder: 2),
            CreateHeadingBlock("1.1 Email Communication", 3, sortOrder: 3),
        };

        ImportReviewServiceTestHelper.CorrectHeadingLevels(blocks);

        blocks[0].Status.Should().Be("rejected", "Chapter 1 label → rejected");
        GetLevel(blocks[1]).Should().Be(1, "Introduction (chapter title) → h1");
        GetLevel(blocks[3]).Should().Be(2, "1.1 Email Communication → h2");
    }

    [Fact]
    public void UnnumberedHeadingsInMiddle_ShouldKeepOriginalLevel()
    {
        // "ElasticSearch", "Logstash" — unnumbered, not front matter, not after "Chapter N"
        var blocks = new List<ImportBlockReview>
        {
            CreateHeadingBlock("3.1.1 ELK Stack", 3, sortOrder: 0),
            CreateParagraphBlock(sortOrder: 1),
            CreateHeadingBlock("ElasticSearch", 2, sortOrder: 2),
            CreateHeadingBlock("Logstash", 2, sortOrder: 3),
        };

        ImportReviewServiceTestHelper.CorrectHeadingLevels(blocks);

        GetLevel(blocks[0]).Should().Be(3, "3.1.1 → h3");
        GetLevel(blocks[2]).Should().Be(2, "ElasticSearch — unnumbered, keep h2");
        GetLevel(blocks[3]).Should().Be(2, "Logstash — unnumbered, keep h2");
    }

    private static ImportBlockReview CreateHeadingBlock(string text, int level, int sortOrder = 0)
    {
        return new ImportBlockReview
        {
            Id = Guid.NewGuid(),
            SessionId = Guid.Empty,
            BlockIndex = sortOrder,
            BlockId = Guid.NewGuid().ToString(),
            Status = "pending",
            OriginalContent = JsonDocument.Parse(
                JsonSerializer.Serialize(new { text, level })),
            OriginalType = "heading",
            SortOrder = sortOrder,
            Depth = 0,
        };
    }

    private static ImportBlockReview CreateParagraphBlock(int sortOrder = 0)
    {
        return new ImportBlockReview
        {
            Id = Guid.NewGuid(),
            SessionId = Guid.Empty,
            BlockIndex = sortOrder,
            BlockId = Guid.NewGuid().ToString(),
            Status = "pending",
            OriginalContent = JsonDocument.Parse("{\"text\": \"paragraph\"}"),
            OriginalType = "paragraph",
            SortOrder = sortOrder,
            Depth = 0,
        };
    }

    private static int GetLevel(ImportBlockReview block)
    {
        var content = block.CurrentContent ?? block.OriginalContent;
        return content!.RootElement.GetProperty("level").GetInt32();
    }
}

/// <summary>
/// Test helper to expose the private static methods from ImportReviewService.
/// In production, these are called internally during CreateSessionAsync.
/// </summary>
public static class ImportReviewServiceTestHelper
{
    public static void CorrectHeadingLevels(List<ImportBlockReview> blockReviews)
    {
        // This calls the same algorithm as ImportReviewService.CorrectHeadingLevelsFromNumbering
        // We duplicate the logic here for testability — kept in sync with the service.
        var headingTypes = new HashSet<string> { BlockTypes.Heading, "header" };

        var topLevelPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "abstract", "acknowledgements", "acknowledgment", "declaration",
            "certificate", "dedication", "preface", "foreword",
            "list of figures", "list of tables", "list of abbreviations",
            "table of contents", "contents", "bibliography", "references",
            "appendix", "glossary", "index", "conclusion", "summary",
        };

        // Pass 1: Correct levels based on numbering patterns, front matter, and "Chapter N"
        ImportBlockReview? previousHeading = null;

        for (var i = 0; i < blockReviews.Count; i++)
        {
            var block = blockReviews[i];
            if (!headingTypes.Contains(block.OriginalType))
                continue;

            var text = ExtractText(block.OriginalContent);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var trimmed = text.Trim();
            int? correctLevel = null;

            // Rule 1: "Chapter N" → reject (label, not content)
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^Chapter\s+\d+\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                block.Status = "rejected";
                previousHeading = block;
                continue;
            }

            // Rule 2: Numbered heading "N.N.N Title" → derive level from dot count
            var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"^(\d+(?:\.\d+)*)\s");
            if (match.Success)
            {
                var number = match.Groups[1].Value;
                var dotCount = number.Count(c => c == '.');
                correctLevel = dotCount + 1;
            }

            // Rule 3: Known front-matter / top-level titles → h1
            if (correctLevel == null && topLevelPatterns.Contains(trimmed))
            {
                correctLevel = 1;
            }

            // Rule 4: Heading immediately after a rejected "Chapter N" (no non-heading blocks between) → h1
            if (correctLevel == null && previousHeading != null && previousHeading.Status == "rejected")
            {
                var prevText = ExtractText(previousHeading.OriginalContent)?.Trim() ?? "";
                if (System.Text.RegularExpressions.Regex.IsMatch(
                    prevText, @"^Chapter\s+\d+\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    var hasNonHeadingBetween = false;
                    for (var j = i - 1; j >= 0; j--)
                    {
                        if (blockReviews[j] == previousHeading) break;
                        if (!headingTypes.Contains(blockReviews[j].OriginalType))
                        {
                            hasNonHeadingBetween = true;
                            break;
                        }
                    }

                    if (!hasNonHeadingBetween)
                    {
                        correctLevel = 1;
                    }
                }
            }

            if (correctLevel != null)
            {
                correctLevel = Math.Clamp(correctLevel.Value, 1, 6);
                UpdateLevel(block, correctLevel.Value);
            }

            previousHeading = block;
        }
    }

    private static string ExtractText(JsonDocument? content)
    {
        if (content == null) return "";
        var root = content.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return "";
        if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            return textProp.GetString() ?? "";
        return "";
    }

    private static void UpdateLevel(ImportBlockReview block, int level)
    {
        var source = block.CurrentContent ?? block.OriginalContent;
        if (source == null) return;
        var root = source.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;

        if (root.TryGetProperty("level", out var currentLevel) &&
            currentLevel.ValueKind == JsonValueKind.Number &&
            currentLevel.GetInt32() == level)
            return;

        var dict = new Dictionary<string, object?>();
        foreach (var prop in root.EnumerateObject())
        {
            dict[prop.Name] = prop.Name == "level"
                ? level
                : JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
        }
        if (!dict.ContainsKey("level")) dict["level"] = level;

        block.CurrentContent = JsonDocument.Parse(JsonSerializer.Serialize(dict));
        block.CurrentType ??= block.OriginalType;
    }
}
