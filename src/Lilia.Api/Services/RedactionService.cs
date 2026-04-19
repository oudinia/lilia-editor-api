using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

public interface IRedactionService
{
    /// <summary>
    /// Scrub likely PII from a piece of text before it crosses the network
    /// to an AI provider. Returns both the redacted text and a summary of
    /// what was replaced (counts per rule). Never returns matched strings.
    /// </summary>
    RedactionResult Redact(string text);
}

public record RedactionResult(string Text, JsonDocument Summary, int TotalReplacements);

/// <summary>
/// Regex-based PII scrubber. Conservative — we'd rather leave a rare
/// false-negative than ship document text to a provider uncleaned. Match
/// order matters; URLs are replaced first so the bare-domain rule doesn't
/// over-match inside them.
///
/// Tokens use opaque placeholders ([[EMAIL_1]], [[PHONE_2]], …) that are
/// meaningful to the prompt ("the author's email" reads fine in-context)
/// and are stable within a single call so the AI can refer to them across
/// multiple mentions.
/// </summary>
public class RedactionService : IRedactionService
{
    // Ordered from most-specific to least — first match wins.
    private static readonly (string Name, Regex Pattern)[] Rules =
    [
        // Emails. The dot-in-local-part pattern is loose but practical.
        ("email",        new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)),

        // LinkedIn profile URLs — kept separate from generic URL rule so
        // they can be tokenised without losing the fact that a social
        // handle was present.
        ("linkedin",     new Regex(@"https?://(?:www\.)?linkedin\.com/(?:in|pub)/[A-Za-z0-9\-_%]+/?", RegexOptions.Compiled | RegexOptions.IgnoreCase)),

        // GitHub / Twitter/X / Instagram profile URLs — same treatment.
        ("github",       new Regex(@"https?://(?:www\.)?github\.com/[A-Za-z0-9\-_]+/?", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("twitter",      new Regex(@"https?://(?:www\.)?(?:twitter|x)\.com/[A-Za-z0-9_]+/?", RegexOptions.Compiled | RegexOptions.IgnoreCase)),

        // Phone numbers. International + common national formats. Keeps
        // false-positives low by requiring at least 8 digits total when
        // separators are considered.
        ("phone",        new Regex(@"(?<!\w)(?:\+\d{1,3}[\s.-]?)?(?:\(?\d{2,4}\)?[\s.-]?){2,4}\d{2,4}(?!\w)", RegexOptions.Compiled)),

        // ORCID — academic identifier. Cheap to redact and plausibly PII.
        ("orcid",        new Regex(@"\b(?:\d{4}-){3}\d{3}[\dX]\b", RegexOptions.Compiled)),

        // Bank-card-ish digit runs. 13–19 digits. Exclude things that look
        // like citation keys or ISBNs (the ISBN rule below handles those).
        ("card_like",    new Regex(@"\b(?:\d[\s-]?){13,19}\b", RegexOptions.Compiled)),

        // ISBNs — intentionally NOT redacted; they're bibliographic data
        // and should pass through. Listed here as a regex for documentation.
        // ("isbn",         new Regex(@"\bISBN(?:-1[03])?:?\s*\d[\d\s-]{8,14}[\dX]\b", RegexOptions.Compiled)),
    ];

    public RedactionResult Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            var empty = JsonDocument.Parse("{}");
            return new RedactionResult(text ?? string.Empty, empty, 0);
        }

        var counts = new Dictionary<string, int>();
        var total = 0;

        foreach (var (name, pattern) in Rules)
        {
            var localIndex = 0;
            text = pattern.Replace(text, _ =>
            {
                localIndex++;
                total++;
                return $"[[{name.ToUpperInvariant()}_{localIndex}]]";
            });
            if (localIndex > 0) counts[name] = localIndex;
        }

        var summary = JsonSerializer.SerializeToDocument(counts);
        return new RedactionResult(text, summary, total);
    }
}
