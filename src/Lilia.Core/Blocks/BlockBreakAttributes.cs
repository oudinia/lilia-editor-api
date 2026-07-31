using System.Text.Json;

namespace Lilia.Core.Blocks;

/// <summary>
/// Page-break intent expressed as attributes on a block, turned into LaTeX.
///
/// <para><b>The problem this replaces.</b> A manual <c>@pagebreak</c> block is
/// <i>positional</i>: it says "break here", not why. Insert one to stop a
/// heading being stranded, then add a paragraph above it, and every downstream
/// break is now in the wrong place — while still looking deliberate. Manual
/// breaks decay silently.</para>
///
/// <para>An attribute survives that edit, because it records the intent rather
/// than the outcome: "do not strand this heading" stays true no matter what
/// moves above it, and LaTeX recomputes where that implies a break. This is the
/// leverage the block model has that a hand-written document does not — Lilia
/// owns emission, so a checkbox becomes the right incantation.</para>
///
/// <para><c>@pagebreak</c> is kept, but demoted from the primary tool to an
/// escape hatch.</para>
///
/// <para>Every construct below was compile-verified with pdflatex before being
/// emitted anywhere (2026-07-31): <c>\Needspace</c>, <c>samepage</c>,
/// <c>\clearpage</c>, <c>\nopagebreak</c>.</para>
/// </summary>
public static class BlockBreakAttributes
{
    /// <summary>
    /// How much following space <c>keepWithNext</c> demands. Four lines is the
    /// conventional choice: enough that a heading cannot sit alone above a
    /// break with only a stray line of body text under it, small enough that it
    /// rarely forces a break of its own.
    /// </summary>
    public const string KeepWithNextSpace = @"4\baselineskip";

    /// <summary>LaTeX emitted before and after a block's own content.</summary>
    public readonly record struct Wrapping(string Before, string After)
    {
        public static readonly Wrapping None = new("", "");
        public bool IsEmpty => Before.Length == 0 && After.Length == 0;
    }

    /// <summary>
    /// Read the break attributes off a block's content and return what to emit
    /// around it.
    ///
    /// <para>Order matters and is deliberate: <c>\clearpage</c> comes first
    /// (start the new page), then <c>\Needspace</c> (reserve room on it). The
    /// reverse would reserve space on the page being abandoned.</para>
    /// </summary>
    public static Wrapping For(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return Wrapping.None;

        var before = new List<string>();
        var after = new List<string>();

        // "This block starts a fresh page" — a chapter, a new section of a
        // report. \clearpage rather than \newpage so pending floats are flushed
        // first; otherwise a figure from the previous section can drift past the
        // break and land under the new heading.
        if (ReadBool(content, "startsOnNewPage"))
            before.Add(@"\clearpage");

        // "Do not strand this heading at the foot of a page." Reserves space for
        // what follows, so LaTeX breaks BEFORE the block rather than after it.
        if (ReadBool(content, "keepWithNext"))
            before.Add($@"\Needspace{{{KeepWithNextSpace}}}");

        // "Do not split this block across pages." samepage suppresses the
        // penalties LaTeX would otherwise use to break inside.
        //
        // Note this is advice, not a guarantee: content taller than a page still
        // has to break, and samepage will not stop it — it makes LaTeX unwilling,
        // not unable. A table too tall for the page is P2.3's problem
        // (automatic longtable), not something an attribute can fix.
        if (ReadBool(content, "avoidBreakInside"))
        {
            before.Add(@"\begin{samepage}");
            after.Add(@"\end{samepage}");
        }

        return new Wrapping(
            string.Join("\n", before),
            string.Join("\n", after));
    }

    /// <summary>
    /// LaTeX float specifier for a block's <c>placement</c> attribute. Shared so
    /// figures and tables cannot drift apart — tables previously hard-coded
    /// <c>[htbp]</c> and ignored the attribute entirely, so setting "here" on a
    /// table did nothing and said nothing.
    /// </summary>
    public static string FloatSpecifier(JsonElement content) =>
        ReadString(content, "placement") switch
        {
            "here" => "[H]",     // float package; exact placement, no drifting
            "top" => "[t]",
            "bottom" => "[b]",
            "page" => "[p]",
            _ => "[htbp]",       // "auto", absent, or unrecognised
        };

    private static bool ReadBool(JsonElement content, string property) =>
        content.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static string ReadString(JsonElement content, string property) =>
        content.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
