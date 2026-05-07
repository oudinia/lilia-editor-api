namespace Lilia.Import.Services;

/// <summary>
/// Splits a raw <c>\documentclass[opts]{class}</c> options blob into the
/// structured <c>Document</c> columns (font size, paper size, columns) and
/// the leftover unrecognised tokens which stay in
/// <c>Document.LatexDocumentClassOptions</c>.
///
/// LILIA-121 (C1). Before this parser, an imported
/// <c>\documentclass[twocolumn,11pt,a4paper]{book}</c> stored everything
/// verbatim in the options blob — and the export builder then merged the
/// structured columns on top, producing
/// <c>\documentclass[11pt,a4paper,twocolumn,11pt,a4paper]{book}</c> on
/// re-export. LaTeX accepts the duplicates, but the source pane reads as
/// noise. This helper removes the recognised tokens at import time so the
/// round-trip stays clean.
/// </summary>
public static class ClassOptionsParser
{
    /// <summary>
    /// Result of splitting a <c>\documentclass</c> options blob.
    /// All structured fields are nullable — they're set only when the input
    /// actually contained a recognised token. Callers should apply each set
    /// field to the new <c>Document</c> and otherwise leave the existing
    /// default in place.
    /// </summary>
    public class ParseResult
    {
        /// <summary>10 / 11 / 12 — set when "10pt" / "11pt" / "12pt" matched.</summary>
        public int? FontSize { get; set; }

        /// <summary>"a4" / "letter" / "a5" / "legal" / "executive" / "b5".</summary>
        public string? PaperSize { get; set; }

        /// <summary>1 or 2 — set when "onecolumn" / "twocolumn" matched.</summary>
        public int? Columns { get; set; }

        /// <summary>
        /// Comma-joined leftover tokens — written to
        /// <c>Document.LatexDocumentClassOptions</c>. Empty string when every
        /// token was recognised; null when the input itself was null/empty.
        /// </summary>
        public string? RemainingOptions { get; set; }
    }

    /// <summary>
    /// Parse a raw options blob. Whitespace around tokens is trimmed.
    /// Recognised tokens are stripped from the remaining-options string;
    /// everything else (e.g. "landscape", "oneside", "manuscript",
    /// "acmsmall") is preserved in source order.
    /// </summary>
    public static ParseResult Parse(string? optionsBlob)
    {
        var result = new ParseResult();

        if (optionsBlob is null)
        {
            result.RemainingOptions = null;
            return result;
        }

        if (string.IsNullOrWhiteSpace(optionsBlob))
        {
            result.RemainingOptions = string.Empty;
            return result;
        }

        var rawTokens = optionsBlob.Split(',');
        var leftover = new List<string>(rawTokens.Length);

        foreach (var raw in rawTokens)
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;

            // Font size: 10pt / 11pt / 12pt.
            if (token.Equals("10pt", StringComparison.OrdinalIgnoreCase))
            {
                result.FontSize = 10;
                continue;
            }
            if (token.Equals("11pt", StringComparison.OrdinalIgnoreCase))
            {
                result.FontSize = 11;
                continue;
            }
            if (token.Equals("12pt", StringComparison.OrdinalIgnoreCase))
            {
                result.FontSize = 12;
                continue;
            }

            // Paper size — six standard variants. The structured Document
            // column stores the short slug (a4, letter, …), the export
            // builder re-attaches the "paper" suffix when emitting.
            if (token.Equals("a4paper", StringComparison.OrdinalIgnoreCase))
            {
                result.PaperSize = "a4";
                continue;
            }
            if (token.Equals("letterpaper", StringComparison.OrdinalIgnoreCase))
            {
                result.PaperSize = "letter";
                continue;
            }
            if (token.Equals("a5paper", StringComparison.OrdinalIgnoreCase))
            {
                result.PaperSize = "a5";
                continue;
            }
            if (token.Equals("legalpaper", StringComparison.OrdinalIgnoreCase))
            {
                result.PaperSize = "legal";
                continue;
            }
            if (token.Equals("executivepaper", StringComparison.OrdinalIgnoreCase))
            {
                result.PaperSize = "executive";
                continue;
            }
            if (token.Equals("b5paper", StringComparison.OrdinalIgnoreCase))
            {
                result.PaperSize = "b5";
                continue;
            }

            // Columns — onecolumn is the article default, but explicit "1"
            // is still stripped so it doesn't clutter the options blob.
            if (token.Equals("twocolumn", StringComparison.OrdinalIgnoreCase))
            {
                result.Columns = 2;
                continue;
            }
            if (token.Equals("onecolumn", StringComparison.OrdinalIgnoreCase))
            {
                result.Columns = 1;
                continue;
            }

            // Anything else — landscape, oneside/twoside, titlepage/notitlepage,
            // draft/final, class-specific (manuscript, acmsmall, …) — stays
            // verbatim. The export builder re-emits these untouched.
            leftover.Add(token);
        }

        result.RemainingOptions = leftover.Count == 0 ? string.Empty : string.Join(",", leftover);
        return result;
    }
}
