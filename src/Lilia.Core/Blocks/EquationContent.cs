using System.Text.Json;

namespace Lilia.Core.Blocks;

/// <summary>
/// Reads an equation block's content, whichever shape it is stored in.
///
/// <para><b>The shape being moved to.</b></para>
/// <code>
/// { notation: "latex", source: "\\mathcal{L} = …", ast: &lt;tree | null&gt; }
/// </code>
///
/// <para><b>The shape most rows are still in.</b> <c>{ latex: "…" }</c>.</para>
///
/// <para>Every reader goes through here so the two can coexist: `source` wins,
/// `latex` is the fallback, and nothing has to know which era a row came from.
/// That is the whole point of landing this before the write side — readers stop
/// caring about the shape first, so changing what gets written is then a change
/// nobody downstream can notice.</para>
///
/// <para><b>Why rename at all — the argument is not tidiness.</b> A string can
/// only be printed; a tree can be used. Validating without compiling,
/// accessible math (PDF/UA needs structure — a rasterised equation has none),
/// semantic search, and symbol lookup (24% of all math questions in the corpus,
/// stable over 14 years) are each blocked by math being an opaque string. The
/// reserved <c>ast</c> field is where that stops being true.</para>
///
/// <para><b>What was NOT worth doing.</b> An earlier plan called for writing
/// <c>ast: null</c> into every equation block now, to "reserve" the field before
/// beta made the migration expensive. Block content is a schemaless
/// <c>JsonDocument</c>: absent and null are indistinguishable to every reader,
/// so that migration does not exist and pre-writing nulls buys nothing. The part
/// that genuinely gets more expensive with every authored document is the
/// <c>latex</c> → <c>source</c> rename, because afterwards you must either
/// migrate rows or support both shapes forever. Hence this file.</para>
/// </summary>
public static class EquationContent
{
    /// <summary>The only notation currently written, and the assumed default.</summary>
    public const string LatexNotation = "latex";

    private const string SourceProperty = "source";
    private const string LegacySourceProperty = "latex";
    private const string NotationProperty = "notation";
    private const string AstProperty = "ast";

    /// <summary>
    /// The equation's source text, verbatim. Prefers <c>source</c>, falls back
    /// to the legacy <c>latex</c>, and returns "" when neither is present —
    /// callers already treat an empty equation as an empty equation rather than
    /// an error, so this keeps that behaviour exactly.
    /// </summary>
    public static string ReadSource(JsonElement content)
    {
        if (TryReadString(content, SourceProperty, out var source)) return source;
        if (TryReadString(content, LegacySourceProperty, out var legacy)) return legacy;
        return "";
    }

    /// <summary>
    /// The equation's mode, without its star, and whether it is numbered.
    ///
    /// <para><c>equationMode</c> is what the editor writes ("display",
    /// "display*", "align*", "gather", "inline" …); <c>mode</c> is the older
    /// shape and <c>displayMode: false</c> the oldest spelling of inline. A
    /// trailing star, or <c>numbered: false</c>, means no number — as
    /// <c>equation*</c> does in LaTeX. Both exporters read the mode here, so
    /// neither can drift from what the editor shows again.</para>
    /// </summary>
    public static (string Mode, bool Numbered) ReadMode(JsonElement content)
    {
        var mode = ReadRawMode(content);
        var starred = mode.EndsWith('*');
        var baseMode = starred ? mode[..^1] : mode;
        var optedOut = content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("numbered", out var n) && n.ValueKind == JsonValueKind.False;
        // A wrapping mode is a display equation whose body sits in that environment: ReadMathSource adds it.
        if (WrappingModes.Contains(baseMode)) baseMode = "display";
        return (baseMode, baseMode != "inline" && !starred && !optedOut);
    }

    /// <summary>
    /// The modes the editor stores as an environment around the body (6 Nov 2026, issue 33 of the
    /// launch bot): a [ ] matrix is saved as <c>equationMode: "bmatrix"</c> with only its rows,
    /// <c>1 &amp; 2 \\ 3 &amp; 4</c>, and the editor's preview adds the environment. The exports read
    /// the body alone, took its <c>&amp;</c> for an alignment and printed a matrix as numbered
    /// <c>align</c> rows, without its brackets; cases lost their brace the same way.
    /// </summary>
    private static readonly HashSet<string> WrappingModes =
        ["cases", "split", "matrix", "pmatrix", "bmatrix", "Bmatrix", "vmatrix", "Vmatrix"];

    /// <summary>
    /// The equation's maths as the editor shows it: <see cref="ReadSource"/>, inside the environment its
    /// mode names when that mode wraps the body (a matrix, cases, split), unless the source already
    /// opens that environment. What every export renders; <see cref="ReadSource"/> stays verbatim for
    /// the write path.
    /// </summary>
    public static string ReadMathSource(JsonElement content)
    {
        var source = ReadSource(content);
        var mode = ReadRawMode(content).TrimEnd('*');
        if (source.Length == 0 || !WrappingModes.Contains(mode)) return source;
        if (source.TrimStart().StartsWith($@"\begin{{{mode}}}", StringComparison.Ordinal)) return source;
        return $@"\begin{{{mode}}}" + source + $@"\end{{{mode}}}";
    }

    /// <summary>
    /// The modes whose environment is the whole display (8 Nov 2026, the launch bot's issue 44): an
    /// align block stores its rows alone, <c>x &amp;= 1 \\ y &amp;= 2</c>. The LaTeX writer opens
    /// <c>align</c> itself, but the HTML and Markdown exports put the bare rows between <c>$$</c>, where
    /// KaTeX shows a top-level <c>&amp;</c> as a red error.
    /// </summary>
    private static readonly HashSet<string> DisplayEnvironmentModes = ["align", "gather", "multline"];

    /// <summary>
    /// <see cref="ReadMathSource"/> as a self-contained display: an align, gather or multline body inside
    /// its own environment, star kept, unless the source already opens it. For the exports that hand the
    /// maths to KaTeX or MathJax whole (HTML, Markdown); the LaTeX and Word writers build the environment
    /// from the mode themselves.
    /// </summary>
    public static string ReadDisplaySource(JsonElement content)
    {
        var source = ReadMathSource(content);
        var mode = ReadRawMode(content);
        if (source.Length == 0 || !DisplayEnvironmentModes.Contains(mode.TrimEnd('*'))) return source;
        if (source.TrimStart().StartsWith(@"\begin{", StringComparison.Ordinal)) return source;
        return $@"\begin{{{mode}}}" + source + $@"\end{{{mode}}}";
    }

    /// <summary>
    /// The rows of a multi-line body, split at its top-level <c>\\</c> (not those inside a matrix or
    /// cases), trimmed, a trailing empty row dropped, and whether each row is numbered:
    /// <c>\nonumber</c> or <c>\notag</c> takes its number away and is removed. The Word export prints
    /// one number per row, as the PDF does (8 Nov 2026, issue 44).
    /// </summary>
    public static IReadOnlyList<(string Latex, bool Numbered)> SplitRows(string? source)
    {
        var rows = new List<(string, bool)>();
        if (string.IsNullOrWhiteSpace(source)) return rows;

        var depth = 0;
        var start = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '\\') continue;
            if (StartsWithAt(source, i, @"\begin{")) { depth++; i += 6; continue; }
            if (StartsWithAt(source, i, @"\end{")) { if (depth > 0) depth--; i += 4; continue; }
            if (i + 1 < source.Length && source[i + 1] == '\\' && depth == 0)
            {
                rows.Add(Row(source[start..i]));
                start = i + 2;
            }
            i++;
        }
        rows.Add(Row(source[start..]));
        if (rows.Count > 1 && rows[^1].Item1.Length == 0) rows.RemoveAt(rows.Count - 1);
        return rows;

        static (string, bool) Row(string text)
        {
            var numbered = !NoNumber.IsMatch(text);
            return (NoNumber.Replace(text, "").Trim(), numbered);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex NoNumber =
        new(@"\\(?:nonumber|notag)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string ReadRawMode(JsonElement content)
    {
        if (TryReadString(content, "equationMode", out var em) && em.Length > 0) return em;
        if (TryReadString(content, "mode", out var m) && m.Length > 0) return m;
        if (content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty("displayMode", out var dm) && dm.ValueKind == JsonValueKind.False) return "inline";
        return "display";
    }

    /// <summary>
    /// The notation the source is written in. Defaults to <c>latex</c>: every
    /// row predating this field is LaTeX, and so is everything the editor
    /// writes today. Callers should not branch on it yet — it exists so that
    /// the day a second notation appears, the rows already say which is which.
    /// </summary>
    public static string ReadNotation(JsonElement content) =>
        TryReadString(content, NotationProperty, out var notation) ? notation : LatexNotation;

    /// <summary>
    /// The parsed tree, when one has been stored. Always false today — no
    /// parser writes it yet (that is P3.4). Present so that populating it later
    /// is purely additive, and so callers can be written against it now.
    /// </summary>
    public static bool TryReadAst(JsonElement content, out JsonElement ast)
    {
        ast = default;
        if (content.ValueKind != JsonValueKind.Object) return false;
        if (!content.TryGetProperty(AstProperty, out var value)) return false;
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return false;
        ast = value;
        return true;
    }

    /// <summary>
    /// True when this content still carries only the legacy <c>latex</c>
    /// property. Used by the write path to decide whether it has anything to
    /// normalise, and by tests to prove the fallback is actually exercised.
    /// </summary>
    public static bool IsLegacyShape(JsonElement content) =>
        content.ValueKind == JsonValueKind.Object
        && !HasNonEmpty(content, SourceProperty)
        && HasNonEmpty(content, LegacySourceProperty);

    private static bool TryReadString(JsonElement content, string property, out string value)
    {
        value = "";
        if (content.ValueKind != JsonValueKind.Object) return false;
        if (!content.TryGetProperty(property, out var element)) return false;
        if (element.ValueKind != JsonValueKind.String) return false;

        var text = element.GetString();
        // An explicit empty string is not a usable source: falling through to
        // the other property is what lets a half-migrated row still render.
        if (string.IsNullOrEmpty(text)) return false;

        value = text;
        return true;
    }

    private static bool HasNonEmpty(JsonElement content, string property) =>
        TryReadString(content, property, out _);

    /// <summary>
    /// Whether the source contains an alignment tab that is not inside an
    /// environment of its own — the shape that needs <c>align</c> rather than
    /// <c>equation</c>.
    /// </summary>
    /// <remarks>
    /// <para>Found by parsing the real corpus: four equation blocks store an
    /// alignment body — <c>a &amp;= b \\ c &amp;= d</c> — with <c>mode</c> unset.
    /// The emitter only reaches for <c>align</c> when mode says so, and an
    /// absent mode reads as <c>display</c>, so those went into
    /// <c>\begin{equation}</c>, where an alignment tab is a hard error:</para>
    /// <code>
    /// ! Misplaced alignment tab character &amp;.
    /// !  ==&gt; Fatal error occurred, no output PDF file produced!
    /// </code>
    /// <para>Not a rendering blemish — the export produces nothing at all.</para>
    ///
    /// <para>Only <b>top-level</b> tabs count. <c>&amp;</c> inside
    /// <c>pmatrix</c>, <c>array</c>, <c>cases</c> or any other environment is
    /// ordinary and correct, and one of the four blocks is exactly that — a
    /// matrix that must be left alone. Environment depth is tracked so the two
    /// are not confused, and an escaped <c>\&amp;</c> is a literal ampersand
    /// rather than a tab.</para>
    /// </remarks>
    public static bool HasTopLevelAlignment(string? source)
    {
        if (string.IsNullOrEmpty(source)) return false;

        var environmentDepth = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\\')
            {
                if (StartsWithAt(source, i, @"\begin{"))
                {
                    environmentDepth++;
                    i += 6;
                    continue;
                }

                if (StartsWithAt(source, i, @"\end{"))
                {
                    if (environmentDepth > 0) environmentDepth--;
                    i += 4;
                    continue;
                }

                // Any other backslash escapes the character after it. Covers
                // both `\&` (a literal ampersand, not a tab) and `\\` (a row
                // break, whose second backslash must not be read as the start
                // of a new command).
                i++;
                continue;
            }

            if (source[i] == '&' && environmentDepth == 0) return true;
        }

        return false;
    }

    private static bool StartsWithAt(string text, int index, string value) =>
        index + value.Length <= text.Length &&
        string.CompareOrdinal(text, index, value, 0, value.Length) == 0;
}
