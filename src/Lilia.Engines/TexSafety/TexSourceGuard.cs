using System.Text.RegularExpressions;

namespace Lilia.Engines.TexSafety;

/// <summary>
/// A document may not make the compiler read the host's files or run its commands.
///
/// <para>A document's LaTeX is compiled by the same operating-system user as the API. Standard
/// primitives (<c>\input{/etc/hostname}</c>, <c>\openin</c>) read any file that user can, and the
/// result is printed into the PDF. Measured on 5 Oct 2026 with TeX Live 2026: the host name came
/// back in the PDF, and <c>openin_any=p</c> set three ways (environment, <c>-cnf-line</c>, a
/// <c>texmf.cnf</c>) did not stop it. The endpoint that does it without any login is
/// <c>GET /api/latex/svg</c>; the raw <c>POST /api/latex/render</c> and every document compile are
/// the same primitive behind a login.</para>
///
/// <para><b>What this is.</b> A refusal, before any compile, of: the file-reading and file-writing
/// primitives given anything but a plain relative name; shell and Lua escapes; the primitives that
/// turn text into code (<c>\scantokens</c>); and any mention of the host's own paths. <b>What it is
/// not.</b> A sandbox. A determined author can build a command name out of tokens
/// (<c>\csname</c>, <c>\catcode</c>) and walk past a scan of the source. The guarantee has to come
/// from the process: a separate unprivileged user or a container with no host files
/// (<see cref="TexProcessRunner"/> runs the engine as <c>LILIA_TEX_RUN_AS</c> when set). This guard
/// removes the easy and the automated cases and gives a clear error; it does not replace that.</para>
/// </summary>
public static class TexSourceGuard
{
    // Commands that open a file by name: the argument must be a plain relative name.
    private static readonly string[] FileMacros =
    {
        "input", "include", "InputIfFileExists", "openin", "verbatiminput", "VerbatimInput", "lstinputlisting",
        "inputminted", "includepdf", "import", "subimport", "includefrom", "inputfrom", "loadglsentries",
        "bibliography", "addbibresource", "usepackage", "RequirePackage", "documentclass", "LoadClass",
        "includegraphics", "openout", "csvreader", "pgfplotstableread", "pgfplotstabletypeset",
    };

    // Never allowed, whatever the argument: shell, Lua, and text-to-code.
    private static readonly string[] Forbidden =
    {
        "write18", "ShellEscape", "directlua", "luaexec", "latelua", "luacode", "luadirect", "lua",
        "scantokens", "scantextokens", "pdfscantokens", "readline", "pdfshellescape",
        "immediate\\s*\\\\write\\s*18",
    };

    // Host paths. A document has no reason to name them.
    private static readonly Regex HostPath = new(
        @"(?<![A-Za-z0-9_])(/(?:etc|proc|sys|root|home|var|usr|opt|app|tmp|dev|run|mnt|srv|boot|bin|sbin|lib|lib64|media)(?:/|\b)|~/|\.\./|\\string\s*/)",
        RegexOptions.Compiled);

    private static readonly Regex FileMacroCall = new(
        @"\\(?<name>" + string.Join("|", FileMacros) + @")(?![A-Za-z@])\s*(?<opt>\[[^\]]*\])?\s*(?<arg>\{[^}]*\}|[^\s{]*)",
        RegexOptions.Compiled);

    private static readonly Regex ForbiddenCall = new(
        @"\\(?:" + string.Join("|", Forbidden) + @")(?![A-Za-z@])", RegexOptions.Compiled);

    private static readonly Regex PlainName = new(@"^[A-Za-z0-9_][A-Za-z0-9_\-./ ]*$", RegexOptions.Compiled);

    /// <summary>Null when the source is acceptable, else a sentence for the author.</summary>
    public static string? Violation(string? latex)
    {
        if (string.IsNullOrEmpty(latex)) return null;
        var source = StripVerbatim(StripComments(latex));

        var forbidden = ForbiddenCall.Match(source);
        if (forbidden.Success)
            return $"This LaTeX uses {forbidden.Value.Trim()}, which runs commands or builds code from text. It is not allowed here.";

        var path = HostPath.Match(source);
        if (path.Success)
            return $"This LaTeX names a path on the server ({path.Value.Trim()}). Documents cannot read or write server files.";

        foreach (Match m in FileMacroCall.Matches(source))
        {
            var name = m.Groups["name"].Value;
            var arg = m.Groups["arg"].Value.Trim().Trim('{', '}').Trim();
            if (name is "usepackage" or "RequirePackage" or "documentclass" or "LoadClass")
            {
                // A package or class is a name, or a comma list of names: never a path.
                if (arg.Split(',').Select(a => a.Trim()).Any(a => a.Length > 0 && (a.StartsWith('/') || a.Contains("..") || a.Contains('\\') || a.Contains('|'))))
                    return $"\\{name} must name a package or class, not a path.";
                continue;
            }
            // \input etc: a plain relative name only. No pipe, no backslash (a built name), no absolute or parent path.
            if (arg.Length == 0 || arg.StartsWith('|') || arg.StartsWith('/') || arg.Contains("..") || arg.Contains('\\') || arg.Contains('|') || !PlainName.IsMatch(arg))
            {
                if (name is "includegraphics" && arg.Length > 0 && !arg.Contains("..") && !arg.StartsWith('/') && !arg.Contains('\\')) continue;
                return $"\\{name} may only name a plain file in the document ({(arg.Length == 0 ? "no name given" : arg)} is not allowed).";
            }
        }
        return null;
    }

    // Text that is printed as it is cannot run anything, and documentation about a Unix system
    // legitimately says /usr/bin/python: leave verbatim, listings, \verb, \url and \href targets out of the scan.
    private static readonly Regex Verbatim = new(
        @"\\begin\{(?<env>verbatim\*?|Verbatim\*?|lstlisting|minted|alltt)\}(?:\[[^\]]*\])?(?:\{[^}]*\})?.*?\\end\{\k<env>\}" +
        @"|\\verb\*?(?<d>[^A-Za-z\s*]).*?\k<d>" +
        @"|\\(?:url|nolinkurl|path)\{[^}]*\}|\\href\{[^}]*\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static string StripVerbatim(string s) => Verbatim.Replace(s, " ");

    /// <summary>A <c>%</c> comment runs to the end of the line, unless escaped.</summary>
    private static string StripComments(string s) => Regex.Replace(s, @"(?<!\\)%[^\r\n]*", "");

    public static void ThrowIfUnsafe(string? latex)
    {
        var v = Violation(latex);
        if (v is not null) throw new UnsafeLatexException(v);
    }
}

/// <summary>The LaTeX asked for something the server will not do. Safe to show to the author.</summary>
public sealed class UnsafeLatexException : Exception
{
    public UnsafeLatexException(string message) : base(message) { }
}
