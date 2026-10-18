using System.Text;
using System.Text.RegularExpressions;
using Lilia.Core.Capabilities;

namespace Lilia.Engines;

/// <summary>
/// One set of maths macros for the editor and the PDF (the user, 18 Oct: "think of math one more
/// time, what would be most practical"). They disagreed both ways: a <c>\R</c> from the document's
/// own preamble showed red in the editor and printed in the PDF; the editor's physics shortcuts
/// (<c>\abs</c>, <c>\bra</c>, <c>\dv</c>, also behind its symbol buttons) showed in the editor and
/// broke the PDF. Now the server holds the list: the editor's KaTeX asks for it
/// (GET …/preview/math-macros), and the PDF defines the same shortcuts.
/// </summary>
public static partial class MathMacros
{
    /// <summary>
    /// Lilia's shortcuts, in the editor and the PDF alike. LaTeX's own commands are not redefined
    /// (<c>\Re</c>, <c>\Im</c>, <c>\hbar</c> print as LaTeX prints them, in both).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> BuiltIn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [@"\bra"] = @"\langle #1 |",
        [@"\ket"] = @"| #1 \rangle",
        [@"\braket"] = @"\langle #1 | #2 \rangle",
        [@"\Bra"] = @"\left\langle #1 \right|",
        [@"\Ket"] = @"\left| #1 \right\rangle",
        [@"\Braket"] = @"\left\langle #1 \middle| #2 \right\rangle",
        [@"\ev"] = @"\langle #1 \rangle",
        [@"\Ev"] = @"\left\langle #1 \right\rangle",
        [@"\mel"] = @"\langle #1 | #2 | #3 \rangle",
        [@"\comm"] = @"\left[ #1, #2 \right]",
        [@"\acomm"] = @"\left\{ #1, #2 \right\}",
        [@"\pb"] = @"\left\{ #1, #2 \right\}",
        [@"\dd"] = @"\mathrm{d}#1",
        [@"\dv"] = @"\frac{\mathrm{d} #1}{\mathrm{d} #2}",
        [@"\pdv"] = @"\frac{\partial #1}{\partial #2}",
        [@"\fdv"] = @"\frac{\delta #1}{\delta #2}",
        [@"\grad"] = @"\nabla",
        [@"\divergence"] = @"\nabla \cdot",
        [@"\curl"] = @"\nabla \times",
        [@"\laplacian"] = @"\nabla^2",
        [@"\vb"] = @"\mathbf{#1}",
        [@"\va"] = @"\vec{\mathbf{#1}}",
        [@"\vu"] = @"\hat{\mathbf{#1}}",
        [@"\vdot"] = @"\boldsymbol{\cdot}",
        [@"\cross"] = @"\boldsymbol{\times}",
        [@"\op"] = @"\hat{#1}",
        [@"\abs"] = @"\left| #1 \right|",
        [@"\norm"] = @"\left\| #1 \right\|",
        [@"\order"] = @"\mathcal{O}\left( #1 \right)",
        [@"\asin"] = @"\sin^{-1}",
        [@"\acos"] = @"\cos^{-1}",
        [@"\atan"] = @"\tan^{-1}",
        [@"\acsc"] = @"\csc^{-1}",
        [@"\asec"] = @"\sec^{-1}",
        [@"\acot"] = @"\cot^{-1}",
        [@"\Tr"] = @"\operatorname{Tr}",
        [@"\tr"] = @"\operatorname{tr}",
        [@"\rank"] = @"\operatorname{rank}",
        [@"\identity"] = @"\mathbb{1}",
        [@"\paulix"] = @"\sigma_x",
        [@"\pauliy"] = @"\sigma_y",
        [@"\pauliz"] = @"\sigma_z",
    };

    /// <summary>
    /// What KaTeX lacks and the PDF has from a real package (siunitx): close stand-ins for the
    /// editor only, so <c>\SI{3}{\metre\per\second}</c> reads "3 m/s" rather than an error.
    /// Never written into the PDF, where siunitx does it properly.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> EditorStandIns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [@"\SI"] = @"#1\,\mathrm{#2}",
        [@"\qty"] = @"#1\,\mathrm{#2}",
        [@"\si"] = @"\mathrm{#1}",
        [@"\unit"] = @"\mathrm{#1}",
        [@"\num"] = @"#1",
        [@"\ang"] = @"#1^\circ",
        [@"\metre"] = "m", [@"\meter"] = "m", [@"\second"] = "s", [@"\kilogram"] = "kg", [@"\gram"] = "g",
        [@"\kelvin"] = "K", [@"\mole"] = "mol", [@"\ampere"] = "A", [@"\candela"] = "cd",
        [@"\newton"] = "N", [@"\joule"] = "J", [@"\watt"] = "W", [@"\pascal"] = "Pa", [@"\hertz"] = "Hz",
        [@"\volt"] = "V", [@"\ohm"] = @"\Omega", [@"\coulomb"] = "C", [@"\farad"] = "F", [@"\tesla"] = "T",
        [@"\litre"] = "L", [@"\liter"] = "L", [@"\minute"] = "min", [@"\hour"] = "h", [@"\electronvolt"] = "eV",
        [@"\celsius"] = @"{}^\circ\mathrm{C}", [@"\degreeCelsius"] = @"{}^\circ\mathrm{C}", [@"\degree"] = @"{}^\circ",
        [@"\percent"] = @"\%",
        [@"\kilo"] = "k", [@"\milli"] = "m", [@"\micro"] = @"\mu{}", [@"\nano"] = "n", [@"\pico"] = "p",
        [@"\mega"] = "M", [@"\giga"] = "G", [@"\centi"] = "c",
        [@"\per"] = "/", [@"\squared"] = "^2", [@"\cubed"] = "^3",
    };

    /// <summary>
    /// The shortcuts for the PDF: provided at \begin{document}, so the author's own definitions
    /// (\newcommand{\abs}…, very common) are made first and win, and the physics package, when
    /// loaded, keeps its own.
    /// </summary>
    public static readonly string PreambleShortcuts = BuildPreamble();

    private static string BuildPreamble()
    {
        var sb = new StringBuilder();
        sb.AppendLine("% Lilia's maths shortcuts, as the editor shows them (defined only if missing, after your preamble)");
        sb.AppendLine(@"\AtBeginDocument{%");
        foreach (var (name, body) in BuiltIn)
        {
            var arity = Arity(body);
            sb.AppendLine(arity == 0 ? $@"  \providecommand{{{name}}}{{{body}}}%" : $@"  \providecommand{{{name}}}[{arity}]{{{body}}}%");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static int Arity(string body) =>
        ParameterRef().Matches(body).Select(m => m.Groups[1].Value[0] - '0').DefaultIfEmpty(0).Max();

    /// <summary>
    /// The macros the editor's KaTeX renders with, for a document: Lilia's shortcuts, the siunitx
    /// stand-ins, then the document's own preamble macros (which win, as in the PDF).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ForEditor(string? customPreamble)
    {
        var all = new Dictionary<string, string>(BuiltIn, StringComparer.Ordinal);
        foreach (var (k, v) in EditorStandIns) all[k] = v;
        foreach (var (name, def) in PreambleMacroCollector.Collect(customPreamble))
        {
            all[name] = def.Form switch
            {
                "DeclareMathOperator" => $@"\operatorname{{{def.Body}}}",
                "DeclareMathOperator*" => $@"\operatorname*{{{def.Body}}}",
                _ => def.Body,
            };
        }
        return all;
    }

    [GeneratedRegex(@"#([1-9])")]
    private static partial Regex ParameterRef();
}
