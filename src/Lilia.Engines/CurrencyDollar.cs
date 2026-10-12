using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// A dollar that is a price, not maths. "costs $5 and … costs $10" paired the two signs as one
/// inline formula and set the words between them as maths (MCP-16). A <c>$</c> followed by an
/// amount and then a word, punctuation or the end of the text is a price; <c>$5 + 3$</c>,
/// <c>$2x$</c> and <c>$5$</c> stay maths.
/// </summary>
public static class CurrencyDollar
{
    private static readonly Regex Price = new(
        @"(?<![\\$])\$(?=\d[\d,]*(?:\.\d+)?(?:\s+[A-Za-z]|\s*$|[,.;:!?)\]]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Each price sign handed to <paramref name="protect"/> as <c>\$</c>.</summary>
    public static string Protect(string text, Func<string, string> protect) =>
        string.IsNullOrEmpty(text) || text.IndexOf('$') < 0 ? text : Price.Replace(text, _ => protect(@"\$"));
}
