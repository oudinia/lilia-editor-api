using System.Text;

namespace Lilia.Engines.Publishers;

/// <summary>
/// Publisher classes (25 Oct 2026, publisher templates phase 1): acmart, IEEEtran, elsarticle, llncs.
/// The owner's requirement: "Visual blocks are an editing surface; LaTeX remains the source of truth";
/// an author picks a venue and gets a compilable, submission-ready project in the publisher's own class.
///
/// <para>What changes for these classes, measured by compiling a full document in each (25 Oct):</para>
/// <list type="bullet">
/// <item><b>No layout of Lilia's own</b>: no margins, spacing, headers, columns or pagination policy
/// (placeins' <c>[section]</c> redefines <c>\section</c>, which acmart refuses). The class decides.</item>
/// <item><b>A slimmer package list</b>: no <c>lmodern</c> (it replaced ACM's and IEEE's fonts), no
/// <c>parskip</c>; acmart brings its own maths fonts (newtx), so <c>amssymb</c>/<c>amsfonts</c> clashed
/// (<c>\Bbbk</c> already defined); llncs defines <c>proof</c>, so amsthm's is cleared first.</item>
/// <item><b>Theorems the class already defines</b> are left to it (llncs defines theorem, lemma…):
/// <see cref="LaTeXPreamble.TheoremEnvironmentsFor"/> guards them.</item>
/// <item><b>Front matter in the class's own shape</b>: acmart wants the abstract and keywords before
/// <c>\maketitle</c>; elsarticle a <c>frontmatter</c> environment with one <c>\author</c> each and no
/// <c>\maketitle</c>; llncs <c>\institute</c> and keywords inside the abstract; IEEEtran author blocks.</item>
/// <item><b>The publisher's bibliography style</b> on export.</item>
/// </list>
/// </summary>
public static class PublisherClasses
{
    /// <summary>
    /// The rules above, as a version a submission's manifest records: a change to the packages a
    /// publisher document loads changes it. Phase 2's package whitelist replaces it.
    /// </summary>
    public const string PackagePolicyVersion = "lilia-publisher-2026-10-25";

    private static readonly string[] Names = ["acmart", "IEEEtran", "elsarticle", "llncs"];

    /// <summary>The class's canonical spelling when it is a publisher class, else null.</summary>
    public static string? Canonical(string? cls) =>
        cls is null ? null : Names.FirstOrDefault(n => string.Equals(n, cls.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool Is(string? cls) => Canonical(cls) is not null;

    /// <summary>The bibliography style each publisher requires.</summary>
    public static string? BibliographyStyle(string? cls) => Canonical(cls) switch
    {
        "acmart" => "ACM-Reference-Format",
        "IEEEtran" => "IEEEtran",
        "elsarticle" => "elsarticle-num",
        "llncs" => "splncs04",
        _ => null,
    };

    /// <summary>Lilia's package list, slimmed for a publisher class (see the class summary).</summary>
    public static string Packages(string packages, string cls)
    {
        var c = Canonical(cls);
        if (c is null) return packages;
        var s = packages;
        s = Drop(s, @"\usepackage{lmodern}");
        s = Drop(s, @"\usepackage{parskip}");
        // Lilia's blue links are not the publisher's: acmart sets its own; the others get plain links
        // (without either, hyperref draws coloured boxes around every link).
        s = s.Replace(@"\PassOptionsToPackage{colorlinks=true,linkcolor=blue,citecolor=blue,urlcolor=blue}{hyperref}",
            c == "acmart" ? "% acmart sets the links' look itself." : @"\PassOptionsToPackage{hidelinks}{hyperref}");
        const string ams = @"\usepackage{amsmath,amssymb,amsfonts,amsthm}";
        s = c switch
        {
            // newtx (loaded by acmart) defines the AMS symbols; acmart loads amsthm itself.
            "acmart" => s.Replace(ams, @"\usepackage{amsmath}"),
            // llncs defines proof (and theorem…); amsthm's \proof would be "already defined".
            "llncs" => s.Replace(ams, "\\let\\proof\\relax\n\\let\\endproof\\relax\n" + ams),
            _ => s,
        };
        return s;
    }

    private static string Drop(string s, string line)
    {
        var i = s.IndexOf(line, StringComparison.Ordinal);
        if (i < 0) return s;
        var end = s.IndexOf('\n', i);
        return end < 0 ? s[..i] : s[..i] + s[(end + 1)..];
    }

    /// <summary>
    /// The front matter for a publisher class, in two parts: what goes before <c>\begin{document}</c>, and
    /// what opens the body up to and including the title (and, for these classes, the abstract and
    /// keywords). <paramref name="abstractEnv"/> is a whole <c>\begin{abstract}…\end{abstract}</c>, or null.
    /// <paramref name="escape"/> escapes plain text (names and affiliations come out of the author string
    /// as text: <see cref="TitleAuthors"/>).
    /// </summary>
    public static (string Preamble, string BodyStart) FrontMatter(
        string cls, string title, string? authorText, string? abstractEnv, IReadOnlyList<string> keywords,
        Func<string, string> escape, string titleLatex)
    {
        var authors = TitleAuthors.Parse(authorText);
        var pre = new StringBuilder();
        var body = new StringBuilder();
        var kw = keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => escape(k.Trim())).ToList();
        string Line(IEnumerable<string> lines, string sep) => string.Join(sep, lines.Select(escape));

        switch (Canonical(cls))
        {
            case "acmart":
                pre.AppendLine($@"\title{{{titleLatex}}}");
                foreach (var a in authors)
                {
                    pre.AppendLine($@"\author{{{escape(a.Name)}}}");
                    // ACM wants an affiliation per author: the lines under the name, the last one as the
                    // country. acmart stops on an empty \country ("No country present"), so: no lines, no
                    // \affiliation (it compiles); one line, the institution with an unknown country
                    // (\relax: present, prints nothing). The compliance check asks for the country.
                    if (a.Lines.Count > 0)
                    {
                        var inst = a.Lines.Count > 1 ? Line(a.Lines.Take(a.Lines.Count - 1), ", ") : escape(a.Lines[0]);
                        var country = a.Lines.Count > 1 ? escape(a.Lines[^1]) : @"\relax";
                        pre.AppendLine($@"\affiliation{{\institution{{{inst}}}\country{{{country}}}}}");
                    }
                }
                body.AppendLine(@"\begin{document}");
                if (abstractEnv is not null) body.AppendLine(abstractEnv.TrimEnd());
                if (kw.Count > 0) body.AppendLine($@"\keywords{{{string.Join(", ", kw)}}}");
                body.AppendLine(@"\maketitle");
                break;

            case "IEEEtran":
                pre.AppendLine($@"\title{{{titleLatex}}}");
                if (authors.Count > 0)
                    pre.AppendLine(@"\author{" + string.Join("\n\\and\n", authors.Select(a =>
                        $@"\IEEEauthorblockN{{{escape(a.Name)}}}" + (a.Lines.Count > 0 ? $@"\IEEEauthorblockA{{{Line(a.Lines, @"\\")}}}" : ""))) + "}");
                else pre.AppendLine(@"\author{}");
                body.AppendLine(@"\begin{document}");
                body.AppendLine(@"\maketitle");
                if (abstractEnv is not null) body.AppendLine(abstractEnv.TrimEnd());
                if (kw.Count > 0) body.AppendLine($@"\begin{{IEEEkeywords}}{string.Join(", ", kw)}\end{{IEEEkeywords}}");
                break;

            case "elsarticle":
                body.AppendLine(@"\begin{document}");
                body.AppendLine(@"\begin{frontmatter}");
                body.AppendLine($@"\title{{{titleLatex}}}");
                foreach (var a in authors)
                {
                    body.AppendLine($@"\author{{{escape(a.Name)}}}");
                    if (a.Lines.Count > 0) body.AppendLine($@"\affiliation{{organization={{{Line(a.Lines, ", ")}}}}}");
                }
                if (abstractEnv is not null) body.AppendLine(abstractEnv.TrimEnd());
                if (kw.Count > 0) body.AppendLine($@"\begin{{keyword}}{string.Join(@" \sep ", kw)}\end{{keyword}}");
                body.AppendLine(@"\end{frontmatter}");
                break;

            case "llncs":
            {
                pre.AppendLine($@"\title{{{titleLatex}}}");
                // \institute lists each affiliation once; authors point to theirs with \inst{n}.
                var institutes = new List<string>();
                var names = new List<string>();
                foreach (var a in authors)
                {
                    var inst = a.Lines.Count > 0 ? Line(a.Lines, ", ") : null;
                    var n = 0;
                    if (inst is not null)
                    {
                        var i = institutes.IndexOf(inst);
                        if (i < 0) { institutes.Add(inst); i = institutes.Count - 1; }
                        n = i + 1;
                    }
                    names.Add(escape(a.Name) + (n > 0 ? $@"\inst{{{n}}}" : ""));
                }
                pre.AppendLine($@"\author{{{string.Join(@" \and ", names)}}}");
                // llncs prints \inst numbers only with more than one institute; one is fine as is.
                pre.AppendLine(institutes.Count > 0 ? $@"\institute{{{string.Join(@" \and ", institutes)}}}" : @"\institute{}");
                body.AppendLine(@"\begin{document}");
                body.AppendLine(@"\maketitle");
                if (abstractEnv is not null)
                {
                    var env = abstractEnv.TrimEnd();
                    if (kw.Count > 0)
                        env = env.Replace(@"\end{abstract}", $"\\keywords{{{string.Join(@" \and ", kw)}}}\n\\end{{abstract}}");
                    body.AppendLine(env);
                }
                break;
            }

            default:
                throw new ArgumentException($"{cls} is not a publisher class");
        }
        return (pre.ToString(), body.ToString());
    }
}
