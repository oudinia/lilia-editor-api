using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// A <c>~</c> between two characters is LaTeX's non-breaking space — <c>Theorem~\ref{…}</c>,
/// <c>Dr.~Smith</c>, the most common idiom in a paper (MCP-67, 12 Oct: every one printed a visible
/// tilde, in all 8 of the campaign's realistic documents, with a clean check). A <c>~</c> standing
/// alone or after a space — <c>~5 min</c>, "about" — stays a printed tilde.
/// </summary>
public static class TieSyntax
{
    // Not next to another ~ (that is ~~strikethrough~~). Run before LaTeX commands become
    // placeholders: those begin with "\x00C", which C# reads as \x00C — a form feed, i.e. whitespace.
    private static readonly Regex Tie = new(@"(?<=[^\s~])~(?=[^\s~])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Each tie handed to <paramref name="protect"/> so the escape pass leaves it a tie.</summary>
    public static string Protect(string text, Func<string, string> protect) =>
        string.IsNullOrEmpty(text) || text.IndexOf('~') < 0 ? text : Tie.Replace(text, _ => protect("~"));
}
