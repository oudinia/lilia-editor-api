using System.Globalization;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// What the check adds to TeX's own words, for causes that are common and that TeX hides (the connector test of
/// 17 Nov 2026, items E and F): "Unknown function `of'" took a round trip to read as "load TikZ's positioning
/// library", an <c>@footnote{…}</c> left in a paragraph printed as typed while the check said all was well, and
/// the check never said how long the PDF is, though venues limit pages.
///
/// <para>Only what can be matched reliably: an unknown key or shape that no library defines gets no hint, since it
/// is more likely misspelt, and no hint is better than a wrong one.</para>
/// </summary>
public static partial class CheckHints
{
    // ── page count ────────────────────────────────────────────────────────

    /// <summary>
    /// The pages of the PDF the compile wrote, from its log ("Output written on document.pdf (3 pages, 41234 bytes)."
    /// for pdfTeX and LuaTeX, "(3 pages)." for XeTeX); null when it wrote none.
    /// </summary>
    public static int? Pages(string? log)
    {
        if (string.IsNullOrEmpty(log)) return null;
        // The temporary directory's path makes the line longer than TeX's 79 columns: it is wrapped.
        var m = OutputWritten().Match(LaTeXGlyphScanner.Unwrap(log));
        return m.Success && int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    [GeneratedRegex(@"Output written on .+? \((?<n>\d+) pages?\b")]
    private static partial Regex OutputWritten();

    /// <summary>
    /// Why the check's count can be lower than the exported PDF's, or null when it shouldn't be: the check's single
    /// pass doesn't load the images (each prints as a one-line frame) and has no table of contents or list yet.
    /// </summary>
    public static string? PagesNote(string source)
    {
        var images = source.Contains(@"\IfFileExists{figures/", StringComparison.Ordinal);
        var lists = ContentsLists().IsMatch(source);
        if (!images && !lists) return null;
        var why = images && lists ? "images and the table of contents" : images ? "images" : "the table of contents or lists";
        return $"Counted by the check's compile, which leaves out {why}: the exported PDF can be longer.";
    }

    [GeneratedRegex(@"\\(?:tableofcontents|listoffigures|listoftables|listofalgorithms|lstlistoflistings)\b")]
    private static partial Regex ContentsLists();

    // ── compile errors ────────────────────────────────────────────────────

    private const string PreambleAdvice =
        "Add \\usetikzlibrary{{{0}}} to the document's custom preamble with set_document_settings "
        + "(customPreamble replaces the old one: read it with get_document_settings and keep what is there).";

    /// <summary>A plain hint for the first error in a compile's log (or its error text), or null.</summary>
    public static string? ForError(string? logOrError)
    {
        if (string.IsNullOrEmpty(logOrError)) return null;
        var text = LaTeXGlyphScanner.Unwrap(logOrError);
        var bang = text.StartsWith('!') ? 0 : text.IndexOf("\n!", StringComparison.Ordinal) + 1;
        var error = text[bang..];
        if (error.Length > 1500) error = error[..1500];

        // below=of a, right=1cm of a: without positioning, PGF's maths reads "of" as a function.
        if (OfPlacement().IsMatch(error))
            return Library("positioning", "Placing a node with \u201cof\u201d (below=of a, right=1cm of a)");
        // TikZ names the library itself (calc, patterns); the hint says where it goes.
        if (NeedsLibrary().Match(error) is { Success: true } named)
            return Library(named.Groups["lib"].Value.Trim(), "This drawing");
        if (ArrowTip().Match(error) is { Success: true } tip && ArrowLibrary(tip.Groups["tip"].Value) is { } arrows)
            return Library(arrows, $"The arrow tip {tip.Groups["tip"].Value}");
        if (error.Contains("You need to load a decoration library", StringComparison.Ordinal))
            return Decoration(DecorationName().Match(error) is { Success: true } d ? d.Groups["name"].Value : null);
        if (UnknownKey().Match(error) is { Success: true } key)
        {
            var name = key.Groups["key"].Value.Trim();
            if (name == "decoration")
                return Decoration(key.Groups["value"].Success ? key.Groups["value"].Value.Split(',')[0] : null);
            if (KeyLibrary(name) is { } lib)
                return Library(lib, $"The TikZ option \u201c{name}\u201d");
        }
        if (UnknownShape().Match(error) is { Success: true } shape && ShapeLibrary(shape.Groups["shape"].Value) is { } shapes)
            return Library(shapes, $"The shape {shape.Groups["shape"].Value}");
        return null;
    }

    private static string Library(string lib, string what) =>
        $"{what} needs the TikZ library {lib}. " + string.Format(CultureInfo.InvariantCulture, PreambleAdvice, lib);

    private static string Decoration(string? name)
    {
        var lib = name is null ? null : DecorationLibraries.FirstOrDefault(d => d.Names.Contains(name.Trim())).Library;
        if (lib is not null) return Library(lib, $"The decoration {name!.Trim()}");
        return "Decorations need a TikZ decorations library: decorations.pathreplacing (brace, ticks, waves), "
            + "decorations.pathmorphing (zigzag, snake, coil, random steps), decorations.markings (markings) or "
            + "decorations.text (text along path). "
            + string.Format(CultureInfo.InvariantCulture, PreambleAdvice, "decorations.pathreplacing");
    }

    [GeneratedRegex(@"PGF Math Error: Unknown (?:function `of'|operator `o' or `of')")]
    private static partial Regex OfPlacement();

    [GeneratedRegex(@"You need to say \\usetikzlibrary\{(?<lib>[^{}]+)\}")]
    private static partial Regex NeedsLibrary();

    [GeneratedRegex(@"Unknown arrow tip kind '(?<tip>[^'\[]+)")]
    private static partial Regex ArrowTip();

    [GeneratedRegex(@"I do not know the key '/tikz/(?<key>[^']+)'(?:, to which you passed '(?<value>[^']*)')?")]
    private static partial Regex UnknownKey();

    [GeneratedRegex(@"Unknown shape ``(?<shape>.+?)\.''")]
    private static partial Regex UnknownShape();

    [GeneratedRegex(@"decoration\s*=\s*\{?\s*(?<name>[A-Za-z][A-Za-z ]*?)\s*[,}\]]")]
    private static partial Regex DecorationName();

    // arrows.meta's tips are capitalised; the old arrows library's are not (stealth and latex without ' are built in).
    private static readonly HashSet<string> MetaTips = new(StringComparer.Ordinal)
    {
        "Arc Barb", "Bar", "Bracket", "Butt Cap", "Circle", "Classical TikZ Rightarrow", "Computer Modern Rightarrow",
        "Diamond", "Ellipse", "Fast Round", "Fast Triangle", "Hooks", "Implies", "Kite", "Latex", "Parenthesis", "Rays",
        "Rectangle", "Round Cap", "Square", "Stealth", "Straight Barb", "Tee Barb", "Triangle", "Triangle Cap", "Turned Square",
    };

    [GeneratedRegex(@"^(?:(?:open )?(?:triangle|angle) (?:45|60|90)|(?:stealth|latex)'|(?:open )?(?:diamond|square|circle)|serif cm)(?: reversed)?$")]
    private static partial Regex OldArrowTip();

    private static string? ArrowLibrary(string tip)
    {
        tip = tip.Trim();
        return MetaTips.Contains(tip) ? "arrows.meta" : OldArrowTip().IsMatch(tip) ? "arrows" : null;
    }

    // Longer names first, so "ellipse split" is not read as "ellipse" and "cloud callout" not as "cloud".
    private static readonly (string Name, string Library)[] Shapes = new (string, string)[]
    {
        ("ellipse", "shapes.geometric"), ("diamond", "shapes.geometric"), ("trapezium", "shapes.geometric"),
        ("semicircle", "shapes.geometric"), ("regular polygon", "shapes.geometric"), ("star", "shapes.geometric"),
        ("isosceles triangle", "shapes.geometric"), ("kite", "shapes.geometric"), ("dart", "shapes.geometric"),
        ("circular sector", "shapes.geometric"), ("cylinder", "shapes.geometric"),
        ("cloud", "shapes.symbols"), ("starburst", "shapes.symbols"), ("signal", "shapes.symbols"), ("tape", "shapes.symbols"),
        ("magnifying glass", "shapes.symbols"), ("forbidden sign", "shapes.symbols"), ("correct forbidden sign", "shapes.symbols"),
        ("single arrow", "shapes.arrows"), ("double arrow", "shapes.arrows"), ("arrow box", "shapes.arrows"),
        ("rectangle split", "shapes.multipart"), ("circle split", "shapes.multipart"), ("circle solidus", "shapes.multipart"),
        ("ellipse split", "shapes.multipart"),
        ("rounded rectangle", "shapes.misc"), ("chamfered rectangle", "shapes.misc"), ("cross out", "shapes.misc"), ("strike out", "shapes.misc"),
        ("rectangle callout", "shapes.callouts"), ("ellipse callout", "shapes.callouts"), ("cloud callout", "shapes.callouts"),
    }.OrderByDescending(s => s.Item1.Length).ToArray();

    private static readonly (string Name, string Library)[] Keys = new (string, string)[]
    {
        ("on grid", "positioning"), ("base left", "positioning"), ("base right", "positioning"), ("mid left", "positioning"), ("mid right", "positioning"),
        ("fit", "fit"),
        ("matrix of nodes", "matrix"), ("matrix of math nodes", "matrix"),
        ("drop shadow", "shadows"), ("circular drop shadow", "shadows"), ("copy shadow", "shadows"), ("double copy shadow", "shadows"), ("general shadow", "shadows"),
        ("on background layer", "backgrounds"), ("show background rectangle", "backgrounds"), ("show background grid", "backgrounds"),
        ("background rectangle", "backgrounds"), ("framed", "backgrounds"), ("gridded", "backgrounds"),
        ("mindmap", "mindmap"), ("concept", "mindmap"),
        ("state", "automata"), ("accepting", "automata"), ("initial", "automata"),
        ("name path", "intersections"), ("name intersections", "intersections"),
        ("circle through", "through"),
        ("spy using outlines", "spy"), ("spy using overlays", "spy"),
    }.OrderByDescending(s => s.Item1.Length).ToArray();

    private static string? Prefixed((string Name, string Library)[] table, string key) =>
        table.FirstOrDefault(t => key == t.Name || key.StartsWith(t.Name + " ", StringComparison.Ordinal)).Library;

    private static string? ShapeLibrary(string shape) => Prefixed(Shapes, shape.Trim());

    private static string? KeyLibrary(string key)
    {
        // node["label" above]: the quotes library reads a label in quotes.
        if (key.StartsWith('"')) return "quotes";
        // shape=diamond goes through the shape's own key, and its options too (regular polygon sides=5).
        return Prefixed(Keys, key) ?? Prefixed(Shapes, key);
    }

    private static readonly (string Library, string[] Names)[] DecorationLibraries =
    [
        ("decorations.pathreplacing", ["brace", "ticks", "border", "waves", "expanding waves", "show path construction"]),
        ("decorations.pathmorphing", ["zigzag", "straight zigzag", "saw", "snake", "coil", "bumps", "random steps", "lineto", "moveto", "curveto"]),
        ("decorations.markings", ["markings"]),
        ("decorations.text", ["text along path", "text effects along path"]),
        ("decorations.shapes", ["crosses", "triangles", "shape backgrounds"]),
        ("decorations.footprints", ["footprints"]),
    ];

    // ── Lilia marks printed as typed ──────────────────────────────────────

    /// <summary>A Lilia mark the emitter left in the text, so the PDF prints it as typed: the line, the mark, what to write.</summary>
    public sealed record LiteralMark(int Line, string Mark, string Message);

    // A mark is "@name" right before its bracket or brace, not inside a word, an address or a command (\@footnotetext).
    [GeneratedRegex(@"(?<![\\\p{L}\p{N}@/:_-])@(?<mark>footnote|cite\p{L}*|ref\p{L}*|label|heading|paragraph|equation|figure|image|table|theorem|lemma|definition|proof|list|code|abstract|quote|blockquote|callout|algorithm|bibliography|embed)(?=[\[{(])", RegexOptions.IgnoreCase)]
    private static partial Regex Mark();

    [GeneratedRegex(@"\\begin\{(?<env>lstlisting|verbatim\*?|Verbatim\*?|minted|comment|filecontents\*?|alltt)\}")]
    private static partial Regex VerbatimBegin();

    // \verb|…| and \lstinline{…} or \lstinline|…|: code typed in the text prints as typed on purpose.
    [GeneratedRegex(@"\\(?:verb\*?|lstinline(?:\[[^\]]*\])?)(?:(?<d>[^\sA-Za-z{]).*?\k<d>|\{[^{}]*\})")]
    private static partial Regex InlineCode();

    /// <summary>
    /// The Lilia marks the emitted LaTeX prints as typed, by line: in the body only, outside code listings,
    /// verbatim text, inline code and comments. <c>@cite{…}</c> and <c>@ref{…}</c> are turned into LaTeX before this,
    /// so whatever is left is in the PDF as typed.
    /// </summary>
    public static IReadOnlyList<LiteralMark> LiteralMarks(string source)
    {
        var found = new List<LiteralMark>();
        var lines = source.Split('\n');
        var begin = Array.FindIndex(lines, l => l.Contains(@"\begin{document}", StringComparison.Ordinal));
        string? verbatim = null;
        for (var i = begin < 0 ? 0 : begin + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (verbatim is not null)
            {
                if (line.Contains($@"\end{{{verbatim}}}", StringComparison.Ordinal)) verbatim = null;
                continue;
            }
            if (VerbatimBegin().Match(line) is { Success: true } v)
            {
                if (!line.Contains($@"\end{{{v.Groups["env"].Value}}}", StringComparison.Ordinal)) verbatim = v.Groups["env"].Value;
                continue;
            }
            var text = InlineCode().Replace(Uncommented(line), "");
            foreach (Match m in Mark().Matches(text))
                found.Add(new LiteralMark(i + 1, "@" + m.Groups["mark"].Value, MarkMessage(m.Groups["mark"].Value)));
        }
        return found;
    }

    /// <summary>The line up to its first unescaped %.</summary>
    private static string Uncommented(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '%') return line[..i];
        }
        return line;
    }

    private static string MarkMessage(string mark)
    {
        var m = mark.ToLowerInvariant();
        if (m == "footnote")
            return "@footnote is not written inside text, so the PDF prints it as typed. Write \\footnote{\u2026} in the text, or add a footnote block after the paragraph.";
        if (m.StartsWith("cite", StringComparison.Ordinal))
            return $"@{mark} is not a Lilia mark, so the PDF prints it as typed. Cite with @cite{{key}} (several: @cite{{a,b}}) or \\cite{{key}}.";
        if (m.StartsWith("ref", StringComparison.Ordinal) || m == "label")
            return $"@{mark} is not a Lilia mark, so the PDF prints it as typed. Refer with @ref{{label}} or Kind~(prefix:label), e.g. Figure~(fig:plot); a block's label goes in its label field.";
        return $"@{mark} starts a block in LML; inside text the PDF prints it as typed. Make it its own block (add_block, or apply_lml for the whole document).";
    }
}
