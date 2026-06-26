using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// "Ask Lilia" — the single natural-language front door to the AI skill family.
///
/// A user types one message; the <see cref="AskLiliaRouter"/> classifies intent
/// to one skill (the SKILL.md artifacts in lilia-docs/ai-skill), and the caller
/// assembles the system prompt = a <see cref="Proficiency"/> preamble + that
/// skill's guidance + the shared output contract, then runs it through the
/// existing AI call path (governance, metering, model catalog, verification).
///
/// This is the routing brain + the proficiency layer. Embedding each skill's
/// full SKILL.md guidance (the way <see cref="AiArchitectPrompts"/> embeds the
/// architect) and dispatching generation are the next steps; the architect
/// remains the reference implementation of a fully-wired skill.
/// </summary>

/// <summary>How much hand-holding the author wants. Modulates every skill.</summary>
public enum Proficiency { Beginner, Intermediate, Advanced }

/// <summary>A skill in the family — metadata + the words that route to it.</summary>
public sealed record AiSkill(
    string Id,            // matches the SKILL.md folder, e.g. "lilia-citations"
    string Name,
    string WhenToUse,     // one-line summary (the SKILL.md description, condensed)
    string[] Triggers,    // intent keywords/phrases
    string Tier,          // default model tier: "fast" | "default" | "premium"
    int Priority);        // tie-break: higher wins when scores tie

public sealed record AskRoute(string SkillId, string SkillName, double Confidence, string Reason);

/// <summary>Per-proficiency guidance, prepended to the chosen skill's prompt so
/// the SAME skill adapts from a gentle guide to a terse expert tool.</summary>
public static class ProficiencyGuidance
{
    public static Proficiency Parse(string? s) => (s?.Trim().ToLowerInvariant()) switch
    {
        "beginner" or "new" or "novice" => Proficiency.Beginner,
        "advanced" or "expert" or "pro" => Proficiency.Advanced,
        _ => Proficiency.Intermediate,
    };

    public static string For(Proficiency p) => p switch
    {
        Proficiency.Beginner => """
            AUTHOR LEVEL — BEGINNER. The author is new to LaTeX/Lilia. Explain choices in plain
            language and define any jargon you use. Prefer safe, conventional defaults over clever
            options. Add a one-line "why" for any non-obvious move. ALWAYS end with a clear next
            step ("Next: …") and, when useful, one or two suggested follow-up actions. Be
            encouraging; never assume prior LaTeX knowledge.
            """,
        Proficiency.Advanced => """
            AUTHOR LEVEL — ADVANCED. The author is an expert. Be terse and dense: lead with the
            artifact, skip explanations, and only flag genuine subtleties or trade-offs. Respect
            their existing notation/conventions; do not over-explain or add hand-holding.
            """,
        _ => """
            AUTHOR LEVEL — INTERMEDIATE. The author knows the basics. Be concise; explain only the
            non-obvious choices. Lead with the artifact, keep prose short.
            """,
    };
}

public interface IAskLiliaRouter
{
    /// <summary>Classify a message to a skill. Never throws; falls back to the architect.</summary>
    AskRoute Route(string message);
    IReadOnlyList<AiSkill> Skills { get; }
    AiSkill Get(string id);
    /// <summary>Assemble the system-prompt preamble (proficiency + skill summary). The full skill
    /// guidance is appended by the generation step (see AiArchitectPrompts for the pattern).</summary>
    string BuildPreamble(AiSkill skill, Proficiency level);
}

public sealed class AskLiliaRouter : IAskLiliaRouter
{
    // The skill family (mirrors lilia-docs/ai-skill/*/SKILL.md). Triggers are lowercase.
    private static readonly AiSkill[] _skills =
    {
        new("lilia-citations", "Citations",
            "DOI/ISBN/arXiv/messy ref → clean BibTeX + cite key + bibliography.",
            new[] { "cite", "citation", "reference", "bibtex", "bibliography", "doi", "isbn", "arxiv", "\\cite", "citep", "citet" },
            "default", 6),
        new("lilia-compile-doctor", "Compile Doctor",
            "A compile error or a Lilia validation finding → cause + minimal fix.",
            new[] { "error", "won't compile", "wont compile", "doesn't compile", "undefined control", "missing $", "fix this", "broken", "validation", "unsupported", "won't build", "red error" },
            "default", 9),
        new("lilia-equation", "Equation",
            "Description → correct display/inline LaTeX math; or fix existing math.",
            new[] { "equation", "formula", "math", "latex for", "derivative", "integral", "matrix", "softmax", "summation", "\\frac", "align" },
            "default", 5),
        new("lilia-table", "Table",
            "Description / pasted CSV → a booktabs table block.",
            new[] { "table", "tabular", "booktabs", "columns", "rows", "spreadsheet" },
            "default", 5),
        new("lilia-figure", "Figure",
            "Description → TikZ diagram / pgfplots chart, or an image figure.",
            new[] { "figure", "diagram", "tikz", "draw", "plot", "graph", "chart", "pgfplots" },
            "default", 5),
        new("lilia-tools", "Tools",
            "Raw input → the right conversion; routes to the specialized skill.",
            new[] { "convert", "to bibtex", "word to latex", "docx", "import", "paste", "from excel", "from csv" },
            "default", 4),
        new("lilia-polish", "Polish",
            "Tighten/clarify selected prose; preserves meaning & voice.",
            new[] { "rewrite", "polish", "tighten", "clarify", "proofread", "grammar", "improve", "shorten", "rephrase", "reword", "active voice", "more formal" },
            "fast", 5),
        new("lilia-coverage", "Coverage",
            "\"Can Lilia compile X?\" → support answer + supported alternative.",
            new[] { "supported", "support", "usepackage", "package", "can lilia", "does lilia", "compile this", "is this supported" },
            "fast", 5),
        new("lilia-journal", "Journal",
            "Format for a venue → document class + restructured sections.",
            new[] { "format for", "ieee", "acm", "neurips", "icml", "iclr", "elsevier", "springer", "lncs", "submission", "journal", "conference", "template", "camera-ready", "two-column" },
            "default", 7),
        new("lilia-tutor", "Tutor",
            "Lilia-aware \"how do I…?\" help + a concrete next step.",
            new[] { "how do i", "how to", "what is", "where do i", "explain", "help me understand", "what's the", "how can i" },
            "fast", 3),
        new("lilia-document-architect", "Document Architect",
            "Design a paper/thesis/report/talk as typed blocks — a real first draft.",
            new[] { "draft", "write a paper", "outline", "structure", "thesis", "report", "slides", "problem set", "scaffold", "new paper", "start a" },
            "default", 4),
    };

    private static readonly AiSkill _default =
        _skills.First(s => s.Id == "lilia-document-architect");

    public IReadOnlyList<AiSkill> Skills => _skills;

    public AiSkill Get(string id) => _skills.FirstOrDefault(s => s.Id == id) ?? _default;

    public AskRoute Route(string message)
    {
        var text = (message ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
            return new AskRoute(_default.Id, _default.Name, 0.0, "empty message → architect");

        AiSkill? best = null;
        var bestScore = 0;
        string? hit = null;
        foreach (var s in _skills)
        {
            var score = 0;
            string? matched = null;
            foreach (var t in s.Triggers)
            {
                if (ContainsPhrase(text, t)) { score += t.Contains(' ') ? 2 : 1; matched ??= t; }
            }
            if (score == 0) continue;
            // tie-break by skill priority (scaled so a clear keyword win still beats priority)
            var ranked = score * 10 + s.Priority;
            var bestRanked = bestScore * 10 + (best?.Priority ?? 0);
            if (best is null || ranked > bestRanked) { best = s; bestScore = score; hit = matched; }
        }

        if (best is null)
            return new AskRoute(_default.Id, _default.Name, 0.2,
                "no clear intent → architect (the general drafting skill)");

        // crude confidence: saturates with the number of matched triggers
        var confidence = Math.Min(0.95, 0.5 + 0.15 * bestScore);
        return new AskRoute(best.Id, best.Name, confidence, $"matched \"{hit}\"");
    }

    public string BuildPreamble(AiSkill skill, Proficiency level)
    {
        var sb = new StringBuilder();
        sb.AppendLine(ProficiencyGuidance.For(level)).AppendLine();
        sb.AppendLine($"ACTIVE SKILL — {skill.Name}. {skill.WhenToUse}");
        sb.AppendLine("(The full skill guidance from its SKILL.md is appended by the generation step.)");
        return sb.ToString();
    }

    private static bool ContainsPhrase(string haystack, string needle)
    {
        if (needle.Contains(' ') || needle.Contains('\\'))
            return haystack.Contains(needle, StringComparison.Ordinal);
        // word-ish match for single tokens, so "error" doesn't fire inside "terror"
        return Regex.IsMatch(haystack, $@"(^|[^a-z]){Regex.Escape(needle)}([^a-z]|$)");
    }
}
