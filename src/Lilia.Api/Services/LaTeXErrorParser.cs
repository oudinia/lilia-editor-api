using System.Text.RegularExpressions;

namespace Lilia.Api.Services;

/// <summary>
/// Parses raw pdflatex .log output into structured error information.
/// </summary>
public static class LaTeXErrorParser
{
    private static readonly Regex LineNumberRe = new(@"\bl\.(\d+)\b", RegexOptions.Compiled);
    private static readonly Regex UndefinedCsRe = new(@"^! Undefined control sequence\.", RegexOptions.Compiled);
    private static readonly Regex UndefinedEnvRe = new(@"Environment (\S+) undefined", RegexOptions.Compiled);
    private static readonly Regex PackageErrorRe = new(@"^! Package (\S+) Error:", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MathModeRe = new(@"Missing \$|Extra \}|math mode|display math", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EnvMismatchRe = new(@"\\begin\{(\S+)\}.*ended by \\end\{(\S+)\}", RegexOptions.Compiled);
    private static readonly Regex MissingFileRe = new(@"File `([^']+)' not found|cannot find file", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UndefinedCsTokenRe = new(@"\\([a-zA-Z@]+)", RegexOptions.Compiled);
    private static readonly Regex BibErrorRe = new(@"Citation `([^']+)' .* undefined|I found no \\bibdata|I found no \\citation", RegexOptions.Compiled);

    public record ParsedLatexError(
        string Category,
        string? Token,
        int? LineNumber,
        string ErrorRaw
    );

    /// <summary>
    /// Parse pdflatex log/stderr output and return a structured error record.
    /// Returns null if the log indicates success (no fatal error).
    /// </summary>
    public static ParsedLatexError? Parse(string logOrStderr)
    {
        if (string.IsNullOrWhiteSpace(logOrStderr)) return null;

        // Grab the first 2 000 chars — error is always near the top
        var log = logOrStderr.Length > 2000 ? logOrStderr[..2000] : logOrStderr;

        // Extract line number (first l.N occurrence)
        int? lineNumber = null;
        var lineMatch = LineNumberRe.Match(log);
        if (lineMatch.Success && int.TryParse(lineMatch.Groups[1].Value, out var ln))
            lineNumber = ln;

        // Raw excerpt: first 5 lines that start with ! or contain "Error"
        var errorLines = log.Split('\n')
            .Where(l => l.StartsWith("!") || l.Contains("Error"))
            .Take(5)
            .ToArray();
        var errorRaw = string.Join("\n", errorLines);
        if (errorRaw.Length > 1000) errorRaw = errorRaw[..1000];

        // ── Category detection (order matters — most specific first) ──────────

        // Undefined control sequence
        if (UndefinedCsRe.IsMatch(log))
        {
            // The token is on the line immediately after the ! line, e.g.:
            //   ! Undefined control sequence.
            //   l.15 \missingcmd
            var token = ExtractUndefinedCsToken(log);
            return new ParsedLatexError("undefined_control_sequence", token, lineNumber, errorRaw);
        }

        // Package error
        var pkgMatch = PackageErrorRe.Match(log);
        if (pkgMatch.Success)
            return new ParsedLatexError("package_error", pkgMatch.Groups[1].Value, lineNumber, errorRaw);

        // Undefined environment
        var envMatch = UndefinedEnvRe.Match(log);
        if (envMatch.Success)
            return new ParsedLatexError("undefined_environment", envMatch.Groups[1].Value, lineNumber, errorRaw);

        // Environment mismatch
        var mismatchMatch = EnvMismatchRe.Match(log);
        if (mismatchMatch.Success)
            return new ParsedLatexError("environment_mismatch",
                $"{mismatchMatch.Groups[1].Value}≠{mismatchMatch.Groups[2].Value}", lineNumber, errorRaw);

        // Missing file / package not found
        var fileMatch = MissingFileRe.Match(log);
        if (fileMatch.Success)
            return new ParsedLatexError("missing_file", fileMatch.Groups[1].Value.Trim(), lineNumber, errorRaw);

        // Math mode errors
        if (MathModeRe.IsMatch(log))
            return new ParsedLatexError("math_mode_error", null, lineNumber, errorRaw);

        // Bibliography errors
        var bibMatch = BibErrorRe.Match(log);
        if (bibMatch.Success)
            return new ParsedLatexError("bibliography_error",
                bibMatch.Groups[1].Success ? bibMatch.Groups[1].Value : null, lineNumber, errorRaw);

        // Generic LaTeX syntax error
        if (log.Contains("Emergency stop") || log.Contains("Fatal error") || errorLines.Length > 0)
            return new ParsedLatexError("syntax_error", null, lineNumber, errorRaw);

        // Timeout is handled separately by the caller, but catch the message here
        if (log.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return new ParsedLatexError("timeout", null, null, "Compilation timed out");

        return new ParsedLatexError("unknown", null, lineNumber, errorRaw);
    }

    private static string? ExtractUndefinedCsToken(string log)
    {
        // After "! Undefined control sequence." pdflatex prints a context line like:
        //   l.15 \missingcommand
        // or:
        //   l.15 ...me text \missingcommand
        var lines = log.Split('\n');
        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (lines[i].TrimStart().StartsWith("! Undefined control sequence"))
            {
                // Look in the next 1-3 lines for the l.N context line
                for (int j = i + 1; j < Math.Min(i + 4, lines.Length); j++)
                {
                    var m = UndefinedCsTokenRe.Match(lines[j]);
                    if (m.Success)
                        return "\\" + m.Groups[1].Value;
                }
            }
        }
        return null;
    }
}
