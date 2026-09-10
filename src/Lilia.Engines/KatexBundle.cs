using System.Reflection;

namespace Lilia.Engines;

/// <summary>
/// The maths renderer an exported HTML file carries with it.
///
/// <para>An exported .html is a file someone downloads. Before this, its
/// equations reached the reader as literal dollar signs — "$a^2 + b^2 = c^2.$"
/// typeset as prose — because the page shipped no renderer at all: no MathJax,
/// no KaTeX, not a single script tag. For a LaTeX-first editor that meant the
/// HTML export worked for prose and failed at the subject matter (2026-09-09).</para>
///
/// <para>KaTeX travels inside the file — stylesheet, script and woff2 fonts as
/// data URIs — so the page typesets itself from disk, offline, years from now.
/// A CDN script would have been one line and would break the moment the reader
/// is on a plane. It costs about 627 KB, which is why it is only added to
/// documents that actually contain mathematics.</para>
/// </summary>
public static class KatexBundle
{
    private const string ResourceName = "Lilia.Engines.Assets.katex-inline.html";

    private static readonly Lazy<string> Content = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource {ResourceName} is missing. Run ops/build-katex-asset.py.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>Style, script and fonts, ready to drop into a &lt;head&gt;.</summary>
    public static string Html => Content.Value;

    /// <summary>
    /// Whether a rendered body has anything for KaTeX to do.
    ///
    /// <para>Deliberately cheap and slightly generous: a stray dollar sign
    /// costs a 627 KB bundle that renders nothing, while a missed equation
    /// costs the reader the equation. The auto-render pass ignores code and
    /// pre, so a shell-scripting paper does not get its prompts typeset as
    /// algebra either way.</para>
    /// </summary>
    public static bool IsNeededFor(string html) =>
        !string.IsNullOrEmpty(html)
        && (html.Contains('$')
            || html.Contains("\\[", StringComparison.Ordinal)
            || html.Contains("\\(", StringComparison.Ordinal));
}
