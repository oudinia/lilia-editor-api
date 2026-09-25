namespace Lilia.Engines;

/// <summary>
/// The key a block's label is known by in the compiled document — the one
/// place that decides it, used by the exporter that writes <c>\label</c> and by
/// the reference index that checks what points at it.
///
/// <para><b>Two conventions exist, and both must keep resolving.</b> A bare
/// name — <c>"main"</c> on an equation — is prefixed with its kind, and is
/// referenced as <c>eq:main</c>; that is deliberate and tested
/// (LaTeXScenarioTests.CrossReferences_EquationAndRef). A full key —
/// <c>"tab:results"</c>, as LaTeX import, the table tool and the derived label
/// chip all produce — is the key already, and is written as it is.</para>
///
/// <para>The exporter used to prefix both. A table's <c>tab:results</c> was
/// written <c>\label{tbl:tab:results}</c>, so the label the PDF defined never
/// matched the key every reference named, and every such reference printed
/// <c>??</c>. The prefix was meant for bare names; the colon is how to tell
/// the two apart.</para>
/// </summary>
public static class LabelKey
{
    /// <summary>The exporter's historic prefix per block kind, for bare names.</summary>
    private static readonly Dictionary<string, string> BarePrefix = new(StringComparer.OrdinalIgnoreCase)
    {
        ["equation"] = "eq",
        ["figure"] = "fig",
        ["table"] = "tbl",
        ["algorithm"] = "alg",
        ["theorem"] = "thm",
    };

    /// <summary>
    /// The compiled key for a label on a block of <paramref name="blockType"/>.
    /// Empty stays empty — a block with no label defines nothing.
    /// </summary>
    public static string Effective(string blockType, string? label)
    {
        var l = label?.Trim() ?? "";
        if (l.Length == 0) return "";
        if (l.Contains(':')) return l;
        return BarePrefix.TryGetValue(blockType, out var prefix) ? $"{prefix}:{l}" : l;
    }
}
