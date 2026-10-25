using System.Text;
using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// BibTeX inside the PDF compile: pass 1 → BibTeX → pass 2, and a third pass only when TeX says the
/// second one is not settled.
///
/// <para>A PDF with references used to build its bibliography first, in a compile of its own (a
/// pdflatex pass and BibTeX), paste the .bbl into the source and only then run the two passes:
/// three TeX runs, ~1.2–1.4 s of them for the bibliography alone (LTX-110, 18 Oct). Here pass 1 of
/// the compile is the one BibTeX reads.</para>
///
/// <para>A citation prints from the <c>\bibcite</c> the previous pass wrote to the .aux, and that
/// line is written while the bibliography is typeset, so after pass 1 → BibTeX → pass 2 every
/// citation would still be [?]. The .bbl says what each <c>\bibcite</c> will be (the order of the
/// items, or their labels), so it is added to the .aux before pass 2 (<see cref="Bibcites"/>).
/// It is a guess TeX checks: at the end of pass 2 LaTeX compares every <c>\bibcite</c> and
/// <c>\newlabel</c> with what it used and warns "Label(s) may have changed" (natbib: "Citation(s)
/// may have changed") on any difference, and then a third pass runs (<see cref="NeedsRerun"/>).
/// A wrong guess costs that pass, never a wrong citation.</para>
/// </summary>
internal static class BibliographyPasses
{
    private static readonly Regex BibData = new(@"\\bibdata\{([^}]*)\}", RegexOptions.Compiled);

    /// <summary>
    /// Whether BibTeX has anything to do: the .aux names a database (<c>\bibdata</c>) and at least
    /// one of them is beside the .tex with something in it. Without a .bib BibTeX would write an
    /// empty bibliography where the source printed nothing before.
    /// </summary>
    public static bool HasDatabase(string aux, string dir)
    {
        foreach (Match m in BibData.Matches(aux))
            foreach (var name in m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var file = Path.Combine(dir, name.EndsWith(".bib", StringComparison.OrdinalIgnoreCase) ? name : name + ".bib");
                var full = Path.GetFullPath(file);
                if (full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && File.Exists(full) && new FileInfo(full).Length > 0)
                    return true;
            }
        return false;
    }

    // \bibitem[optional label]{key}: the label may hold braces (\natexlab{a}), one level deep.
    private static readonly Regex BibItem = new(
        @"\\bibitem\s*(?:\[(?<opt>(?:[^\[\]{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\])?\s*\{(?<key>[^{}]*)\}",
        RegexOptions.Compiled);

    // natbib's label: Short(Year)Long, split at the first "(" and the first ")" after it.
    private static readonly Regex NatbibLabel = new(@"^(?<short>[^()]*)\((?<year>[^()]*)\)(?<long>.*)$", RegexOptions.Compiled);

    // A command the .bbl defines for itself (\providecommand \citenamefont [1]{#1}: REVTeX's styles).
    private static readonly Regex BblDefines = new(@"\\(?:provide|new)command\s*\*?\s*\{?\\(?<name>[A-Za-z@]+)", RegexOptions.Compiled);
    private static readonly Regex CommandName = new(@"\\(?<name>[A-Za-z@]+)", RegexOptions.Compiled);

    /// <summary>
    /// The <c>\bibcite</c> lines typesetting <paramref name="bbl"/> will write: <c>{n}</c> for an
    /// unlabelled item and the label itself for a labelled one, or natbib's
    /// <c>{{n}{year}{{short}}{{long}}}</c>. Items it cannot read are left out (TeX then asks for
    /// the third pass): a label using a command the .bbl defines itself, which is not defined yet
    /// when the .aux is read (REVTeX's <c>[{\citenamefont {Knuth}(1984)}]</c>, 25 Oct 2026).
    /// </summary>
    public static string Bibcites(string bbl, bool natbib)
    {
        // TeX reads a line end and the next line's indentation as one space.
        var text = Regex.Replace(bbl, @"\s*\n\s*", " ");
        var ownCommands = BblDefines.Matches(text).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var sb = new StringBuilder();
        int numbered = 0, all = 0;
        foreach (Match m in BibItem.Matches(text))
        {
            var key = m.Groups["key"].Value.Trim();
            var labelled = m.Groups["opt"].Success;
            var opt = m.Groups["opt"].Value.Trim();
            all++;
            if (!labelled) numbered++;
            if (key.Length == 0) continue;
            if (CommandName.Matches(opt).Any(c => ownCommands.Contains(c.Groups["name"].Value))) continue;
            // [{label}]: braces that protect the label; natbib reads what is inside.
            if (opt.Length > 1 && opt[0] == '{' && opt[^1] == '}' && Balanced(opt[1..^1])) opt = opt[1..^1].Trim();
            if (natbib)
            {
                if (!labelled) { sb.Append($"\\bibcite{{{key}}}{{{{{all}}}{{}}{{{{}}}}{{{{}}}}}}\n"); continue; }
                var parts = NatbibLabel.Match(opt);
                if (!parts.Success) continue;
                sb.Append($"\\bibcite{{{key}}}{{{{{all}}}{{{parts.Groups["year"].Value}}}{{{{{parts.Groups["short"].Value}}}}}{{{{{parts.Groups["long"].Value}}}}}}}\n");
            }
            else
            {
                sb.Append($"\\bibcite{{{key}}}{{{(labelled ? opt : numbered.ToString())}}}\n");
            }
        }
        return sb.ToString();
    }

    private static bool Balanced(string s)
    {
        var depth = 0;
        foreach (var c in s)
        {
            if (c == '{') depth++;
            else if (c == '}' && --depth < 0) return false;
        }
        return depth == 0;
    }

    private static readonly Regex LoadsNatbib = new(@"\\(?:usepackage|RequirePackage)\s*(?:\[[^\]]*\])?\s*\{[^}]*\bnatbib\b", RegexOptions.Compiled);

    /// <summary>
    /// natbib is loaded: its <c>\bibcite</c> carries four fields, not one. The source says so, or the
    /// log (a class or package that loads it). A pass started from the preamble format lists none of
    /// the format's packages in its log, so the log alone missed it and every natbib PDF took the
    /// third pass.
    /// </summary>
    public static bool UsesNatbib(string latex, string log) =>
        LoadsNatbib.IsMatch(latex) || log.Contains("Package: natbib", StringComparison.Ordinal);

    /// <summary>
    /// The pass left something unsettled: LaTeX's own label check, natbib's citation check, or
    /// hyperref's outlines. Log lines wrap at 79 characters, so the warning is looked for with the
    /// line ends taken out.
    /// </summary>
    public static bool NeedsRerun(string log)
    {
        var flat = log.Replace("\r", "").Replace("\n", "");
        return flat.Contains("may have changed", StringComparison.Ordinal)
            || flat.Contains("Rerun to get", StringComparison.Ordinal);
    }

    /// <summary>The lists a pass reads from the one before (contents, figures, tables).</summary>
    public static readonly string[] ListExtensions = [".toc", ".lof", ".lot"];
}
