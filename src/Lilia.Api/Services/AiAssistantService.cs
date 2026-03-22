using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Models.AiAssistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

public class AiAssistantService : IAiAssistantService
{
    private readonly IChatClient _chatClient;
    private readonly AiOptions _options;
    private readonly ILogger<AiAssistantService> _logger;
    private readonly bool _useAi;
    private readonly int _rateLimitPerHour;

    // In-memory rate limiting: userId -> list of request timestamps
    private static readonly ConcurrentDictionary<string, List<DateTime>> RateLimitTracker = new();
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly object CleanupLock = new();

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

    private static readonly HashSet<string> ValidActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "improve", "paraphrase", "expand", "shorten", "formalize"
    };

    // Common math patterns: description keyword(s) -> LaTeX template
    private static readonly List<(Regex Pattern, string Template, string Explanation)> MathPatterns =
    [
        (new Regex(@"fraction\s+(?:of\s+)?(\w+)\s+over\s+(\w+)", RegexOptions.IgnoreCase),
            @"\frac{{{0}}}{{{1}}}", "Fraction"),
        (new Regex(@"integral\s+(?:of\s+)?(.+?)\s+from\s+(\w+)\s+to\s+(\w+)", RegexOptions.IgnoreCase),
            @"\int_{{{1}}}^{{{2}}} {0} \, dx", "Definite integral"),
        (new Regex(@"integral\s+(?:of\s+)?(.+)", RegexOptions.IgnoreCase),
            @"\int {0} \, dx", "Indefinite integral"),
        (new Regex(@"sum\s+(?:of\s+)?(.+?)\s+from\s+(\w+)\s*=\s*(\w+)\s+to\s+(\w+)", RegexOptions.IgnoreCase),
            @"\sum_{{{1}={2}}}^{{{3}}} {0}", "Summation"),
        (new Regex(@"sum\s+(?:of\s+)?(.+)", RegexOptions.IgnoreCase),
            @"\sum {0}", "Summation"),
        (new Regex(@"limit\s+(?:of\s+)?(.+?)\s+as\s+(\w+)\s+(?:approaches|goes to|->|to)\s+(\w+)", RegexOptions.IgnoreCase),
            @"\lim_{{{1} \to {2}}} {0}", "Limit"),
        (new Regex(@"square\s*root\s+(?:of\s+)?(.+)", RegexOptions.IgnoreCase),
            @"\sqrt{{{0}}}", "Square root"),
        (new Regex(@"(\w+)\s+squared", RegexOptions.IgnoreCase),
            @"{0}^{{2}}", "Squared"),
        (new Regex(@"(\w+)\s+cubed", RegexOptions.IgnoreCase),
            @"{0}^{{3}}", "Cubed"),
        (new Regex(@"matrix\s+(\d+)\s*(?:x|by)\s*(\d+)", RegexOptions.IgnoreCase),
            null!, "Matrix"), // Handled specially
        (new Regex(@"(\w+)\s+(?:sub|subscript)\s+(\w+)", RegexOptions.IgnoreCase),
            @"{0}_{{{1}}}", "Subscript"),
        (new Regex(@"(\w+)\s+(?:sup|superscript|to the power of|to the)\s+(\w+)", RegexOptions.IgnoreCase),
            @"{0}^{{{1}}}", "Superscript"),
        (new Regex(@"partial\s+(?:derivative\s+(?:of\s+)?)?(\w+)\s+(?:with respect to|wrt)\s+(\w+)", RegexOptions.IgnoreCase),
            @"\frac{{\partial {0}}}{{\partial {1}}}", "Partial derivative"),
        (new Regex(@"infinity", RegexOptions.IgnoreCase),
            @"\infty", "Infinity symbol"),
        (new Regex(@"alpha", RegexOptions.IgnoreCase), @"\alpha", "Greek letter alpha"),
        (new Regex(@"beta", RegexOptions.IgnoreCase), @"\beta", "Greek letter beta"),
        (new Regex(@"gamma", RegexOptions.IgnoreCase), @"\gamma", "Greek letter gamma"),
        (new Regex(@"delta", RegexOptions.IgnoreCase), @"\delta", "Greek letter delta"),
        (new Regex(@"theta", RegexOptions.IgnoreCase), @"\theta", "Greek letter theta"),
        (new Regex(@"pi\b", RegexOptions.IgnoreCase), @"\pi", "Greek letter pi"),
    ];

    // Common LaTeX fix patterns: broken -> fixed
    private static readonly List<(Regex Pattern, string Replacement, string Description)> LatexFixes =
    [
        // Missing closing brace after \frac{...}{
        (new Regex(@"\\frac\{([^}]*)\}\{([^}]*)$"), @"\frac{$1}{$2}", "Added missing closing brace in \\frac"),
        // Missing closing brace in general commands
        (new Regex(@"(\\(?:sqrt|text|mathbf|mathit|mathrm|hat|bar|vec|tilde))\{([^}]*)$"), "$1{$2}", "Added missing closing brace"),
        // \fracab -> \frac{a}{b} (missing braces)
        (new Regex(@"\\frac([a-zA-Z0-9])([a-zA-Z0-9])"), @"\frac{$1}{$2}", "Added braces to \\frac arguments"),
        // \sqrtx -> \sqrt{x}
        (new Regex(@"\\sqrt([a-zA-Z0-9])(?!\{)"), @"\sqrt{$1}", "Added braces to \\sqrt argument"),
        // Double backslash typos: \\\\frac -> \frac
        (new Regex(@"\\\\(frac|int|sum|lim|sqrt|begin|end|left|right|partial|nabla)"), @"\$1", "Fixed double backslash"),
        // \left( without matching \right)
        (new Regex(@"\\left\(([^)]*?)(?<!\\right)\)"), @"\left($1\right)", "Added \\right to match \\left"),
        // Unescaped % in math mode
        (new Regex(@"(?<!\\)%(?!.*$)", RegexOptions.Multiline), @"\%", "Escaped percent sign"),
        // Common typo: \itegral -> \integral (not a real command, but \int)
        (new Regex(@"\\integral"), @"\int", "Replaced \\integral with \\int"),
        // \summation -> \sum
        (new Regex(@"\\summation"), @"\sum", "Replaced \\summation with \\sum"),
    ];

    public AiAssistantService(
        IChatClient chatClient,
        IOptions<AiOptions> options,
        ILogger<AiAssistantService> logger,
        IConfiguration configuration)
    {
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;

        _useAi = !string.IsNullOrEmpty(_options.Anthropic.ApiKey)
                 && _options.Anthropic.ApiKey != "sk-placeholder";

        _rateLimitPerHour = configuration.GetValue("AiSettings:RateLimitPerHour", 100);
    }

    // -----------------------------------------------------------------------
    // Rate Limiting
    // -----------------------------------------------------------------------

    /// <summary>
    /// Check and record a request for rate limiting. Returns true if the request is allowed.
    /// </summary>
    internal bool CheckRateLimit(string userId)
    {
        CleanupStaleEntries();

        var now = DateTime.UtcNow;
        var oneHourAgo = now.AddHours(-1);

        var timestamps = RateLimitTracker.GetOrAdd(userId, _ => new List<DateTime>());
        lock (timestamps)
        {
            // Remove entries older than one hour
            timestamps.RemoveAll(t => t < oneHourAgo);

            if (timestamps.Count >= _rateLimitPerHour)
                return false;

            timestamps.Add(now);
            return true;
        }
    }

    private static void CleanupStaleEntries()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCleanup).TotalMinutes < 10)
            return;

        lock (CleanupLock)
        {
            if ((now - _lastCleanup).TotalMinutes < 10)
                return;

            _lastCleanup = now;
            var oneHourAgo = now.AddHours(-1);

            foreach (var kvp in RateLimitTracker)
            {
                lock (kvp.Value)
                {
                    kvp.Value.RemoveAll(t => t < oneHourAgo);
                    if (kvp.Value.Count == 0)
                        RateLimitTracker.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    /// <summary>
    /// Clear all rate limit tracking data. Used for testing.
    /// </summary>
    internal static void ResetRateLimits()
    {
        RateLimitTracker.Clear();
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    public async Task<MathGenerationResult> GenerateMathAsync(string description, string? context)
    {
        if (_useAi)
        {
            try
            {
                return await GenerateMathWithAiAsync(description, context);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiAssistant] AI math generation failed, falling back to heuristics");
            }
        }

        return GenerateMathWithHeuristics(description);
    }

    public async Task<MathFixResult> FixMathAsync(string brokenLatex, string? errorMessage)
    {
        if (_useAi)
        {
            try
            {
                return await FixMathWithAiAsync(brokenLatex, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiAssistant] AI math fix failed, falling back to heuristics");
            }
        }

        return FixMathWithHeuristics(brokenLatex);
    }

    public async Task<WritingResult> ImproveWritingAsync(string text, string action, string? style)
    {
        if (!ValidActions.Contains(action))
            action = "improve";

        if (_useAi)
        {
            try
            {
                return await ImproveWritingWithAiAsync(text, action, style);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiAssistant] AI writing improvement failed, falling back to heuristics");
            }
        }

        return ImproveWritingWithHeuristics(text, action);
    }

    public async Task<BlockClassificationResult> ClassifyBlockAsync(string content)
    {
        if (_useAi)
        {
            try
            {
                return await ClassifyBlockWithAiAsync(content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AiAssistant] AI classification failed, falling back to heuristics");
            }
        }

        return ClassifyBlockWithHeuristics(content);
    }

    // -----------------------------------------------------------------------
    // AI-powered implementations
    // -----------------------------------------------------------------------

    private string GetModel()
    {
        return _options.Models.TryGetValue("assistant", out var model)
            ? model
            : _options.DefaultModel;
    }

    private async Task<MathGenerationResult> GenerateMathWithAiAsync(string description, string? context)
    {
        var model = GetModel();
        var userMessage = string.IsNullOrEmpty(context)
            ? description
            : $"Context: {context}\n\nGenerate LaTeX for: {description}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiAssistantPrompts.GenerateMath),
            new(ChatRole.User, userMessage),
        };

        _logger.LogInformation("[AiAssistant] Generating math with model {Model}", model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 1024 });
        var raw = StripMarkdownFences(response.Text);
        var result = JsonSerializer.Deserialize<AiMathGenerateResponse>(raw, JsonOptions);

        return new MathGenerationResult(
            result?.Latex ?? description,
            result?.Explanation ?? "AI-generated expression",
            Math.Clamp(result?.Confidence ?? 0.5, 0, 1)
        );
    }

    private async Task<MathFixResult> FixMathWithAiAsync(string brokenLatex, string? errorMessage)
    {
        var model = GetModel();
        var userMessage = string.IsNullOrEmpty(errorMessage)
            ? $"Fix this LaTeX:\n{brokenLatex}"
            : $"Fix this LaTeX:\n{brokenLatex}\n\nError message: {errorMessage}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiAssistantPrompts.FixMath),
            new(ChatRole.User, userMessage),
        };

        _logger.LogInformation("[AiAssistant] Fixing math with model {Model}", model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 1024 });
        var raw = StripMarkdownFences(response.Text);
        var result = JsonSerializer.Deserialize<AiMathFixResponse>(raw, JsonOptions);

        return new MathFixResult(
            result?.FixedLatex ?? brokenLatex,
            result?.Changes ?? [],
            Math.Clamp(result?.Confidence ?? 0.5, 0, 1)
        );
    }

    private async Task<WritingResult> ImproveWritingWithAiAsync(string text, string action, string? style)
    {
        var model = GetModel();
        var userMessage = $"Action: {action}\nStyle: {style ?? "academic"}\n\nText:\n{text}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiAssistantPrompts.ImproveWriting),
            new(ChatRole.User, userMessage),
        };

        _logger.LogInformation("[AiAssistant] Improving writing ({Action}) with model {Model}", action, model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 4096 });
        var raw = StripMarkdownFences(response.Text);
        var result = JsonSerializer.Deserialize<AiWritingResponse>(raw, JsonOptions);

        return new WritingResult(
            result?.ImprovedText ?? text,
            result?.Changes ?? [],
            action
        );
    }

    private async Task<BlockClassificationResult> ClassifyBlockWithAiAsync(string content)
    {
        var model = GetModel();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AiAssistantPrompts.ClassifyBlock),
            new(ChatRole.User, Truncate(content, 2000)),
        };

        _logger.LogInformation("[AiAssistant] Classifying block with model {Model}", model);
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 512 });
        var raw = StripMarkdownFences(response.Text);
        var result = JsonSerializer.Deserialize<AiClassifyResponse>(raw, JsonOptions);

        var suggestedType = ValidBlockTypes.Contains(result?.SuggestedType ?? "")
            ? result!.SuggestedType
            : "paragraph";

        return new BlockClassificationResult(
            suggestedType,
            Math.Clamp(result?.Confidence ?? 0.5, 0, 1),
            result?.Reasoning ?? "AI classification"
        );
    }

    // -----------------------------------------------------------------------
    // Heuristic implementations (no API key required)
    // -----------------------------------------------------------------------

    internal static MathGenerationResult GenerateMathWithHeuristics(string description)
    {
        var trimmed = description.Trim();

        // Try each pattern
        foreach (var (pattern, template, explanation) in MathPatterns)
        {
            var match = pattern.Match(trimmed);
            if (!match.Success)
                continue;

            // Special case: matrix generation
            if (explanation == "Matrix")
            {
                var rows = int.Parse(match.Groups[1].Value);
                var cols = int.Parse(match.Groups[2].Value);
                rows = Math.Clamp(rows, 1, 10);
                cols = Math.Clamp(cols, 1, 10);
                var matrixBody = string.Join(" \\\\\n",
                    Enumerable.Range(0, rows).Select(r =>
                        string.Join(" & ", Enumerable.Range(0, cols).Select(c => $"a_{{{r + 1}{c + 1}}}"))));
                var latex = $"\\begin{{pmatrix}}\n{matrixBody}\n\\end{{pmatrix}}";
                return new MathGenerationResult(latex, $"{rows}x{cols} matrix", 0.85);
            }

            // For patterns with no template (should not happen after matrix), skip
            if (template == null!)
                continue;

            // Build the result using captured groups
            var groups = match.Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToArray();
            var generatedLatex = string.Format(template, groups);
            return new MathGenerationResult(generatedLatex, explanation, 0.8);
        }

        // Fallback: wrap the description as-is (it might already be partial LaTeX)
        return new MathGenerationResult(
            trimmed,
            "Could not parse description; returned as-is",
            0.3
        );
    }

    internal static MathFixResult FixMathWithHeuristics(string brokenLatex)
    {
        var result = brokenLatex;
        var changes = new List<string>();

        // Apply each fix pattern
        foreach (var (pattern, replacement, description) in LatexFixes)
        {
            var newResult = pattern.Replace(result, replacement);
            if (newResult != result)
            {
                changes.Add(description);
                result = newResult;
            }
        }

        // Fix mismatched \begin{X}...\end{Y} environment names
        var envPattern = new Regex(@"\\begin\s*\{([^}]*)\}([\s\S]*?)\\end\s*\{([^}]*)\}");
        var envFixed = envPattern.Replace(result, m =>
            m.Groups[1].Value != m.Groups[3].Value
                ? $"\\begin{{{m.Groups[1].Value}}}{m.Groups[2].Value}\\end{{{m.Groups[1].Value}}}"
                : m.Value);
        if (envFixed != result)
        {
            changes.Add("Fixed mismatched environment names");
            result = envFixed;
        }

        // Fix unbalanced braces
        var openCount = result.Count(c => c == '{');
        var closeCount = result.Count(c => c == '}');
        if (openCount > closeCount)
        {
            result += new string('}', openCount - closeCount);
            changes.Add($"Added {openCount - closeCount} missing closing brace(s)");
        }
        else if (closeCount > openCount)
        {
            // Remove trailing extra closing braces
            var excess = closeCount - openCount;
            for (var i = 0; i < excess; i++)
            {
                var lastBrace = result.LastIndexOf('}');
                if (lastBrace >= 0)
                    result = result.Remove(lastBrace, 1);
            }
            changes.Add($"Removed {excess} extra closing brace(s)");
        }

        // Fix unbalanced dollar signs
        var dollarCount = result.Count(c => c == '$');
        if (dollarCount % 2 != 0)
        {
            result += "$";
            changes.Add("Added missing closing dollar sign");
        }

        var confidence = changes.Count > 0 ? 0.7 : 0.9;
        return new MathFixResult(result, changes, confidence);
    }

    internal static WritingResult ImproveWritingWithHeuristics(string text, string action)
    {
        var result = text;
        var changes = new List<string>();

        switch (action.ToLowerInvariant())
        {
            case "improve":
            case "formalize":
                // Basic capitalization fix
                if (result.Length > 0 && char.IsLower(result[0]))
                {
                    result = char.ToUpper(result[0]) + result[1..];
                    changes.Add("Capitalized first letter");
                }

                // Collapse multiple spaces
                var collapsed = Regex.Replace(result, @"[ \t]{2,}", " ");
                if (collapsed != result)
                {
                    result = collapsed;
                    changes.Add("Collapsed multiple spaces");
                }

                // Ensure ends with period (for formalize)
                if (action == "formalize" && result.Length > 0 && !result.EndsWith('.') && !result.EndsWith('?') && !result.EndsWith('!'))
                {
                    result += ".";
                    changes.Add("Added ending period");
                }

                // Trim trailing whitespace
                var trimmed = result.TrimEnd();
                if (trimmed != result)
                {
                    result = trimmed;
                    changes.Add("Removed trailing whitespace");
                }
                break;

            case "paraphrase":
                // Heuristic paraphrase: no real rewriting without AI, just clean up
                result = Regex.Replace(result, @"[ \t]{2,}", " ").Trim();
                if (result != text)
                    changes.Add("Cleaned up whitespace");
                break;

            case "expand":
                // Cannot meaningfully expand without AI; return as-is with a note
                changes.Add("Expansion requires AI mode; text returned with basic cleanup");
                result = Regex.Replace(result, @"[ \t]{2,}", " ").Trim();
                break;

            case "shorten":
                // Basic shortening: remove filler phrases
                var fillers = new[]
                {
                    "it is important to note that ",
                    "it should be noted that ",
                    "it is worth mentioning that ",
                    "as a matter of fact, ",
                    "in order to ",
                    "due to the fact that ",
                    "for the purpose of ",
                    "in the event that ",
                };
                foreach (var filler in fillers)
                {
                    var idx = result.IndexOf(filler, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        result = result.Remove(idx, filler.Length);
                        // Capitalize if at start of sentence
                        if (idx == 0 && result.Length > 0)
                            result = char.ToUpper(result[0]) + result[1..];
                        changes.Add($"Removed filler phrase: \"{filler.Trim()}\"");
                    }
                }
                break;
        }

        if (changes.Count == 0)
            changes.Add("No heuristic improvements available; consider enabling AI mode");

        return new WritingResult(result, changes, action);
    }

    internal static BlockClassificationResult ClassifyBlockWithHeuristics(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new BlockClassificationResult("paragraph", 0.5, "Empty content defaults to paragraph");

        var trimmed = content.Trim();

        // Equation detection
        if (ContainsEquationMarkers(trimmed))
            return new BlockClassificationResult("equation", 0.85, "Content contains LaTeX math markers");

        // Code detection
        if (ContainsCodeMarkers(trimmed))
            return new BlockClassificationResult("code", 0.8, "Content contains programming constructs");

        // Table detection
        if (ContainsTableMarkers(trimmed))
            return new BlockClassificationResult("table", 0.8, "Content contains tabular data with pipe delimiters");

        // List detection
        if (ContainsListMarkers(trimmed))
            return new BlockClassificationResult("list", 0.8, "Content contains list item markers");

        // Heading detection
        if (LooksLikeHeading(trimmed))
            return new BlockClassificationResult("heading", 0.75, "Content appears to be a section heading");

        // Blockquote detection
        if (trimmed.StartsWith('>') || trimmed.StartsWith('\u201c'))
            return new BlockClassificationResult("blockquote", 0.7, "Content appears to be a quotation");

        return new BlockClassificationResult("paragraph", 0.9, "Content appears to be regular prose text");
    }

    // -----------------------------------------------------------------------
    // Detection helpers (reused from AiImportService patterns)
    // -----------------------------------------------------------------------

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
        var clean = text.TrimStart('#').Trim();
        if (string.IsNullOrEmpty(clean)) return false;

        var isSingleLine = !clean.Contains('\n');
        var isShort = clean.Length <= 120;
        if (!isSingleLine || !isShort) return false;

        if (Regex.IsMatch(clean, @"^\d+[\.\)]\s")) return true;
        if (clean.Length >= 3 && clean == clean.ToUpperInvariant() && clean.Any(char.IsLetter)) return true;
        if (text.TrimStart().StartsWith('#')) return true;

        return false;
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

    // -----------------------------------------------------------------------
    // AI response DTOs (internal)
    // -----------------------------------------------------------------------

    private record AiMathGenerateResponse(string Latex, string? Explanation, double Confidence);
    private record AiMathFixResponse(string FixedLatex, List<string>? Changes, double Confidence);
    private record AiWritingResponse(string ImprovedText, List<string>? Changes);
    private record AiClassifyResponse(string SuggestedType, double Confidence, string? Reasoning);
}

// -----------------------------------------------------------------------
// Prompt templates
// -----------------------------------------------------------------------

internal static class AiAssistantPrompts
{
    public const string GenerateMath = """
        You are a LaTeX math expert. Given a natural language description of a mathematical expression,
        generate the corresponding LaTeX code.

        Guidelines:
        - Generate clean, standard LaTeX that works with KaTeX
        - Use appropriate environments (align, equation, pmatrix, etc.) when needed
        - Prefer simple notation when multiple valid representations exist

        Respond with ONLY a JSON object: {"latex": "...", "explanation": "...", "confidence": 0.0-1.0}
        """;

    public const string FixMath = """
        You are a LaTeX math expert. Given a broken LaTeX expression (and optionally an error message),
        fix the expression so it compiles correctly.

        Common issues to fix:
        - Missing or extra braces
        - Incorrect command names
        - Unbalanced delimiters (\left/\right, \begin/\end)
        - Missing dollar signs
        - Incorrect environment usage

        Respond with ONLY a JSON object: {"fixedLatex": "...", "changes": ["description of change 1", ...], "confidence": 0.0-1.0}
        """;

    public const string ImproveWriting = """
        You are an academic writing expert. Improve the given text according to the specified action.

        Actions:
        - improve: Fix grammar, improve clarity and flow
        - paraphrase: Rewrite to convey the same meaning differently
        - expand: Add more detail and explanation
        - shorten: Make more concise while preserving meaning
        - formalize: Make more formal and academic in tone

        Apply the specified style (default: academic).

        Respond with ONLY a JSON object: {"improvedText": "...", "changes": ["description of change 1", ...]}
        """;

    public const string ClassifyBlock = """
        You are an expert document block classifier for an academic writing tool.
        Given a block of text content, determine the most accurate block type.

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
}
