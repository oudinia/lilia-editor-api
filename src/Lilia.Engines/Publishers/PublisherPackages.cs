using System.Text.RegularExpressions;

namespace Lilia.Engines.Publishers;

/// <summary>
/// The packages a publisher accepts (publisher templates phase 2, 25 Oct 2026). ACM publishes one:
/// "The ACM Publishing System (TAPS) List of Accepted LaTeX Packages", authors.acm.org, last updated
/// April 2025, read on 25 Oct 2026, plus the packages acmart already loads. TAPS converts the source
/// to HTML and rejects anything else. The other publishers publish no such list.
///
/// <para>Lilia's own preamble loads some packages beyond ACM's list for features a document may not
/// use (callouts, struck-through text, table notes…). <see cref="KeepAccepted"/> drops each one the
/// document doesn't use; one it does use stays, and the compliance check names the feature.</para>
/// </summary>
public static partial class PublisherPackages
{
    /// <summary>Which list, for the manifest's package-policy version.</summary>
    public const string AcmListVersion = "acm-taps-2025-04";

    private static readonly HashSet<string> AcmAccepted = new(StringComparer.Ordinal)
    {
        "abstract", "acronym", "algorithm", "algorithm2e", "algorithmic", "alltt", "amsbsy", "amscd", "amsfonts", "amsgen",
        "amsmath", "amsmidx", "amsopn", "amssymb", "amstext", "amsthm", "amsxtra", "apacite", "appendix", "auxhook",
        "balance", "bbding", "bbold", "bm", "bold-braces", "braket", "breakurl",
        "calc", "cancel", "ccicons", "centernot", "cgloss4e", "changes", "checkend", "CJK", "clean", "cleveref", "cmap",
        "color", "colortbl", "comma", "coollist", "coolstr", "crossreftools", "curves",
        "datenumber", "dcolumn", "decimal", "delarray", "dirtytalk", "draftwatermark",
        "enumitem", "epigraph", "epstopdf", "esdiff", "etex", "eucal", "eufrak",
        "fancybox", "fancyhdr", "fancyvrb", "fix-cm", "fixfoot", "fixltx2e", "fixme", "flafter", "float", "fontawesome",
        "fontawesome5", "fontenc", "forloop", "fp", "framed",
        "gb4e", "geometry", "glossaries", "graphics", "graphicx", "graphpap",
        "harmony", "html", "hyperref", "ifpdf", "ifthen", "index", "inputenc", "iopams", "keyval", "kvoptions",
        "listings", "lscape", "makecell", "makeidx", "maple2e", "mapleenv", "mapleplots", "maplestyle", "mapletab",
        "mapleutil", "mathabx", "mathtool", "mathtools", "mciteplus", "microtype", "multirow",
        "natbib", "newlfont", "nicefrac", "nomencl", "nopageno", "oldlfont", "overword", "physics", "pifont", "rotating",
        "setspace", "shortvrb", "showidx", "SIunits", "siunitx", "stfloats", "stmaryrd", "soul", "subcaption", "subfig",
        "subfigure", "suffix", "svg", "tabular", "textcase", "textcomp", "textgreek", "tfrupee", "tipa", "tipx",
        "titlepage", "tloop", "totpages", "trimspaces", "units", "upmath", "url", "verbatim", "xcolor", "xfrac", "xspace",
    };

    /// <summary>What acmart loads itself: accepted, and pointless (at best) to load again.</summary>
    public static IReadOnlySet<string> AcmartLoads { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "amsart", "amsmath", "babel", "balance", "booktabs", "caption", "cmap", "comment", "draftwatermark", "environ",
        "etoolbox", "fancyhdr", "float", "fontenc", "framed", "geometry", "graphicx", "hyperref", "hyperxmp", "iftex",
        "libertine", "manyfoot", "microtype", "natbib", "newtxmath", "pbalance", "refcount", "setspace", "totpages",
        "unicode-math", "xcolor", "xkeyval", "xstring", "zi4", "zref-savepos", "zref-user",
    };

    /// <summary>The packages <paramref name="cls"/>'s publisher accepts, or null when it publishes no list.</summary>
    public static IReadOnlySet<string>? Accepted(string? cls) =>
        PublisherClasses.Canonical(cls) == "acmart" ? Acm : null;

    private static readonly HashSet<string> Acm = [.. AcmAccepted, .. AcmartLoads];

    /// <summary>
    /// Lilia's packages outside ACM's list, what each is for in words, and how a document shows it uses
    /// it. <c>newunicodechar</c> is judged by the characters it maps (<see cref="KeepAccepted"/>).
    /// </summary>
    private static readonly (string Package, string Feature, Regex Use)[] Features =
    [
        ("mathrsfs", "script letters (\\mathscr)", new(@"\\mathscr\b")),
        ("tabularx", "full-width tables", new(@"\\begin\{tabularx\}")),
        ("longtable", "tables that run over pages", new(@"\\begin\{longtable\*?\}|\\begin\{ThreePartTable\}")),
        ("array", "table column formats", new(@"\\newcolumntype|\\begin\{(?:tabularx|longtable)|\\begin\{tabular\*?\}(?:\{[^}]*\})?\{[^}]*[<>mbw]\{")),
        ("threeparttablex", "table notes", new(@"\\begin\{(?:ThreePartTable|threeparttable|tablenotes)\}|\\tnote\b")),
        ("needspace", "keeping a heading with what follows it", new(@"\\[Nn]eedspace\b")),
        ("tcolorbox", "callout blocks", new(@"\\begin\{tcolorbox\}|\\tcb[a-z]*\b|\\newtcolorbox\b")),
        ("epsfig", "old \\epsfig figures", new(@"\\(?:epsfig|psfig)\b")),
        ("ulem", "struck-through or wavy-underlined text", new(@"\\(?:sout|uwave|xout|dashuline|dotuline|uline)\b")),
        ("newunicodechar", "Greek letters and symbols typed as characters", new(@"(?!)")),
    ];

    /// <summary>What a Lilia package is used for, in words, or null.</summary>
    public static string? FeatureOf(string package) => Features.FirstOrDefault(f => f.Package == package).Feature;

    [GeneratedRegex(@"^\s*\\(?:usepackage|RequirePackage)\s*(?:\[[^\]]*\])?\s*\{(?<names>[^}]*)\}\s*$", RegexOptions.Multiline)]
    private static partial Regex PackageLine();

    [GeneratedRegex(@"^\\newunicodechar\{(?<ch>[^}]+)\}.*$\n?", RegexOptions.Multiline)]
    private static partial Regex UnicodeCharLine();

    /// <summary>Every package the preamble of <paramref name="latex"/> loads, in order, comments ignored.</summary>
    public static IReadOnlyList<string> Loaded(string latex)
    {
        var end = latex.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        var preamble = StripComments(end < 0 ? latex : latex[..end]);
        return PackageLine().Matches(preamble)
            .SelectMany(m => m.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// <paramref name="latex"/> without the Lilia packages <paramref name="cls"/>'s publisher doesn't accept
    /// and the document doesn't use. <paramref name="everything"/> is all of the project's LaTeX when the
    /// body is in other files (the multi-file export); by default, <paramref name="latex"/> itself.
    /// </summary>
    public static string KeepAccepted(string latex, string cls, string? everything = null)
    {
        var accepted = Accepted(cls);
        if (accepted is null) return latex;
        var end = latex.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        if (end < 0) return latex;
        // Where a use counts: the body, and anything not a package line (the author's own preamble).
        var uses = StripComments(PackageLine().Replace(UnicodeCharLine().Replace(everything ?? latex, ""), ""));
        var preamble = latex[..end];
        var drop = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (package, _, use) in Features)
        {
            if (accepted.Contains(package)) continue;
            if (package == "newunicodechar")
            {
                // The characters it maps: kept when the document types one of them.
                var chars = UnicodeCharLine().Matches(preamble).Select(m => m.Groups["ch"].Value).ToList();
                var bodyText = (everything ?? latex)[end..];
                if (!chars.Any(c => bodyText.Contains(c, StringComparison.Ordinal))) drop.Add(package);
                continue;
            }
            if (!use.IsMatch(uses)) drop.Add(package);
        }
        if (drop.Count == 0) return latex;
        preamble = PackageLine().Replace(preamble, m =>
        {
            var names = m.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var kept = names.Where(n => !drop.Contains(n)).ToList();
            if (kept.Count == names.Length) return m.Value;
            return kept.Count == 0 ? "" : m.Value.Replace(m.Groups["names"].Value, string.Join(",", kept));
        });
        if (drop.Contains("newunicodechar")) preamble = UnicodeCharLine().Replace(preamble, "");
        // ulem's own switch, written right after it.
        if (drop.Contains("ulem")) preamble = Regex.Replace(preamble, @"^\\normalem\s*$\n?", "", RegexOptions.Multiline);
        return preamble + latex[end..];
    }

    private static string StripComments(string s) => Regex.Replace(s, @"(?<!\\)%.*$", "", RegexOptions.Multiline);
}
