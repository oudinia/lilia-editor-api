using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Models.AiImport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

public class AiImportService : IAiImportService
{
    private readonly IChatClient _chatClient;
    private readonly AiOptions _options;
    private readonly ILogger<AiImportService> _logger;
    private readonly bool _useAi;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> ValidBlockTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "paragraph", "heading", "equation", "figure", "code", "list",
        "blockquote", "table", "theorem", "abstract", "bibliography",
        "tableOfContents", "pageBreak"
    };

    public AiImportService(
        IChatClient chatClient,
        IOptions<AiOptions> options,
        ILogger<AiImportService> logger)
    {
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;

        // Only use AI when a real API key is configured
        _useAi = !string.IsNullOrEmpty(_options.Anthropic.ApiKey)
                 && _options.Anthropic.ApiKey != "sk-placeholder";
    }

    private string GetModel()
    {
        return _options.Models.TryGetValue("import-classify", out var model)
            ? model
            : _options.DefaultModel;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    public async Task<BlockClassification> ClassifyBlockAsync(string content, string currentType)
    {
        if (_useAi)
        {
            try
            {
                return await ClassifyBlockWithAiAsync(content, currentType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiImport] AI classification failed, falling back to heuristics");
            }
        }

        return ClassifyBlockWithHeuristics(content, currentType);
    }

    public async Task<List<QualitySuggestion>> SuggestImprovementsAsync(string content, string blockType)
    {
        if (_useAi)
        {
            try
            {
                return await SuggestImprovementsWithAiAsync(content, blockType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiImport] AI suggestions failed, falling back to heuristics");
            }
        }

        return SuggestImprovementsWithHeuristics(content, blockType);
    }

    public async Task<string> FixFormattingAsync(string content, string blockType)
    {
        if (_useAi)
        {
            try
            {
                return await FixFormattingWithAiAsync(content, blockType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiImport] AI formatting fix failed, falling back to heuristics");
            }
        }

        return FixFormattingWithHeuristics(content, blockType);
    }

    public async Task<List<BlockClassification>> ClassifyBatchAsync(
        List<(string content, string currentType)> blocks)
    {
        if (_useAi)
        {
            try
            {
                return await ClassifyBatchWithAiAsync(blocks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiImport] AI batch classification failed, falling back to heuristics");
            }
        }

        return blocks
            .Select(b => ClassifyBlockWithHeuristics(b.content, b.currentType))
            .ToList();
    }

    // -----------------------------------------------------------------------
    // AI-powered implementations
    // -----------------------------------------------------------------------

    private async Task<BlockClassification> ClassifyBlockWithAiAsync(string content, string currentType)
    {
        var model = GetModel();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiImportPrompts.ClassifyBlock),
            new(ChatRole.User, $"Current type: {currentType}\n\nContent:\n{Truncate(content, 2000)}"),
        };

        _logger.LogInformation("[AiImport] Classifying block (current: {CurrentType}) with model {Model}", currentType, model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 512 });
        var raw = StripMarkdownFences(response.Text);
        var result = JsonSerializer.Deserialize<AiClassifyResponse>(raw, JsonOptions);

        var suggestedType = ValidBlockTypes.Contains(result?.SuggestedType ?? "")
            ? result!.SuggestedType
            : currentType;

        return new BlockClassification(
            null,
            currentType,
            suggestedType,
            Math.Clamp(result?.Confidence ?? 0.5, 0, 1),
            result?.Reasoning ?? "AI classification"
        );
    }

    private async Task<List<BlockClassification>> ClassifyBatchWithAiAsync(
        List<(string content, string currentType)> blocks)
    {
        var model = GetModel();

        // Build a numbered list of blocks for the prompt
        var blockDescriptions = blocks.Select((b, i) =>
            $"[{i}] type={b.currentType}\n{Truncate(b.content, 500)}"
        );
        var batchContent = string.Join("\n---\n", blockDescriptions);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiImportPrompts.ClassifyBatch),
            new(ChatRole.User, batchContent),
        };

        _logger.LogInformation("[AiImport] Batch classifying {Count} blocks with model {Model}", blocks.Count, model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 4096 });
        var raw = StripMarkdownFences(response.Text);
        var results = JsonSerializer.Deserialize<List<AiClassifyResponse>>(raw, JsonOptions) ?? [];

        var classifications = new List<BlockClassification>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var (content, currentType) = blocks[i];
            if (i < results.Count)
            {
                var r = results[i];
                var suggestedType = ValidBlockTypes.Contains(r.SuggestedType) ? r.SuggestedType : currentType;
                classifications.Add(new BlockClassification(null, currentType, suggestedType,
                    Math.Clamp(r.Confidence, 0, 1), r.Reasoning ?? "AI classification"));
            }
            else
            {
                // AI returned fewer results than blocks — fall back to heuristics for the rest
                classifications.Add(ClassifyBlockWithHeuristics(content, currentType));
            }
        }

        return classifications;
    }

    private async Task<List<QualitySuggestion>> SuggestImprovementsWithAiAsync(string content, string blockType)
    {
        var model = GetModel();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiImportPrompts.SuggestImprovements),
            new(ChatRole.User, $"Block type: {blockType}\n\nContent:\n{Truncate(content, 3000)}"),
        };

        _logger.LogInformation("[AiImport] Suggesting improvements for {BlockType} with model {Model}", blockType, model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 2048 });
        var raw = StripMarkdownFences(response.Text);
        var results = JsonSerializer.Deserialize<List<AiSuggestionResponse>>(raw, JsonOptions) ?? [];

        return results.Select(r => new QualitySuggestion(
            NormalizeCategory(r.Category),
            r.Description ?? "",
            r.SuggestedFix,
            NormalizeSeverity(r.Severity)
        )).ToList();
    }

    private async Task<string> FixFormattingWithAiAsync(string content, string blockType)
    {
        var model = GetModel();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiImportPrompts.FixFormatting),
            new(ChatRole.User, $"Block type: {blockType}\n\nContent:\n{content}"),
        };

        _logger.LogInformation("[AiImport] Fixing formatting for {BlockType} with model {Model}", blockType, model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 4096 });
        var raw = StripMarkdownFences(response.Text);

        // The AI should return just the fixed content as a JSON string
        try
        {
            var result = JsonSerializer.Deserialize<AiFixResponse>(raw, JsonOptions);
            return result?.FixedContent ?? content;
        }
        catch
        {
            // If it returned plain text instead of JSON, use it directly
            return raw;
        }
    }

    // -----------------------------------------------------------------------
    // Heuristic/mock implementations (no API key required)
    // -----------------------------------------------------------------------

    private static BlockClassification ClassifyBlockWithHeuristics(string content, string currentType)
    {
        var detectedType = DetectBlockType(content);

        if (detectedType != null && !string.Equals(detectedType, currentType, StringComparison.OrdinalIgnoreCase))
        {
            return new BlockClassification(
                null, currentType, detectedType, 0.75,
                $"Heuristic: content appears to be a {detectedType} block"
            );
        }

        return new BlockClassification(
            null, currentType, currentType, 0.9,
            "Heuristic: current type appears correct"
        );
    }

    private static string? DetectBlockType(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var trimmed = content.Trim();

        // Equation detection
        if (ContainsEquationMarkers(trimmed))
            return "equation";

        // Code detection
        if (ContainsCodeMarkers(trimmed))
            return "code";

        // Table detection (multiple lines with | delimiters)
        if (ContainsTableMarkers(trimmed))
            return "table";

        // List detection
        if (ContainsListMarkers(trimmed))
            return "list";

        // Heading detection (short text, numbered, or ALL-CAPS)
        if (LooksLikeHeading(trimmed))
            return "heading";

        // Blockquote detection
        if (trimmed.StartsWith('>') || trimmed.StartsWith('\u201c') /* left double quote */)
            return "blockquote";

        return null;
    }

    private static bool ContainsEquationMarkers(string text)
    {
        return text.Contains("\\begin{equation}", StringComparison.Ordinal)
               || text.Contains("\\begin{align", StringComparison.Ordinal)
               || text.Contains("$$", StringComparison.Ordinal)
               || text.Contains("\\frac{", StringComparison.Ordinal)
               || text.Contains("\\int", StringComparison.Ordinal)
               || text.Contains("\\sum", StringComparison.Ordinal)
               || text.Contains("\\lim", StringComparison.Ordinal)
               || text.Contains("\\partial", StringComparison.Ordinal)
               || text.Contains("\\nabla", StringComparison.Ordinal)
               || Regex.IsMatch(text, @"\\\w+\{.*\}.*=");
    }

    private static bool ContainsCodeMarkers(string text)
    {
        return text.Contains("```", StringComparison.Ordinal)
               || text.Contains("def ", StringComparison.Ordinal)
               || text.Contains("function ", StringComparison.Ordinal)
               || text.Contains("class ", StringComparison.Ordinal)
               || text.Contains("import ", StringComparison.Ordinal)
               || text.Contains("public static", StringComparison.Ordinal)
               || text.Contains("Console.Write", StringComparison.Ordinal)
               || text.Contains("System.out", StringComparison.Ordinal)
               || Regex.IsMatch(text, @"^\s*(if|for|while|switch)\s*\(", RegexOptions.Multiline);
    }

    private static bool ContainsTableMarkers(string text)
    {
        var lines = text.Split('\n');
        var pipeLines = lines.Count(l => l.Contains('|') && l.Trim().Length > 1);
        return pipeLines >= 2;
    }

    private static bool ContainsListMarkers(string text)
    {
        var lines = text.Split('\n');
        var listLines = lines.Count(l =>
        {
            var t = l.TrimStart();
            return t.StartsWith("- ", StringComparison.Ordinal)
                   || t.StartsWith("* ", StringComparison.Ordinal)
                   || Regex.IsMatch(t, @"^\d+[\.\)]\s");
        });
        return listLines >= 2;
    }

    private static bool LooksLikeHeading(string text)
    {
        // Remove leading markdown heading markers
        var clean = text.TrimStart('#').Trim();

        if (string.IsNullOrEmpty(clean))
            return false;

        // Short text that looks like a title
        var isSingleLine = !clean.Contains('\n');
        var isShort = clean.Length <= 120;

        if (!isSingleLine || !isShort)
            return false;

        // Starts with a number (e.g. "1. Introduction", "2.3 Methods")
        if (Regex.IsMatch(clean, @"^\d+[\.\)]\s"))
            return true;

        // ALL-CAPS text (at least 3 chars)
        if (clean.Length >= 3 && clean == clean.ToUpperInvariant() && clean.Any(char.IsLetter))
            return true;

        // Starts with markdown heading marker
        if (text.TrimStart().StartsWith('#'))
            return true;

        return false;
    }

    private static List<QualitySuggestion> SuggestImprovementsWithHeuristics(string content, string blockType)
    {
        var suggestions = new List<QualitySuggestion>();

        if (string.IsNullOrWhiteSpace(content))
        {
            suggestions.Add(new QualitySuggestion(
                QualitySuggestion.Categories.Content,
                "Block has empty content",
                "Consider removing this empty block or adding content",
                QualitySuggestion.Severities.Warning
            ));
            return suggestions;
        }

        // Check for mismatched type
        var detected = DetectBlockType(content);
        if (detected != null && !string.Equals(detected, blockType, StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add(new QualitySuggestion(
                QualitySuggestion.Categories.Structure,
                $"Content appears to be a {detected} block but is typed as {blockType}",
                $"Consider changing block type to {detected}",
                QualitySuggestion.Severities.Warning
            ));
        }

        // Formatting checks
        if (content.Contains("  ", StringComparison.Ordinal))
        {
            suggestions.Add(new QualitySuggestion(
                QualitySuggestion.Categories.Formatting,
                "Content contains multiple consecutive spaces",
                "Replace multiple spaces with single spaces",
                QualitySuggestion.Severities.Info
            ));
        }

        if (content.TrimEnd() != content && content.EndsWith("  ", StringComparison.Ordinal))
        {
            suggestions.Add(new QualitySuggestion(
                QualitySuggestion.Categories.Formatting,
                "Content has trailing whitespace",
                "Remove trailing whitespace",
                QualitySuggestion.Severities.Info
            ));
        }

        // Math-specific checks
        if (blockType == "equation" || content.Contains('$'))
        {
            var dollarCount = content.Count(c => c == '$');
            if (dollarCount % 2 != 0)
            {
                suggestions.Add(new QualitySuggestion(
                    QualitySuggestion.Categories.Math,
                    "Unbalanced dollar signs in math expression",
                    "Ensure all math delimiters are properly paired",
                    QualitySuggestion.Severities.Error
                ));
            }

            if (content.Contains("\\begin{", StringComparison.Ordinal))
            {
                var beginCount = Regex.Matches(content, @"\\begin\{").Count;
                var endCount = Regex.Matches(content, @"\\end\{").Count;
                if (beginCount != endCount)
                {
                    suggestions.Add(new QualitySuggestion(
                        QualitySuggestion.Categories.Math,
                        $"Mismatched LaTeX environments: {beginCount} \\begin vs {endCount} \\end",
                        "Ensure all LaTeX environments are properly opened and closed",
                        QualitySuggestion.Severities.Error
                    ));
                }
            }
        }

        // Content checks for paragraphs
        if (blockType == "paragraph")
        {
            if (content.Length < 10)
            {
                suggestions.Add(new QualitySuggestion(
                    QualitySuggestion.Categories.Content,
                    "Very short paragraph (less than 10 characters)",
                    "Consider merging with adjacent paragraph or converting to a different block type",
                    QualitySuggestion.Severities.Info
                ));
            }
        }

        return suggestions;
    }

    private static string FixFormattingWithHeuristics(string content, string blockType)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        var result = content;

        // Normalize line endings
        result = result.Replace("\r\n", "\n");

        // Collapse multiple spaces into one (but not in code blocks)
        if (blockType != "code")
        {
            result = Regex.Replace(result, @"[ \t]{2,}", " ");
        }

        // Trim trailing whitespace from each line
        result = string.Join('\n', result.Split('\n').Select(l => l.TrimEnd()));

        // Remove leading/trailing blank lines
        result = result.Trim('\n', '\r');

        // Fix common LaTeX issues in equation blocks
        if (blockType == "equation")
        {
            // Normalize display math delimiters
            result = result.Replace("\\[", "$$").Replace("\\]", "$$");
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string StripMarkdownFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];
        }
        if (text.EndsWith("```"))
            text = text[..^3];
        return text.Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private static string NormalizeCategory(string? category)
    {
        return category?.ToLowerInvariant() switch
        {
            "formatting" => QualitySuggestion.Categories.Formatting,
            "structure" => QualitySuggestion.Categories.Structure,
            "content" => QualitySuggestion.Categories.Content,
            "math" => QualitySuggestion.Categories.Math,
            _ => QualitySuggestion.Categories.Content,
        };
    }

    private static string NormalizeSeverity(string? severity)
    {
        return severity?.ToLowerInvariant() switch
        {
            "info" => QualitySuggestion.Severities.Info,
            "warning" => QualitySuggestion.Severities.Warning,
            "error" => QualitySuggestion.Severities.Error,
            _ => QualitySuggestion.Severities.Info,
        };
    }

    // -----------------------------------------------------------------------
    // AI response DTOs (internal)
    // -----------------------------------------------------------------------

    private record AiClassifyResponse(
        string SuggestedType,
        double Confidence,
        string? Reasoning
    );

    private record AiSuggestionResponse(
        string? Category,
        string? Description,
        string? SuggestedFix,
        string? Severity
    );

    private record AiFixResponse(
        string FixedContent,
        List<string>? ChangesApplied
    );
}

// -----------------------------------------------------------------------
// Prompt templates
// -----------------------------------------------------------------------

internal static class AiImportPrompts
{
    public const string ClassifyBlock = """
        You are an expert document block classifier for an academic writing tool.
        Given a block of content and its current type, determine the most accurate block type.

        Valid block types: paragraph, heading, equation, figure, code, list, blockquote, table, theorem, abstract, bibliography, tableOfContents, pageBreak

        Guidelines:
        - "equation": Contains LaTeX math ($$, \frac, \begin{equation}, \int, \sum, etc.)
        - "code": Contains programming code (function definitions, imports, class declarations, etc.)
        - "heading": Short single-line text, often numbered or ALL-CAPS, used as section titles
        - "list": Multiple lines starting with -, *, or numbered items
        - "table": Contains pipe-delimited rows or structured tabular data
        - "theorem": Mathematical theorems, lemmas, propositions, corollaries, or proofs
        - "abstract": Document abstract or summary section
        - "bibliography": Reference entries, citations
        - "blockquote": Quoted text from other sources
        - "figure": Image references or figure captions
        - "paragraph": Regular prose text (default)

        Respond with ONLY a JSON object: {"suggestedType": "...", "confidence": 0.0-1.0, "reasoning": "..."}
        """;

    public const string ClassifyBatch = """
        You are an expert document block classifier for an academic writing tool.
        Given multiple blocks separated by ---, classify each one.

        Valid block types: paragraph, heading, equation, figure, code, list, blockquote, table, theorem, abstract, bibliography, tableOfContents, pageBreak

        Each block is prefixed with [index] and its current type.
        Respond with ONLY a JSON array of objects: [{"suggestedType": "...", "confidence": 0.0-1.0, "reasoning": "..."}, ...]
        One entry per block, in the same order.
        """;

    public const string SuggestImprovements = """
        You are a document quality reviewer for an academic writing tool.
        Analyze the given block content and suggest improvements.

        Categories: formatting, structure, content, math
        Severities: info (minor style), warning (should fix), error (must fix)

        Focus on:
        - Formatting: whitespace, indentation, line breaks, special characters
        - Structure: block type correctness, logical organization
        - Content: clarity, completeness, obvious errors
        - Math: LaTeX syntax, balanced delimiters, correct notation

        Respond with ONLY a JSON array: [{"category": "...", "description": "...", "suggestedFix": "...", "severity": "..."}, ...]
        Return an empty array [] if no issues are found.
        """;

    public const string FixFormatting = """
        You are a document formatting fixer for an academic writing tool.
        Fix formatting issues in the given block content while preserving its meaning.

        Fixes to apply:
        - Normalize whitespace (collapse multiple spaces, trim trailing)
        - Fix LaTeX syntax (balanced delimiters, proper commands)
        - Normalize line endings
        - Fix common import artifacts (broken characters, encoding issues)
        - Do NOT change the meaning or structure of the content

        Respond with ONLY a JSON object: {"fixedContent": "...", "changesApplied": ["description of change 1", ...]}
        If no changes are needed, return the original content unchanged.
        """;
}
