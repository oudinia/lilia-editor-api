using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// The accessible PDF (PROD-40, 1 Nov 2026): the export's LaTeX made into a tagged PDF/UA-2 source,
/// compiled with LuaLaTeX. Screen readers get the structure (headings, lists, tables with header
/// cells, footnotes, figures with their alt text) and the maths as MathML.
///
/// <para>The normal export is not touched: this runs on its finished main.tex. LaTeX's tagging code
/// (\DocumentMetadata, TeX Live 2026) refuses or mis-tags a dozen packages the shared preamble always
/// loads (LaTeX's tagging-status list). Tried on a real export, 1 Nov:</para>
/// <list type="bullet">
/// <item>those packages are dropped when the document does not use them, which is nearly always;</item>
/// <item>what the body does use is rewritten to what tags: a code block's listing becomes verbatim
/// (its caption a line above it), highlight a yellow box per word, strike and underline lua-ul,
/// [H] floats [!htbp];</item>
/// <item>the kernel defines theorem* with every \newtheorem, so our own \newtheorem* is skipped;</item>
/// <item>a theorem or proof straight after a displayed equation stopped the compile ("automatic
/// begin and end para hooks differ"): each starts with \par, as it starts a paragraph anyway;</item>
/// <item>inputenc, fontenc and lmodern go (LuaLaTeX sets Latin Modern in Unicode itself).</item>
/// </list>
/// <para>Not lua-ul's \highLight: it needs luacolor, and luacolor beside coloured links wrote an
/// invalid PDF ("Unknown operator 'rgb'").</para>
/// </summary>
public static class TaggedPdf
{
    /// <summary>The engine a tagged PDF is compiled with: MathML for the maths needs LuaTeX.</summary>
    public const string Engine = "lualatex";

    /// <param name="Latex">The source to compile.</param>
    /// <param name="Dropped">Packages left out because the document does not use them.</param>
    /// <param name="Untaggable">Packages kept because the document uses them, which LaTeX does not
    /// tag yet (multirow, for example): the PDF is tagged, those parts less well.</param>
    public sealed record Result(string Latex, IReadOnlyList<string> Dropped, IReadOnlyList<string> Untaggable);

    // LaTeX's tagging-status list: currently incompatible, and what in a document needs each one.
    // A package not listed here is compatible or partly so, and stays.
    private static readonly (string Package, Regex Use)[] Incompatible =
    [
        ("cancel", new(@"\\[bx]?cancel\b|\\cancelto\b")),
        ("float", new(@"\\restylefloat|\\newfloat|\\floatstyle|\\begin\{(?:figure|table)\*?\}\[H\]")),
        ("caption", new(@"\\captionsetup|\\captionof|\\caption\*|\\DeclareCaption")),
        ("subcaption", new(@"\{subfigure\}|\{subtable\}|\\subcaption|\\subcaptionbox")),
        ("multirow", new(@"\\multirow")),
        ("threeparttablex", new(@"ThreePartTable|\{tablenotes\}|\\tnote\b")),
        ("threeparttable", new(@"\{threeparttable\}|\{tablenotes\}|\\tnote\b")),
        ("needspace", new(@"\\[Nn]eedspace")),
        ("listings", new(@"\{lstlisting\}|\\lstinline|\\lstinputlisting|\\lstnewenvironment")),
        ("algorithm", new(@"\{algorithm\}")),
        ("algorithmic", new(@"\{algorithmic\}")),
        ("soul", new(@"\\(?:hl|st|ul|so|caps|sethlcolor)\b")),
        ("ulem", new(@"\\(?:sout|uline|uuline|uwave|xout|dashuline|dotuline)\b")),
    ];

    // LuaLaTeX sets Latin Modern in Unicode by itself; these are pdfLaTeX's.
    private static readonly HashSet<string> PdflatexOnly = new(StringComparer.Ordinal) { "inputenc", "fontenc", "lmodern" };

    private static readonly Regex UsePackageLine = new(@"^\\usepackage(?<opt>\[[^\]]*\])?\{(?<names>[^}]*)\}(?<tail>.*)$", RegexOptions.Compiled);
    private static readonly Regex Comment = new(@"(?<!\\)%.*", RegexOptions.Compiled);
    private static readonly Regex StarredTheorem = new(@"^\\newtheorem\*\{(?<env>[A-Za-z]+\*)\}(?<rest>.*)$", RegexOptions.Compiled);
    private static readonly Regex Listing = new(@"\\begin\{lstlisting\}(?<opt>\[[^\]\n]*\])?[ \t]*\n(?<code>.*?)\\end\{lstlisting\}", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ListingCaption = new(@"caption=\{(?<c>[^{}]*)\}", RegexOptions.Compiled);
    private static readonly Regex HereFloat = new(@"\\begin\{(?<env>figure\*?|table\*?)\}\[H\]", RegexOptions.Compiled);
    private static readonly Regex TheoremDefinition = new(@"\\newtheorem\*?\s*\{(?<env>[A-Za-z]+\*?)\}", RegexOptions.Compiled);

    /// <summary>
    /// The export's main.tex as a tagged PDF/UA-2 source. <paramref name="language"/> is the
    /// document's (BCP 47: "fr", "en-GB"), <paramref name="title"/> its title: PDF/UA needs both.
    /// </summary>
    public static Result Apply(string latex, string? language, string? title)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();
        latex = Regex.Replace(latex, @"^\\DocumentMetadata\{.*\}[ \t]*\n", "", RegexOptions.Multiline);
        var at = latex.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException("The LaTeX has no \\begin{document}.");
        var preamble = latex[..at];
        var body = latex[at..];

        // The body first, so what it no longer uses can go from the preamble.
        body = Listing.Replace(body, m =>
        {
            var caption = ListingCaption.Match(m.Groups["opt"].Value);
            var head = caption.Success ? $"\\par\\noindent\\textit{{{caption.Groups["c"].Value}}}\\par\n" : "";
            return head + "\\begin{verbatim}\n" + m.Groups["code"].Value + "\\end{verbatim}";
        });
        body = HereFloat.Replace(body, m => $"\\begin{{{m.Groups["env"].Value}}}[!htbp]");
        body = Highlight(body);
        var luaUl = false;
        foreach (var (from, to) in new[] { ("st", "strikeThrough"), ("sout", "strikeThrough"), ("ul", "underLine"), ("uline", "underLine") })
        {
            var next = Regex.Replace(body, $@"\\{from}(?=\s*\{{)", "\\" + to);
            luaUl |= next != body;
            body = next;
        }

        var theorems = TheoremDefinition.Matches(preamble).Select(m => m.Groups["env"].Value).Append("proof").Distinct();
        body = Regex.Replace(body, $@"(?<!\\par)\\begin\{{(?:{string.Join("|", theorems.Select(Regex.Escape))})\}}", m => "\\par" + m.Value);

        var lines = preamble.Split('\n').ToList();
        // What the document uses: its body, and every preamble line that is not a \usepackage
        // (the layout, the theme, the custom preamble), comments aside.
        var uses = Comment.Replace(body + "\n" + string.Join("\n", lines.Where(l => !UsePackageLine.IsMatch(l))), "");
        var dropped = new List<string>();
        var kept = new List<string>();
        var output = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var m = UsePackageLine.Match(line);
            if (m.Success)
            {
                var names = m.Groups["names"].Value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
                var keep = names.Where(n => !PdflatexOnly.Contains(n) && (Use(n) is not { } use || use.IsMatch(uses))).ToList();
                dropped.AddRange(names.Except(keep));
                kept.AddRange(keep.Where(n => Use(n) is not null));
                if (keep.Count == 0) continue;
                line = $"\\usepackage{m.Groups["opt"].Value}{{{string.Join(",", keep)}}}{m.Groups["tail"].Value}";
            }
            // A dropped package's settings: \lstset{…}, \lstdefinelanguage{…}{…}, \normalem.
            else if (SettingOf(line) is { } owner && dropped.Contains(owner))
            {
                i = EndOfGroup(lines, i);
                continue;
            }
            else if (StarredTheorem.Match(line) is { Success: true } t)
            {
                var env = t.Groups["env"].Value;
                line = $"\\ifcsname {env}\\endcsname\\else\\newtheorem*{{{env}}}{t.Groups["rest"].Value}\\fi";
            }
            output.Add(line);
        }

        var sb = new StringBuilder();
        sb.Append("\\DocumentMetadata{lang=").Append(lang)
          .Append(", pdfversion=2.0, pdfstandard=ua-2, tagging=on, tagging-setup={math/setup=mathml-SE}}\n");
        sb.Append(string.Join("\n", output).TrimEnd('\n')).Append('\n');
        if (luaUl) sb.Append("% Strike and underline that LuaLaTeX can break across lines (the accessible PDF)\n\\usepackage{lua-ul}\n");
        // PDF/UA: the title in the file, shown as the window's title.
        sb.Append("\\ifdefined\\hypersetup\\hypersetup{pdftitle={")
          .Append(LatexText.Escape(title ?? "")).Append("}, pdfdisplaydoctitle=true}\\fi\n");
        sb.Append(body);
        return new Result(sb.ToString(), dropped.Distinct().ToList(), kept.Distinct().ToList());
    }

    private static Regex? Use(string package) =>
        Incompatible.FirstOrDefault(p => p.Package == package).Use;

    private static string? SettingOf(string line) =>
        line.StartsWith(@"\lstset", StringComparison.Ordinal) || line.StartsWith(@"\lstdefinelanguage", StringComparison.Ordinal) ? "listings"
        : line.Trim() == @"\normalem" ? "ulem"
        : null;

    // The line where the setting that starts on line i ends: where its braces balance again.
    private static int EndOfGroup(List<string> lines, int i)
    {
        var depth = 0;
        for (var j = i; j < lines.Count; j++)
        {
            foreach (var c in Comment.Replace(lines[j], ""))
                depth += c == '{' ? 1 : c == '}' ? -1 : 0;
            if (depth <= 0) return j;
        }
        return lines.Count - 1;
    }

    // \hl{words}: a yellow box per word, so a long highlight still breaks between words. A highlight
    // holding commands is one box.
    private static string Highlight(string body) =>
        Regex.Replace(body, @"\\hl\{(?<t>[^{}]*)\}", m =>
        {
            var text = m.Groups["t"].Value;
            if (text.Contains('\\')) return $"\\colorbox{{yellow}}{{{text}}}";
            return string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => $"\\colorbox{{yellow}}{{{w}}}"));
        });

    /// <summary>
    /// A figure block's LaTeX with its description on each image and TikZ picture (alt=), which the
    /// tagging code writes as the figure's alternate text. Null or blank: unchanged.
    /// </summary>
    public static string WithAlt(string figureLatex, string? alt)
    {
        if (string.IsNullOrWhiteSpace(alt)) return figureLatex;
        var key = "alt={" + LatexText.Escape(alt.Trim()) + "}";
        figureLatex = Regex.Replace(figureLatex, @"\\includegraphics(?:\[(?<o>[^\]]*)\])?(?=\s*\{)",
            m => m.Groups["o"].Success && m.Groups["o"].Value.Length > 0 ? $"\\includegraphics[{key},{m.Groups["o"].Value}]" : $"\\includegraphics[{key}]");
        return Regex.Replace(figureLatex, @"\\begin\{(?<env>tikzpicture|tikzcd)\}(?:(?<sp>\s*)\[(?<o>[^\]]*)\])?",
            m => m.Groups["o"].Success ? $"\\begin{{{m.Groups["env"].Value}}}{m.Groups["sp"].Value}[{key},{m.Groups["o"].Value}]" : $"\\begin{{{m.Groups["env"].Value}}}[{key}]");
    }

    /// <summary>
    /// A figure's description: its alt text when that describes it (not a file name, an upload's
    /// default), else its caption. Null when it has neither.
    /// </summary>
    public static string? AltOf(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return null;
        var alt = content.TryGetProperty("alt", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (Publishers.PublisherCompliance.IsDescription(alt ?? "")) return alt!.Trim();
        var caption = content.TryGetProperty("caption", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        return string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
    }
}
