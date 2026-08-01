using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Core.Capabilities;

/// <summary>
/// Reads a document's blocks and says what rendering it will need.
///
/// <para><b>Read-only, and nothing depends on it.</b> Both exporters already
/// walk <c>Block.Content</c>; this walks the same tree without emitting
/// anything, so it can answer "what would this need?" before a compile is
/// attempted rather than after one has failed.</para>
///
/// <para>That ordering is the point. Everything else in this plan reports
/// failures after the fact — the engine header names a fallback that already
/// happened, telemetry records a compile that already failed. This is the first
/// thing that can answer beforehand.</para>
/// </summary>
public static partial class RequirementExtractor
{
    /// <summary>
    /// Content keys whose value is LaTeX source rather than prose.
    /// </summary>
    /// <remarks>
    /// <para>Commands are collected only from these. Scanning every string for
    /// a backslash would pull control sequences out of prose, file paths and
    /// code blocks, and a pre-flight report that cries wolf gets skimmed past —
    /// at which point it is worth less than no report, because it is trusted
    /// less than nothing while looking like diligence.</para>
    ///
    /// <para><c>latex</c> is the pre-P1.4 spelling of <c>source</c> and is still
    /// the shape most rows are in.</para>
    /// </remarks>
    private static readonly HashSet<string> LatexBearingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "source", "latex", "raw", "rawLatex", "preamble",
    };

    /// <summary>Keys that hold prose — scanned for characters, not commands.</summary>
    private static readonly HashSet<string> ProseKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "caption", "title", "shortTitle", "alt", "items", "rows", "label",
    };

    [GeneratedRegex(@"\\([a-zA-Z]+)")]
    private static partial Regex ControlSequence();

    /// <summary>
    /// Everything the given blocks will ask of a renderer.
    /// </summary>
    /// <param name="blockContents">Each block's parsed <c>Content</c>.</param>
    /// <param name="fontFamily">The document's font family, when it sets one.</param>
    /// <param name="documentClass">The document class, when known.</param>
    public static IReadOnlyList<Requirement> Extract(
        IEnumerable<JsonElement> blockContents,
        string? fontFamily = null,
        string? documentClass = null)
    {
        // A set, because a document repeats the same character thousands of
        // times and the same command in every equation. Resolving each
        // occurrence would make a document-sized question expensive enough that
        // callers stop asking it.
        var requirements = new HashSet<Requirement>();

        if (!string.IsNullOrWhiteSpace(fontFamily))
            requirements.Add(new FontRequirement(fontFamily.Trim()));

        if (!string.IsNullOrWhiteSpace(documentClass))
            requirements.Add(new DocumentClassRequirement(documentClass.Trim()));

        foreach (var content in blockContents)
        {
            Walk(content, requirements, insideLatex: false);
        }

        // Stable order so two runs over the same document produce the same
        // report — otherwise a diff between them is unreadable.
        return [.. requirements.OrderBy(r => r.Key, StringComparer.Ordinal)];
    }

    private static void Walk(JsonElement element, HashSet<Requirement> into, bool insideLatex)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var isLatex = insideLatex || LatexBearingKeys.Contains(property.Name);

                    // Anything not recognised as prose or LaTeX is still walked
                    // for characters — a block type added later should not go
                    // unexamined just because nobody updated a list here.
                    Walk(property.Value, into, isLatex);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, into, insideLatex);
                break;

            case JsonValueKind.String:
                CollectFromText(element.GetString(), into, insideLatex);
                break;
        }
    }

    private static void CollectFromText(string? text, HashSet<Requirement> into, bool insideLatex)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (insideLatex)
        {
            foreach (Match match in ControlSequence().Matches(text))
            {
                into.Add(new CommandRequirement(match.Groups[1].Value));
            }
        }

        // Characters are characters wherever they appear — prose, a caption, an
        // equation. Enumerated as runes so an astral character is one
        // requirement rather than two surrogate halves, the same distinction
        // P2.5 had to make.
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value < 0x80) continue; // ASCII renders everywhere

            into.Add(new CodepointRequirement(rune.Value));

            var script = ScriptOf(rune.Value);
            if (script is not null) into.Add(new ScriptRequirement(script));
        }
    }

    /// <summary>
    /// The writing system a code point belongs to, or null when it is a symbol
    /// rather than part of a script.
    /// </summary>
    /// <remarks>
    /// <para>A coarse range table, deliberately. .NET exposes no Unicode Script
    /// property, and the alternative is a dependency carrying the full
    /// character database to answer a question whose useful resolution is
    /// "does this need a CJK font".</para>
    ///
    /// <para>The scripts named here are the ones where the answer changes what
    /// a renderer must do: CJK and Hangul need a large font, Arabic and Hebrew
    /// need right-to-left, Arabic and Devanagari need contextual shaping. A
    /// Latin accented character needs none of that, so it returns null and is
    /// resolved as a code point alone.</para>
    ///
    /// <para>Returning null for an unlisted range is the safe direction: it
    /// under-reports scripts rather than mislabelling one, and the code-point
    /// requirement still gets resolved either way.</para>
    /// </remarks>
    internal static string? ScriptOf(int codepoint) => codepoint switch
    {
        >= 0x0590 and <= 0x05FF => "Hebrew",
        >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F => "Arabic",
        >= 0x0900 and <= 0x097F => "Devanagari",
        >= 0x0E00 and <= 0x0E7F => "Thai",
        >= 0x3040 and <= 0x309F => "Hiragana",
        >= 0x30A0 and <= 0x30FF => "Katakana",
        >= 0xAC00 and <= 0xD7AF or >= 0x1100 and <= 0x11FF => "Hangul",
        // CJK Unified Ideographs, plus the extension A block below it.
        >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF => "Han",
        >= 0x0400 and <= 0x04FF => "Cyrillic",
        >= 0x0370 and <= 0x03FF => "Greek",
        _ => null,
    };

    /// <summary>
    /// A one-line summary of what a document needs, for logs and reports.
    /// </summary>
    public static string Summarise(IReadOnlyList<Requirement> requirements)
    {
        if (requirements.Count == 0) return "nothing beyond plain ASCII";

        var groups = requirements
            .GroupBy(r => r switch
            {
                CommandRequirement => "commands",
                PackageRequirement => "packages",
                DocumentClassRequirement => "document class",
                CodepointRequirement => "non-ASCII characters",
                ScriptRequirement => "scripts",
                FontRequirement => "fonts",
                _ => "other",
            })
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count()} {g.Key}");

        return string.Join(", ", groups);
    }
}
