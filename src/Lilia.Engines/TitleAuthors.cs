using System.Text;

namespace Lilia.Engines;

/// <summary>
/// One author of the Title block: the name, the affiliation lines under it and
/// the <c>\thanks</c> notes attached to it. <see cref="NoteSymbols"/> holds the
/// footnote mark of each note, numbered across the whole title.
/// </summary>
public sealed record TitleAuthor(
    string Name,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> NoteSymbols);

/// <summary>
/// Reads the Title block's <c>author</c> string, which is one LaTeX string
/// such as <c>Jane Doe\thanks{Funded.}\\U. of South \and John Doe\\U. of North</c>,
/// into a list of authors. The LaTeX export passes the string through verbatim;
/// this is for the targets that cannot (Typst).
///
/// <para>Authors are split at a top-level <c>\and</c> (also <c>\And</c>), never
/// one inside braces. <c>\\</c> starts a new line (the first is the name, the
/// rest are the affiliation). <c>\thanks{...}</c> is lifted out as a note, with
/// balanced braces. Any other command is dropped with its argument, the way
/// <c>PlainTitleMetaForTypst</c> always did.</para>
/// </summary>
public static class TitleAuthors
{
    // LaTeX's \thanks marks, \fnsymbol order.
    private static readonly string[] Symbols =
        ["*", "†", "‡", "§", "¶", "‖", "**", "††", "‡‡"];

    /// <summary>The footnote mark for the n-th note (1-based). Past nine, where LaTeX gives up, the number.</summary>
    public static string SymbolFor(int n) =>
        n >= 1 && n <= Symbols.Length ? Symbols[n - 1] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static IReadOnlyList<TitleAuthor> Parse(string? text)
    {
        var result = new List<TitleAuthor>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var lines = new List<string>();
        var notes = new List<string>();
        var cur = new StringBuilder();
        var noteCounter = 0;
        var perAuthorSymbols = new List<string>();

        void EndLine()
        {
            var line = Tidy(cur.ToString());
            cur.Clear();
            if (line.Length > 0) lines.Add(line);
        }

        void EndAuthor()
        {
            EndLine();
            if (lines.Count > 0 || notes.Count > 0)
            {
                var name = lines.Count > 0 ? lines[0] : "";
                result.Add(new TitleAuthor(name, lines.Skip(1).ToList(), notes.ToList(), perAuthorSymbols.ToList()));
            }
            lines.Clear();
            notes.Clear();
            perAuthorSymbols.Clear();
        }

        var depth = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '{') { depth++; i++; continue; }
            if (c == '}') { if (depth > 0) depth--; i++; continue; }
            if (c != '\\') { cur.Append(c); i++; continue; }

            // Backslash.
            if (i + 1 >= text.Length) { i++; break; }
            var n = text[i + 1];
            if (n == '\\')
            {
                EndLine();
                i += 2;
                if (i < text.Length && text[i] == '*') i++;
                i = SkipOptional(text, i);
                continue;
            }
            if (SymbolAccents.TryGetValue(n, out var accentMark))
            {
                // \'e  \"{o}: the accented letter (5 Oct review).
                var (letter, next) = AccentTarget(text, i + 2);
                cur.Append(Accent(letter, accentMark));
                i = next;
                continue;
            }
            if (!char.IsAsciiLetter(n))
            {
                // \& \% \$ \# \_ \{ \} give the character; \, \; \  give a space.
                cur.Append(n is ',' or ';' or ' ' or '!' ? ' ' : n);
                i += 2;
                continue;
            }

            var j = i + 1;
            while (j < text.Length && char.IsAsciiLetter(text[j])) j++;
            var name = text[(i + 1)..j];
            if (j < text.Length && text[j] == '*') j++;

            if (depth == 0 && (name == "and" || name == "And"))
            {
                EndAuthor();
                i = j;
                continue;
            }
            if (name == "thanks")
            {
                var k = j;
                while (k < text.Length && char.IsWhiteSpace(text[k])) k++;
                if (k < text.Length && text[k] == '{')
                {
                    var end = MatchingBrace(text, k);
                    var inner = text[(k + 1)..end];
                    var note = Tidy(Flatten(inner));
                    if (note.Length > 0)
                    {
                        noteCounter++;
                        notes.Add(note);
                        perAuthorSymbols.Add(SymbolFor(noteCounter));
                    }
                    i = Math.Min(end + 1, text.Length);
                    continue;
                }
            }

            if (name.Length == 1 && LetterAccents.TryGetValue(name, out var letterMark))
            {
                // \H{o}  \c{c}  \v{s}
                var (letter, next) = AccentTarget(text, j);
                cur.Append(Accent(letter, letterMark));
                i = next;
                continue;
            }
            if (Letters.TryGetValue(name, out var special))
            {
                // \ss  \o  \ae  \L (and an empty {} after them)
                cur.Append(special);
                i = j;
                if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '}') i += 2;
                else if (i < text.Length && text[i] == ' ') i++;   // "\o ren": the space ends the command
                continue;
            }
            if (KeepText.Contains(name))
            {
                // \textbf{Jane Doe}: skip the command and read its group as ordinary text, so a \\ or a
                // \thanks inside it still counts.
                i = SkipOptional(text, j);
                continue;
            }

            // Unknown command: drop it with its optional and mandatory argument.
            j = SkipOptional(text, j);
            var m = j;
            while (m < text.Length && text[m] == ' ') m++;
            if (m < text.Length && text[m] == '{') j = Math.Min(MatchingBrace(text, m) + 1, text.Length);
            i = j;
        }
        EndAuthor();
        return result;
    }

    /// <summary>
    /// Plain visible text of a fragment: accents resolved to letters, formatting commands kept as their
    /// text, other commands dropped, escapes resolved, braces removed.
    ///
    /// <para>5 Oct review: accents and formatting were dropped with everything else, so <c>G\"odel</c>
    /// became <c>G"odel</c>, <c>Erd\H{o}s</c> became <c>Erds</c>, and a name in <c>\textbf{}</c> or
    /// <c>\textsc{}</c> vanished, sometimes taking its author's column with it.</para>
    /// </summary>
    private static string Flatten(string s)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '{' || c == '}') { i++; continue; }
            if (c != '\\') { sb.Append(c); i++; continue; }
            if (i + 1 >= s.Length) break;
            var n = s[i + 1];
            if (n == '\\') { sb.Append(' '); i += 2; continue; }
            if (SymbolAccents.TryGetValue(n, out var mark))
            {
                // \'e  \'{e}  \"{o}
                var (letter, next) = AccentTarget(s, i + 2);
                sb.Append(Accent(letter, mark));
                i = next;
                continue;
            }
            if (!char.IsAsciiLetter(n)) { sb.Append(n is ',' or ';' or ' ' or '!' ? ' ' : n); i += 2; continue; }
            var j = i + 1;
            while (j < s.Length && char.IsAsciiLetter(s[j])) j++;
            var name = s[(i + 1)..j];
            if (LetterAccents.TryGetValue(name, out var lmark) && name.Length == 1)
            {
                // \H{o}  \c{c}  \v{s}
                var (letter, next) = AccentTarget(s, j);
                sb.Append(Accent(letter, lmark));
                i = next;
                continue;
            }
            if (Letters.TryGetValue(name, out var special))
            {
                sb.Append(special);
                i = j;
                if (i + 1 < s.Length && s[i] == '{' && s[i + 1] == '}') i += 2;   // \ss{}
                else if (i < s.Length && s[i] == ' ') i++;
                continue;
            }
            if (j < s.Length && s[j] == '*') j++;
            j = SkipOptional(s, j);
            var m = j;
            while (m < s.Length && s[m] == ' ') m++;
            if (m < s.Length && s[m] == '{')
            {
                var close = Math.Min(MatchingBrace(s, m), s.Length);
                if (KeepText.Contains(name) && close > m)
                    sb.Append(Flatten(s[(m + 1)..Math.Min(close, s.Length)]));
                j = Math.Min(close + 1, s.Length);
            }
            i = j;
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // Commands whose argument is the visible text.
    private static readonly HashSet<string> KeepText = new(StringComparer.Ordinal)
    {
        "textbf", "textit", "emph", "textsc", "textsf", "texttt", "textrm", "textup", "textsl", "textmd",
        "textnormal", "mbox", "hbox", "text", "uppercase", "MakeUppercase", "lowercase", "MakeLowercase",
    };

    // \'e style: the accent is a symbol. Values are combining marks.
    private static readonly Dictionary<char, char> SymbolAccents = new()
    {
        ['\''] = '\u0301', ['`'] = '\u0300', ['^'] = '\u0302', ['"'] = '\u0308', ['~'] = '\u0303',
        ['='] = '\u0304', ['.'] = '\u0307',
    };

    // \H{o} style: the accent is a one-letter command.
    private static readonly Dictionary<string, char> LetterAccents = new(StringComparer.Ordinal)
    {
        ["H"] = '\u030B', ["c"] = '\u0327', ["v"] = '\u030C', ["u"] = '\u0306', ["k"] = '\u0328',
        ["r"] = '\u030A', ["d"] = '\u0323', ["b"] = '\u0331',
    };

    private static readonly Dictionary<string, string> Letters = new(StringComparer.Ordinal)
    {
        ["ss"] = "ß", ["o"] = "ø", ["O"] = "Ø", ["ae"] = "æ", ["AE"] = "Æ", ["oe"] = "œ", ["OE"] = "Œ",
        ["aa"] = "å", ["AA"] = "Å", ["l"] = "ł", ["L"] = "Ł", ["i"] = "ı", ["j"] = "ȷ",
    };

    /// <summary>The letter an accent applies to, at <paramref name="i"/>: "e", "{e}", "{\i}". Returns it and where to go on.</summary>
    private static (string Letter, int Next) AccentTarget(string s, int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
        if (i >= s.Length) return ("", i);
        if (s[i] == '{')
        {
            var close = MatchingBrace(s, i);
            var inner = close > i ? Flatten(s[(i + 1)..Math.Min(close, s.Length)]) : "";
            return (inner, Math.Min(close + 1, s.Length));
        }
        if (s[i] == '\\' && i + 1 < s.Length && char.IsAsciiLetter(s[i + 1]))
        {
            var j = i + 1;
            while (j < s.Length && char.IsAsciiLetter(s[j])) j++;
            return (Letters.TryGetValue(s[(i + 1)..j], out var l) ? l : "", j);
        }
        return (s[i].ToString(), i + 1);
    }

    private static string Accent(string letter, char mark)
    {
        if (letter.Length == 0) return "";
        // LaTeX accents a dotless \i or \j (\'{\i}); Unicode composes the accent on a plain i or j.
        var baseChar = letter[0] switch { 'ı' => 'i', 'ȷ' => 'j', var c => c };
        return (baseChar + mark.ToString() + letter[1..]).Normalize(NormalizationForm.FormC);
    }

    private static int SkipOptional(string s, int i)
    {
        if (i < s.Length && s[i] == '[')
        {
            var close = s.IndexOf(']', i);
            if (close > 0) return close + 1;
        }
        return i;
    }

    /// <summary>Index of the brace closing the one at <paramref name="open"/> (or the end of text if unbalanced).</summary>
    private static int MatchingBrace(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return s.Length;
    }

    private static string Tidy(string s)
    {
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        // An accent is a letter plus a combining mark until it is composed: "é", not "e" + U+0301.
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
