namespace Lilia.Engines;

/// <summary>
/// LaTeX maths → Typst maths, as data.
///
/// <para>The generator used to translate by regex: sixteen rewrite rules, and
/// anything they did not recognise was passed through unchanged. Typst then
/// read the backslash as its own escape, so <c>\blacksquare</c> became the
/// variable <c>lacksquare</c> and <c>\nabla</c> became <c>abla</c> — the
/// leading letter eaten because <c>\n</c> is an escape. A real paper failed to
/// compile on exactly that (2026-09-10), and because the PDF path falls back
/// to pdflatex when Typst fails, nobody saw it.</para>
///
/// <para>Rules that are pure substitution belong in a table, not in code: a
/// new symbol is then one line and a test, with no logic to re-read. The
/// structural forms that genuinely need parsing — fractions, roots, accents,
/// delimiters, matrices — stay in the converter, which is the only place
/// judgement is required.</para>
///
/// <para>Every entry here is verified by compiling it: see
/// TypstSymbolTableTests, which walks this table and asks typst.</para>
/// </summary>
public static class LatexToTypstSymbols
{
    /// <summary>
    /// Commands that map to a Typst identifier or operator, one for one.
    /// Keys carry no backslash.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Symbols =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── relations ──────────────────────────────────────────────
            ["leq"] = "<=",           ["le"] = "<=",
            ["geq"] = ">=",           ["ge"] = ">=",
            ["neq"] = "!=",           ["ne"] = "!=",
            ["approx"] = "approx",    ["equiv"] = "equiv",
            ["sim"] = "tilde.op",     ["simeq"] = "tilde.eq",
            ["cong"] = "tilde.equiv", ["propto"] = "prop",
            ["ll"] = "lt.double",     ["gg"] = "gt.double",

            // ── operators ──────────────────────────────────────────────
            ["times"] = "times",      ["div"] = "div",
            ["cdot"] = "dot.op",      ["pm"] = "plus.minus",
            ["mp"] = "minus.plus",    ["ast"] = "ast.op",
            ["circ"] = "compose",     ["bullet"] = "bullet",
            ["oplus"] = "plus.o",     ["otimes"] = "times.o",

            // ── set theory and logic ───────────────────────────────────
            ["in"] = "in",            ["notin"] = "in.not",
            ["subset"] = "subset",    ["subseteq"] = "subset.eq",
            ["supset"] = "supset",    ["supseteq"] = "supset.eq",
            ["cup"] = "union",        ["cap"] = "inter",
            ["emptyset"] = "nothing", ["varnothing"] = "nothing",
            ["forall"] = "forall",    ["exists"] = "exists",
            ["neg"] = "not",          ["land"] = "and",
            ["lor"] = "or",           ["setminus"] = "without",

            // ── arrows ─────────────────────────────────────────────────
            ["to"] = "->",            ["rightarrow"] = "->",
            ["leftarrow"] = "<-",     ["leftrightarrow"] = "<->",
            ["Rightarrow"] = "=>",    ["Leftarrow"] = "<=",
            ["Leftrightarrow"] = "<=>", ["mapsto"] = "|->",
            ["implies"] = "=>",       ["iff"] = "<=>",

            // ── named constants and miscellany ─────────────────────────
            ["infty"] = "infinity",   ["partial"] = "partial",
            ["nabla"] = "nabla",      ["ldots"] = "dots.h",
            ["cdots"] = "dots.c",     ["vdots"] = "dots.v",
            ["ddots"] = "dots.down",  ["dots"] = "dots.h",
            ["angle"] = "angle",      ["triangle"] = "triangle",

            // ── delimiters ─────────────────────────────────────────────
            // Found by scanning real documents rather than guessed: \lVert
            // and \rVert appeared nine times each in one paper. Typst 0.15
            // names no angle bracket, so the literal glyph is used — exact,
            // where an invented name would silently be wrong.
            ["lVert"] = "bar.v.double", ["rVert"] = "bar.v.double",
            ["lvert"] = "|",            ["rvert"] = "|",
            ["|"] = "bar.v.double",
            ["langle"] = "\u27E8",       ["rangle"] = "\u27E9",
            ["lceil"] = "⌈",            ["rceil"] = "⌉",
            ["lfloor"] = "⌊",           ["rfloor"] = "⌋",
            ["square"] = "square",    ["blacksquare"] = "square.filled",
            ["qedsymbol"] = "square.filled",
            ["star"] = "star",        ["dagger"] = "dagger",
            ["prime"] = "prime",      ["degree"] = "degree",
            // Typst 0.15 has no name for the reduced Planck constant; the literal
            // glyph is exact where an invented name would silently be wrong.
            ["ell"] = "ell",          ["hbar"] = "\u210F",
            ["aleph"] = "aleph",      ["Re"] = "Re",  ["Im"] = "Im",

            // ── lowercase Greek ────────────────────────────────────────
            ["alpha"] = "alpha", ["beta"] = "beta", ["gamma"] = "gamma",
            ["delta"] = "delta", ["epsilon"] = "epsilon", ["varepsilon"] = "epsilon.alt",
            ["zeta"] = "zeta", ["eta"] = "eta", ["theta"] = "theta",
            ["vartheta"] = "theta.alt", ["iota"] = "iota", ["kappa"] = "kappa",
            ["lambda"] = "lambda", ["mu"] = "mu", ["nu"] = "nu", ["xi"] = "xi",
            ["pi"] = "pi", ["varpi"] = "pi.alt", ["rho"] = "rho", ["varrho"] = "rho.alt",
            ["sigma"] = "sigma", ["varsigma"] = "sigma.alt", ["tau"] = "tau",
            ["upsilon"] = "upsilon", ["phi"] = "phi", ["varphi"] = "phi.alt",
            ["chi"] = "chi", ["psi"] = "psi", ["omega"] = "omega",

            // ── uppercase Greek ────────────────────────────────────────
            ["Gamma"] = "Gamma", ["Delta"] = "Delta", ["Theta"] = "Theta",
            ["Lambda"] = "Lambda", ["Xi"] = "Xi", ["Pi"] = "Pi",
            ["Sigma"] = "Sigma", ["Upsilon"] = "Upsilon", ["Phi"] = "Phi",
            ["Psi"] = "Psi", ["Omega"] = "Omega",

            // ── large operators ────────────────────────────────────────
            ["sum"] = "sum", ["prod"] = "product", ["coprod"] = "product.co",
            ["int"] = "integral", ["iint"] = "integral.double",
            ["iiint"] = "integral.triple", ["oint"] = "integral.cont",
            ["bigcup"] = "union.big", ["bigcap"] = "inter.big",
        };

    /// <summary>
    /// Functions Typst already knows by name; the backslash simply goes.
    /// Kept separate from <see cref="Symbols"/> because a reader looking for
    /// "is sin handled" should not have to scan a symbol list to find out.
    /// </summary>
    public static readonly IReadOnlySet<string> Functions = new HashSet<string>(StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "cot", "sec", "csc",
        "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh",
        "log", "ln", "lg", "exp", "det", "dim", "ker", "deg",
        "gcd", "lcm", "max", "min", "sup", "inf", "lim", "limsup", "liminf",
        "arg", "mod", "Pr",
    };

    /// <summary>
    /// Accents, which take one argument: <c>\hat{x}</c> → <c>hat(x)</c>.
    /// The Typst name is not always the LaTeX one — <c>\bar</c> is
    /// <c>macron</c>, and <c>\vec</c> is <c>arrow</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Accents =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hat"] = "hat",         ["widehat"] = "hat",
            ["bar"] = "macron",      ["overline"] = "overline",
            ["underline"] = "underline",
            ["vec"] = "arrow",       ["dot"] = "dot",
            ["ddot"] = "dot.double", ["tilde"] = "tilde",
            ["widetilde"] = "tilde", ["breve"] = "breve",
            ["check"] = "caron",     ["acute"] = "acute",
            ["grave"] = "grave",
        };

    /// <summary>
    /// Font commands taking one argument: <c>\mathbb{R}</c> → <c>bb(R)</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Fonts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mathbb"] = "bb",       ["mathbf"] = "bold",
            ["mathcal"] = "cal",     ["mathfrak"] = "frak",
            ["mathrm"] = "upright",  ["mathit"] = "italic",
            ["mathsf"] = "sans",     ["mathtt"] = "mono",
            ["boldsymbol"] = "bold", ["text"] = "text",
            ["textrm"] = "text",     ["textbf"] = "bold",
        };

    /// <summary>
    /// Spacing commands, which Typst expresses with explicit widths or not at
    /// all. Mapping them to a space keeps the surrounding maths readable
    /// instead of leaving a stray backslash for Typst to choke on.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Spacing =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [","] = "thin", [";"] = "med", ["!"] = "",
            ["quad"] = "quad", ["qquad"] = "wide",
            [":"] = "med", [" "] = " ",
        };
}
