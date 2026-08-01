namespace Lilia.Core.Capabilities;

/// <summary>
/// What a requirement is being resolved against.
///
/// <para><b>This is the half that was missing.</b> Three catalogues carry a
/// <c>coverage_level</c> column on the subject, which makes coverage a property
/// of the thing rather than a relation between the thing and a target. So the
/// schema can record that <c>\setmainfont</c> is <c>none</c> — but not that it
/// is <c>none</c> under pdflatex and <c>full</c> under lualatex, which is the
/// only form of the answer anyone can act on:</para>
///
/// <code>
/// subject          pdflatex     lualatex
/// \setmainfont     none         full
/// U+03B3           shimmed      native
/// U+4E2D           impossible   full, given a CJK font
/// </code>
///
/// <para>Naming the target explicitly is most of the reframe.</para>
/// </summary>
public enum RenderTarget
{
    /// <summary>The default engine. No system fonts, no Unicode input.</summary>
    Pdflatex,

    /// <summary>System fonts via fontspec, full Unicode.</summary>
    Xelatex,

    /// <summary>As xelatex, plus Lua. The usual answer for complex scripts.</summary>
    Lualatex,

    /// <summary>The fast tier. No engine/font layer to fail in the same ways.</summary>
    Typst,
}

public static class RenderTargets
{
    /// <summary>Every target, for building a coverage matrix.</summary>
    public static readonly IReadOnlyList<RenderTarget> All =
    [
        RenderTarget.Pdflatex, RenderTarget.Xelatex, RenderTarget.Lualatex, RenderTarget.Typst,
    ];

    /// <summary>
    /// The wire spelling, matching the engine names already used by
    /// <c>LaTeXRenderService</c>, the <c>X-Render-Engine</c> header and the
    /// <c>engine</c> query parameter — so a verdict can be reported against the
    /// same vocabulary a caller already sees.
    /// </summary>
    public static string ToWireName(this RenderTarget target) => target switch
    {
        RenderTarget.Pdflatex => "pdflatex",
        RenderTarget.Xelatex => "xelatex",
        RenderTarget.Lualatex => "lualatex",
        RenderTarget.Typst => "typst",
        _ => target.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Parse an engine name. Returns false rather than guessing — an
    /// unrecognised engine must not silently become pdflatex, which is the one
    /// target least able to render the things people ask about.
    /// </summary>
    public static bool TryParse(string? name, out RenderTarget target)
    {
        target = RenderTarget.Pdflatex;
        if (string.IsNullOrWhiteSpace(name)) return false;

        switch (name.Trim().ToLowerInvariant())
        {
            case "pdflatex": target = RenderTarget.Pdflatex; return true;
            case "xelatex": target = RenderTarget.Xelatex; return true;
            case "lualatex": target = RenderTarget.Lualatex; return true;
            case "typst": target = RenderTarget.Typst; return true;
            default: return false;
        }
    }
}
