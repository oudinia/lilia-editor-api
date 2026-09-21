using System.Text.Json;

namespace Lilia.Engines;

/// <summary>
/// Getting JSON out of text that is mostly JSON.
///
/// <para>A model told to answer with an object will nearly always do it, and
/// then occasionally lead with "Here's the revised table:" or close with a
/// paragraph explaining itself. The reply is not malformed — there is a whole,
/// valid object in it, with prose either side. Throwing that away turns a
/// usable answer into an error the author cannot act on.</para>
///
/// <para>This is deliberately not a repair: it does not close unbalanced
/// braces, strip trailing commas, or guess at truncated output. It finds one
/// complete object and hands it over, or says there isn't one.</para>
/// </summary>
public static class JsonText
{
    /// <summary>
    /// The first complete JSON object in <paramref name="text"/>, or null.
    /// Brace counting is string-aware: a `{` inside a LaTeX cell value, or an
    /// escaped quote before one, must not move the depth.
    /// </summary>
    public static string? FirstObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var start = text.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth != 0) continue;

                var candidate = text[start..(i + 1)];
                // Balanced is not the same as valid — `{ "a": }` counts to zero
                // too. The parser is the only authority on that.
                try
                {
                    using var _ = JsonDocument.Parse(candidate);
                    return candidate;
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        return null;
    }
}
