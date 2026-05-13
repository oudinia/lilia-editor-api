using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// LaTeX engine — ordered by capability (Pdflatex &lt; Xelatex &lt; Lualatex).
/// Use Max() across blocks to find the engine the doc as a whole needs;
/// use string form ("pdflatex"/"xelatex"/"lualatex") to drive the
/// existing engine-aware overloads on <see cref="LaTeXRenderService"/>
/// and the DB column.
/// </summary>
public enum LatexEngine
{
    Pdflatex = 0,
    Xelatex = 1,
    Lualatex = 2,
}

public static class LatexEngineExtensions
{
    public static string ToCli(this LatexEngine engine) => engine switch
    {
        LatexEngine.Lualatex => "lualatex",
        LatexEngine.Xelatex => "xelatex",
        _ => "pdflatex",
    };

    public static LatexEngine ParseEngine(this string? value) => (value?.ToLowerInvariant()) switch
    {
        "lualatex" => LatexEngine.Lualatex,
        "xelatex" => LatexEngine.Xelatex,
        _ => LatexEngine.Pdflatex,
    };
}

/// <summary>
/// Scan LaTeX source for engine-specific commands and return the
/// minimum engine required to compile it. Cheap regex pass over text
/// already in memory — no DB hit, no caller-side caching needed.
///
/// Strategy (2026-05-14): the user shouldn't have to pick a compiler.
/// Auto-detect per block; the doc-export engine is the max across all
/// blocks plus the preamble. pdflatex is the floor (fastest), lualatex
/// only if the content actually needs it (fontspec, system fonts,
/// unicode-math, \directlua). Add patterns here as Sentry surfaces real
/// engine-mismatch cases — the cost of a false-pdflatex tag is a
/// validation pass that errors with "Undefined control sequence
/// \setmainfont", which already routes to the user.
/// </summary>
public static class EngineDetector
{
    // Lualatex-only — Lua callouts that pdflatex/xelatex can't run.
    private static readonly Regex LuaOnly = new(
        @"\\(directlua|luaexec|luadirect)\b",
        RegexOptions.Compiled);

    // fontspec-family — works on lualatex AND xelatex. Lualatex is our
    // chosen modern engine, so map these to Lualatex (tier 2).
    private static readonly Regex Fontspec = new(
        @"\\(setmainfont|setsansfont|setmonofont|setmathfont|newfontfamily|newfontface|fontspec)\b",
        RegexOptions.Compiled);

    // Package-level signal — if a block (or preamble) loads fontspec or
    // unicode-math, the doc is locked into lua/xelatex.
    private static readonly Regex FontPackages = new(
        @"\\usepackage\{(?:fontspec|unicode-math|polyglossia)\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Detect the engine required to compile <paramref name="latex"/>.
    /// Returns <see cref="LatexEngine.Pdflatex"/> when no engine-specific
    /// commands are present.
    /// </summary>
    public static LatexEngine Detect(string? latex)
    {
        if (string.IsNullOrEmpty(latex)) return LatexEngine.Pdflatex;
        if (LuaOnly.IsMatch(latex)) return LatexEngine.Lualatex;
        if (Fontspec.IsMatch(latex) || FontPackages.IsMatch(latex)) return LatexEngine.Lualatex;
        return LatexEngine.Pdflatex;
    }

    /// <summary>
    /// Document-level engine — the max engine required across every
    /// block + the preamble extras. Caller passes preamble text (e.g.
    /// the LaTeX preamble overrides from `Document.LatexPackages`
    /// + the font picker's `\setmainfont{...}`) so a doc with no
    /// per-block fontspec but a doc-level OTF font still picks lualatex.
    /// </summary>
    public static LatexEngine DetectDocument(IEnumerable<string?> blockContents, string? preambleExtras = null)
    {
        var max = Detect(preambleExtras);
        foreach (var block in blockContents)
        {
            var e = Detect(block);
            if (e > max) max = e;
            if (max == LatexEngine.Lualatex) break; // already at the ceiling
        }
        return max;
    }
}
