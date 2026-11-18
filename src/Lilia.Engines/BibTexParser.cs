using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// Minimal BibTeX parser — pulls out cite key, entry type, and the
/// common author/title/year/journal/booktitle/publisher/volume/pages/
/// doi/url fields. Good enough for import-time ingestion of
/// Overleaf-style .bib files; doesn't handle string macros or
/// cross-references (those are rare in practice and the raw fields
/// survive untouched in the raw source for export).
///
/// Scope: parse @type{key, field = value, ...} entries. Handles both
/// brace-quoted and double-quoted values, escaped braces inside
/// values, and comments (lines starting with %).
/// </summary>
public static class BibTexParser
{
    public record BibEntry(string CiteKey, string EntryType, IReadOnlyDictionary<string, string> Fields);

    // Matches @type{citekey, ... body ...} spanning until the matching closing brace.
    // The body group captures everything between the first { and the matching }.
    private static readonly Regex EntryHeader = new(
        @"@(\w+)\s*\{\s*([^,\s]+)\s*,",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<BibEntry> Parse(string content)
    {
        var entries = new List<BibEntry>();
        if (string.IsNullOrWhiteSpace(content)) return entries;

        // Strip comment lines (BibTeX convention: % at line start). A comment line that holds entries is a file whose
        // line breaks were lost: only the comment before the first entry goes. A .bib starting "% Journals" stored on
        // one line lost all 102 of its entries (launch-bot #225, 18 Nov 2026).
        var lines = content.Split('\n')
            .Select(l => !l.TrimStart().StartsWith('%') ? l
                : EntryStart.Match(l) is { Success: true } first ? l[first.Index..] : null)
            .Where(l => l is not null)
            .ToArray();
        var cleaned = string.Join("\n", lines);

        var i = 0;
        while (i < cleaned.Length)
        {
            var at = cleaned.IndexOf('@', i);
            if (at < 0) break;
            var match = EntryHeader.Match(cleaned, at);
            if (!match.Success)
            {
                i = at + 1;
                continue;
            }

            var entryType = match.Groups[1].Value.ToLowerInvariant();
            var citeKey = match.Groups[2].Value.Trim();

            // Skip @string / @preamble / @comment — not citation entries.
            if (entryType is "string" or "preamble" or "comment")
            {
                i = match.Index + match.Length;
                continue;
            }

            var bodyStart = match.Index + match.Length;
            var bodyEnd = FindMatchingCloseBrace(cleaned, bodyStart - 1);
            if (bodyEnd < 0)
            {
                i = bodyStart;
                continue;
            }

            var body = cleaned.Substring(bodyStart, bodyEnd - bodyStart);
            var fields = ParseFields(body);
            entries.Add(new BibEntry(citeKey, NormalizeEntryType(entryType), fields));
            i = bodyEnd + 1;
        }

        return entries;
    }

    // Walks forward from an opening brace, tracking depth, to find the
    // matching closer. Respects braces inside quoted / braced values.
    private static int FindMatchingCloseBrace(string s, int openIdx)
    {
        var depth = 1;
        for (var i = openIdx + 1; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    // Walk the body splitting on top-level commas. Each field is
    // "key = value". Value may be {braced}, "quoted", or a bare token
    // (numbers / string macros we don't resolve — stored as-is).
    private static Dictionary<string, string> ParseFields(string body)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < body.Length)
        {
            // Skip whitespace + commas
            while (i < body.Length && (char.IsWhiteSpace(body[i]) || body[i] == ',')) i++;
            if (i >= body.Length) break;

            // Read key = chars up to '=' (stopping on whitespace)
            var keyStart = i;
            while (i < body.Length && body[i] != '=' && !char.IsWhiteSpace(body[i])) i++;
            var key = body.Substring(keyStart, i - keyStart).Trim();
            if (string.IsNullOrEmpty(key)) break;

            // Skip to '='
            while (i < body.Length && body[i] != '=') i++;
            if (i >= body.Length) break;
            i++; // past '='

            // Skip whitespace
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (i >= body.Length) break;

            // Read value
            string value;
            if (body[i] == '{')
            {
                var close = FindMatchingCloseBrace(body, i);
                if (close < 0) break;
                value = body.Substring(i + 1, close - i - 1);
                i = close + 1;
            }
            else if (body[i] == '"')
            {
                var close = body.IndexOf('"', i + 1);
                if (close < 0) break;
                value = body.Substring(i + 1, close - i - 1);
                i = close + 1;
            }
            else
            {
                // Bare token — read until comma or end
                var tokStart = i;
                while (i < body.Length && body[i] != ',' && body[i] != '\n') i++;
                value = body.Substring(tokStart, i - tokStart).Trim();
            }

            // Normalise: strip nested single braces commonly used for
            // capitalisation preservation, collapse whitespace.
            // A title written {{…}} keeps its case as written: the extra layer goes, and the entry remembers it
            // (titleprotected), so references.bib writes it back. IEEEtran lower-cased "The Transport Layer Security…"
            // (launch-bot #220, 18 Nov 2026).
            if (key.Equals("title", StringComparison.OrdinalIgnoreCase) && WholeBraced(value.Trim())) fields["titleprotected"] = "true";
            value = StripOuterBraces(value).Trim();
            value = Regex.Replace(value, @"\s+", " ");
            if (key.Length > 0) fields[key.ToLowerInvariant()] = value;
        }
        return fields;
    }

    /// <summary>Whether the value is one braced group from its first character to its last ("{A B}", not "{A} and {B}").</summary>
    internal static bool WholeBraced(string v)
    {
        if (v.Length < 2 || v[0] != '{' || v[^1] != '}') return false;
        var depth = 0;
        for (var i = 0; i < v.Length; i++)
        {
            if (v[i] == '\\') { i++; continue; }
            if (v[i] == '{') depth++;
            else if (v[i] == '}' && --depth == 0) return i == v.Length - 1;
        }
        return false;
    }

    private static readonly Regex EntryStart = new(@"@[A-Za-z]+\s*[{(]", RegexOptions.Compiled);

    private static string StripOuterBraces(string v)
    {
        // Some styles wrap values like {{Title Here}} — strip one layer.
        if (v.Length >= 2 && v[0] == '{' && v[^1] == '}')
            return v.Substring(1, v.Length - 2);
        return v;
    }

    // Match Lilia.Core.Entities.BibliographyEntryTypes vocabulary.
    private static string NormalizeEntryType(string t) => t switch
    {
        "article" => "article",
        "book" => "book",
        "inproceedings" or "conference" => "inproceedings",
        // A chapter and the other standard types keep theirs: as @misc a chapter lost its book in every style
        // (launch-bot #131, 17 Nov 2026: ACM printed no book or series).
        "incollection" => "incollection",
        "inbook" => "inbook",
        "proceedings" => "proceedings",
        "manual" => "manual",
        "booklet" => "booklet",
        "unpublished" => "unpublished",
        "phdthesis" => "phdthesis",
        "mastersthesis" => "mastersthesis",
        "techreport" => "techreport",
        "online" or "electronic" or "www" => "online",
        _ => "misc",
    };
}
