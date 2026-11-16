using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// Emoji in a LaTeX PDF, drawn by the twemojis package (Twitter's emoji as pictures; pdflatex, XeLaTeX and LuaLaTeX).
/// pdflatex stopped at any emoji the shared list doesn't map ("Unicode character 🚀 not set up"; the document sweep,
/// E-015, 16 Nov 2026), and the fontspec engines printed nothing in its place.
///
/// <para>Each emoji in the body becomes <c>\texttwemoji{1f680}</c>, matched to the longest sequence the package knows, so
/// a skin tone (👍🏽) or a joined emoji (👨‍💻) stays one picture. Left alone: code listings and verbatim text, characters
/// the shared list already maps (✅ ★ …), and anything the installed package doesn't know, so nothing that printed before
/// prints differently.</para>
/// </summary>
public static partial class EmojiLatex
{
    /// <summary>The sequences the installed twemojis.sty defines, read once; empty (the step does nothing) without it.</summary>
    public static IReadOnlySet<string> Installed => installed.Value;

    private static readonly Lazy<IReadOnlySet<string>> installed = new(ReadInstalled);

    private static IReadOnlySet<string> ReadInstalled()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("kpsewhich", "twemojis.sty") { RedirectStandardOutput = true, UseShellExecute = false })!;
            var path = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(10_000);
            return path.Length > 0 && File.Exists(path) ? Parse(File.ReadAllText(path)) : new HashSet<string>();
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    /// <summary>The code-point sequences in a twemojis.sty (its names, such as "rocket", are skipped).</summary>
    public static IReadOnlySet<string> Parse(string sty) =>
        Defined().Matches(sty).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\\defineTwemoji\{\d+\}\{([0-9a-f]+(?:-[0-9a-f]+)*)\}")]
    private static partial Regex Defined();

    [GeneratedRegex(@"\\begin\{(lstlisting|verbatim|Verbatim|minted)\}.*?\\end\{\1\}", RegexOptions.Singleline)]
    private static partial Regex Literal();

    /// <summary>
    /// The document with its emoji drawn by twemojis, and the package loaded before \begin{document}; unchanged when it
    /// has none the package knows. <paramref name="mapped"/>: code points the preamble already maps.
    /// </summary>
    public static string Apply(string latex, IReadOnlySet<string> known, IReadOnlySet<int> mapped)
    {
        if (known.Count == 0) return latex;
        var at = latex.IndexOf(Begin, StringComparison.Ordinal);
        if (at < 0) return latex;
        var body = DrawText(latex[(at + Begin.Length)..], known, mapped, out var replaced);
        return replaced ? latex[..at] + Package + Begin + body : latex;
    }

    private const string Begin = @"\begin{document}";

    /// <summary>The line that loads the package, for a preamble.</summary>
    public const string Package = "% [lilia] Emoji as pictures (twemojis)\n\\usepackage{twemojis}\n";

    /// <summary>
    /// A piece of LaTeX body with its emoji drawn (code listings and verbatim text left alone); <paramref name="replaced"/>
    /// says whether the package is needed. For a project's chapter files, whose preamble is elsewhere.
    /// </summary>
    public static string DrawText(string body, IReadOnlySet<string> known, IReadOnlySet<int> mapped, out bool replaced)
    {
        replaced = false;
        if (known.Count == 0 || (!body.Any(char.IsSurrogate) && !body.Contains('\uFE0F'))) return body;
        var sb = new StringBuilder(body.Length + 64);
        var last = 0;
        foreach (Match m in Literal().Matches(body))
        {
            sb.Append(Draw(body[last..m.Index], known, mapped, ref replaced)).Append(m.Value);
            last = m.Index + m.Length;
        }
        sb.Append(Draw(body[last..], known, mapped, ref replaced));
        return replaced ? sb.ToString() : body;
    }

    /// <summary>The code points the shared preamble's list maps (<see cref="LaTeXPreamble.UnicodeShims"/>): left to it.</summary>
    public static IReadOnlySet<int> SharedListMapped { get; } = Regex
        .Matches(LaTeXPreamble.UnicodeShims, @"\\newunicodechar\{(.+?)\}\{")
        .Select(m => m.Groups[1].Value)
        .Where(c => c.Length > 0)
        .Select(c => char.ConvertToUtf32(c, 0))
        .ToHashSet();

    private static string Draw(string text, IReadOnlySet<string> known, IReadOnlySet<int> mapped, ref bool replaced)
    {
        var cps = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) cps.Add(char.ConvertToUtf32(text, i));
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < cps.Count;)
        {
            if (IsStart(cps, i, mapped) && Longest(cps, i, known) is { } match)
            {
                sb.Append(@"\texttwemoji{").Append(match.Key).Append('}');
                replaced = true;
                i = match.End;
                continue;
            }
            sb.Append(char.ConvertFromUtf32(cps[i]));
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Text holding an emoji, by the same reading <see cref="Apply"/> uses. Automatic sends such a document to LaTeX:
    /// Typst on the server has no emoji font, so 🚀 printed as an empty box (16 Nov 2026).
    /// </summary>
    public static bool Contains(string text)
    {
        if (!text.Any(char.IsSurrogate) && !text.Contains('\uFE0F')) return false;
        var cps = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) cps.Add(char.ConvertToUtf32(text, i));
        for (var i = 0; i < cps.Count; i++)
            if (IsStart(cps, i, NoneMapped)) return true;
        return false;
    }

    private static readonly IReadOnlySet<int> NoneMapped = new HashSet<int>();

    /// <summary>An emoji starts here: a pictograph (U+1F000 up), or a symbol from the older blocks asking for its emoji form (U+FE0F).</summary>
    private static bool IsStart(List<int> cps, int i, IReadOnlySet<int> mapped)
    {
        var cp = cps[i];
        if (mapped.Contains(cp)) return false;
        if (cp is >= 0x1F000 and <= 0x1FAFF) return true;
        var older = cp is >= 0x2300 and <= 0x23FF or >= 0x2600 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF;
        return older && i + 1 < cps.Count && cps[i + 1] == 0xFE0F;
    }

    /// <summary>The longest sequence from <paramref name="i"/> that the package knows, with or without its U+FE0F selectors.</summary>
    private static (string Key, int End)? Longest(List<int> cps, int i, IReadOnlySet<string> known)
    {
        var j = i + 1;
        while (j < cps.Count)
        {
            var c = cps[j];
            if (c is 0xFE0F or 0x20E3 or >= 0x1F3FB and <= 0x1F3FF or >= 0xE0020 and <= 0xE007F) { j++; continue; }
            if (c == 0x200D && j + 1 < cps.Count) { j += 2; continue; }
            if (j == i + 1 && cps[i] is >= 0x1F1E6 and <= 0x1F1FF && c is >= 0x1F1E6 and <= 0x1F1FF) { j++; continue; }
            break;
        }
        for (var end = j; end > i; end--)
        {
            var seq = cps.GetRange(i, end - i);
            var full = Key(seq);
            if (known.Contains(full)) return (full, end);
            var bare = Key(seq.Where(c => c != 0xFE0F));
            if (bare.Length > 0 && known.Contains(bare)) return (bare, end);
        }
        return null;
    }

    private static string Key(IEnumerable<int> cps) => string.Join('-', cps.Select(c => c.ToString("x", CultureInfo.InvariantCulture)));
}
