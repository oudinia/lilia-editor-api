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
/// <item>inputenc, fontenc and lmodern go (LuaLaTeX sets Latin Modern in Unicode itself);</item>
/// <item>microtype keeps its font expansion but not protrusion, which beside the MathML tagging
/// stopped LuaTeX on an equation ("trying to delete an attribute reference of a non attribute node").</item>
/// </list>
/// <para>Not lua-ul's \highLight: it needs luacolor, and luacolor beside coloured links wrote an
/// invalid PDF ("Unknown operator 'rgb'").</para>
/// <para>The publisher classes (launch-bot #257, 19 Nov 2026), each tried on a full export (title, authors,
/// abstract, sections, an equation, a theorem and proof, a table, a figure with alt text, an algorithm, a
/// citation). Not one compiled tagged:</para>
/// <list type="bullet">
/// <item>an algorithm block stopped every class: float.sty's algorithm float is unknown to the tagging code
/// ("tag \l__tag_name_float/algorithm_tl is not known", then a Lua error), and once named, its own begin
/// and end left the paragraph hooks unbalanced. It is now a kernel float the tagging code knows;</item>
/// <item>the tagging code brings its own proof and \qed. elsarticle, llncs and SIAM define theirs again
/// ("Command \proof already defined", "\qed already defined"), and acmart's proof (a trivlist) left the
/// paragraph hooks unbalanced. Under a publisher class, LaTeX's tagged proof is set aside before the
/// class and put back at \begin{document}; the class's \qed stays;</item>
/// <item>llncs: its footnote text hides \hb@xt@ in \llap, which the footnote tagging cannot patch
/// (an undefined \ERROR at \begin{document}), and its \section and \subsection set "\rightskip=…" in
/// the heading style, which the heading tagging reads as a key ("Misplaced '='"). Both are written
/// again, the same on the page;</item>
/// <item>elsarticle: its \@author is a command with arguments, which the tagging code read as the PDF's author
/// on the last page and stopped; the author is left out of the PDF's properties (the title stays);</item>
/// <item>IEEEtran: its bibliography's \bibitem wrapper left the paragraph hooks unbalanced once the references
/// printed; the same list, without the wrapper;</item>
/// <item>then veraPDF (PDF/UA-2) failed two: acmart's sigconf authors are paragraphs in boxes inside a paragraph
/// ("&lt;P&gt; shall not contain &lt;P&gt;"), and SIAM's ntheorem theorems never closed their list item, so the rest of the
/// paper was tagged inside it. The author boxes' lines are now part of their line's paragraph, and SIAM's
/// theorems are LaTeX's own \newtheorem;</item>
/// <item>revtex4-2 (APS, AIP) is refused (<see cref="WhyUnavailable"/>): with \DocumentMetadata alone,
/// tagging off, every tabular stops ("You can't use \halign in math mode"), as REVTeX's own array code
/// no longer recognises the kernel's.</item>
/// </list>
/// </summary>
public static class TaggedPdf
{
    /// <summary>The engine a tagged PDF is compiled with: MathML for the maths needs LuaTeX.</summary>
    public const string Engine = "lualatex";

    /// <summary>
    /// Seconds per LuaLaTeX pass. The MathML makes a tagged pass about ten times a pdfLaTeX one
    /// (a 19-page paper with 9 equations: 39 s against 3.8 s, 1 Nov); the usual 60 stopped it.
    /// </summary>
    public const int PassTimeoutSeconds = 150;

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
    // algorithm too (launch-bot #257, 19 Nov 2026): a kernel float now, which has no [H].
    private static readonly Regex HereFloat = new(@"\\begin\{(?<env>figure\*?|table\*?|algorithm)\}\[H\]", RegexOptions.Compiled);
    private static readonly Regex DocumentClass = new(@"\\documentclass\s*(?:\[[^\]]*\])?\s*\{(?<cls>[^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Why this LaTeX can't be made an accessible PDF yet, for the person; null when it can. Only its class
    /// decides (launch-bot #257, 19 Nov 2026).
    /// </summary>
    public static string? WhyUnavailable(string latex)
    {
        var cls = ClassOf(latex);
        return cls is "revtex4-2" or "revtex4-1" or "revtex4"
            ? "An accessible PDF isn't available yet for the APS and AIP format (REVTeX): LaTeX's tagging code can't set its tables yet. Download the normal PDF, or choose another format for the accessible copy."
            : null;
    }

    private static string? ClassOf(string latex) =>
        DocumentClass.Match(Comment.Replace(latex, "")) is { Success: true } m ? m.Groups["cls"].Value.Trim() : null;

    // LaTeX's own tagged proof, set aside before a publisher class defines its own proof or \qed, and put back at
    // \begin{document} (launch-bot #257, 19 Nov 2026). The class's \qed is kept; LaTeX's only when it has none.
    private const string KeepTaggedProof = """
        \makeatletter
        \NewEnvironmentCopy{LiliaTaggedProof}{proof}\let\proof\@undefined\let\endproof\@undefined
        \let\LiliaTaggedQed\qed\let\qed\@undefined
        \AtBeginDocument{\let\proof\@undefined\let\endproof\@undefined\NewEnvironmentCopy{proof}{LiliaTaggedProof}\ifdefined\qed\else\let\qed\LiliaTaggedQed\fi}
        \makeatother
        """;

    // float.sty's algorithm (algorithm.sty, or SIAM's class) as a kernel float, which the tagging code tags as it
    // tags a figure (launch-bot #257, 19 Nov 2026). Its captions go through the kernel's \@caption.
    private const string AlgorithmAsKernelFloat = """
        \makeatletter
        \ifdefined\c@algorithm\tagpdfsetup{float/new=algorithm}\renewenvironment{algorithm}{\@float{algorithm}}{\end@float}\expandafter\let\csname @float@c@algorithm\endcsname\@caption\fi
        \makeatother
        """;

    // llncs's footnote text and headings, the same on the page, in a shape the tagging code reads (launch-bot #257).
    private const string LlncsTaggable = """
        \makeatletter
        \long\def\@makefntext#1{\parindent\fnindent\leftskip\fnindent\noindent\hb@xt@\z@{\hss\hb@xt@1em{\hss\@makefnmark\ }}\ignorespaces#1}
        \renewcommand\section{\@startsection{section}{1}{\z@}{-18\p@ \@plus -4\p@ \@minus -4\p@}{12\p@ \@plus 4\p@ \@minus 4\p@}{\normalfont\large\bfseries\boldmath\rightskip\z@ \@plus 8em\pretolerance10000 }}
        \renewcommand\subsection{\@startsection{subsection}{2}{\z@}{-18\p@ \@plus -4\p@ \@minus -4\p@}{8\p@ \@plus 4\p@ \@minus 4\p@}{\normalfont\normalsize\bfseries\boldmath\rightskip\z@ \@plus 8em\pretolerance10000 }}
        \makeatother
        """;

    // elsarticle's \@author is its own command with arguments, not the names: the tagging code read it as the PDF's
    // author on the last page and stopped ("Use of \__text_expand_space:w doesn't match"). Left out instead: PDF/UA
    // needs the title, not the author (launch-bot #257, 19 Nov 2026).
    private const string ElsarticleTaggable = """
        \ExplSyntaxOn
        \AddToHook{cmd/maketitle/after}{\tl_gclear:N \g__tag_title_author_tl}
        \ExplSyntaxOff
        """;

    // IEEEtran's bibliography wraps \bibitem in a macro of its own (for \IEEEtriggeratref), which left the tagging
    // code's paragraphs unbalanced on the pass that prints the references. The same look, without the wrapper
    // (launch-bot #257, 19 Nov 2026).
    private const string IeeeBibliography = """
        \makeatletter
        \renewenvironment{thebibliography}[1]{\section*{\refname}\addcontentsline{toc}{section}{\refname}\footnotesize\vskip 0.3\baselineskip plus 0.1\baselineskip minus 0.1\baselineskip\list{\@biblabel{\@arabic\c@enumiv}}{\settowidth\labelwidth{\@biblabel{#1}}\leftmargin\labelwidth\advance\leftmargin\labelsep\itemsep\IEEEbibitemsep\relax\usecounter{enumiv}\let\p@enumiv\@empty\renewcommand\theenumiv{\@arabic\c@enumiv}}\def\newblock{\hskip .11em plus .33em minus .07em}\sloppy\clubpenalty4000\widowpenalty4000\interlinepenalty500\sfcode`\.\@m}{\def\@noitemerr{\@latex@warning{Empty `thebibliography' environment}}\endlist}
        \makeatother
        """;

    // acmart's sigconf (and siggraph, sigplan…) sets its authors side by side, each a \vtop of paragraphs inside the
    // line's paragraph: a paragraph in a paragraph, which PDF/UA forbids (veraPDF: "<P> shall not contain <P>"). The
    // same boxes, their lines part of the line's paragraph (launch-bot #257, 19 Nov 2026).
    private const string AcmAuthorBoxes = """
        \makeatletter
        \def\@typeset@author@bx{\bgroup\hsize=\author@bx@wd\def\and{\par}\normalbaselines\global\setbox\author@bx=\vtop{\tagpdfsetup{para/tagging=false}\if@ACM@sigchiamode\else\centering\fi\@authorfont\@currentauthors\par\@affiliationfont\@currentaffiliation\par}\egroup\box\author@bx\hspace{\author@bx@sep}\gdef\@currentauthors{}\gdef\@currentaffiliation{}}
        \makeatother
        """;

    // SIAM's theorems are ntheorem's, whose item was never closed: everything after the first theorem was tagged inside
    // it (veraPDF: "<LI> shall not contain <Sect>"). LaTeX's own \newtheorem is kept before the class and SIAM's five
    // are declared again with it, right after the class, so the export's own theorems use it too (launch-bot #257).
    private const string KeepTaggedNewtheorem = """
        \makeatletter\NewCommandCopy\LiliaTaggedNewtheorem\newtheorem\makeatother
        """;

    private const string SiamTheorems = """
        \makeatletter
        \@for\Lilia@thm:=theorem,lemma,corollary,proposition,definition\do{\expandafter\let\csname\Lilia@thm\endcsname\@undefined\expandafter\let\csname end\Lilia@thm\endcsname\@undefined\expandafter\let\csname\Lilia@thm*\endcsname\@undefined\expandafter\let\csname end\Lilia@thm*\endcsname\@undefined}
        \let\c@theorem\@undefined\let\thetheorem\@undefined\let\p@theorem\@undefined\let\cl@theorem\@undefined\let\theHtheorem\@undefined
        \let\newtheorem\LiliaTaggedNewtheorem
        \newtheorem{theorem}{Theorem}
        \newtheorem{lemma}[theorem]{Lemma}
        \newtheorem{corollary}[theorem]{Corollary}
        \newtheorem{proposition}[theorem]{Proposition}
        \newtheorem{definition}[theorem]{Definition}
        \makeatother
        """;
    private static readonly Regex TheoremDefinition = new(@"\\newtheorem\*?\s*\{(?<env>[A-Za-z]+\*?)\}", RegexOptions.Compiled);

    /// <summary>
    /// The export's main.tex as a tagged PDF/UA-2 source. <paramref name="language"/> is the
    /// document's (BCP 47: "fr", "en-GB"), <paramref name="title"/> its title: PDF/UA needs both.
    /// </summary>
    public static Result Apply(string latex, string? language, string? title)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();
        latex = Regex.Replace(latex, @"^\\DocumentMetadata\{.*\}[ \t]*\n", "", RegexOptions.Multiline);
        // A class LaTeX can't tag yet is refused, never handed back untagged (launch-bot #257, 19 Nov 2026).
        if (WhyUnavailable(latex) is { } why) throw new TaggedPdfUnavailableException(why);
        var cls = ClassOf(latex);
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
        // Under a publisher class, LaTeX's tagged proof outlives the class's own (launch-bot #257, 19 Nov 2026).
        if (Publishers.PublisherClasses.Is(cls) && output.FindIndex(l => DocumentClass.IsMatch(Comment.Replace(l, ""))) is >= 0 and var classLine)
        {
            output.Insert(classLine, KeepTaggedProof);
            if (cls == "siamart251216")
            {
                output.Insert(classLine + 1, KeepTaggedNewtheorem); // before the class, which loads ntheorem
                output.Insert(classLine + 3, SiamTheorems);         // right after it
            }
        }

        var sb = new StringBuilder();
        sb.Append("\\DocumentMetadata{lang=").Append(lang)
          .Append(", pdfversion=2.0, pdfstandard=ua-2, tagging=on, tagging-setup={math/setup=mathml-SE}}\n")
          .Append("\\PassOptionsToPackage{protrusion=false}{microtype}\n");
        sb.Append(string.Join("\n", output).TrimEnd('\n')).Append('\n');
        if (luaUl) sb.Append("% Strike and underline that LuaLaTeX can break across lines (the accessible PDF)\n\\usepackage{lua-ul}\n");
        if (body.Contains(@"\begin{algorithm}", StringComparison.Ordinal)) sb.Append(AlgorithmAsKernelFloat).Append('\n');
        sb.Append(cls switch
        {
            "llncs" => LlncsTaggable + "\n",
            "elsarticle" => ElsarticleTaggable + "\n",
            "IEEEtran" => IeeeBibliography + "\n",
            "acmart" => AcmAuthorBoxes + "\n",
            _ => "",
        });
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

/// <summary>The accessible PDF can't be made for this document (TaggedPdf): the reason is for the person.</summary>
public sealed class TaggedPdfUnavailableException(string message) : Exception(message);
