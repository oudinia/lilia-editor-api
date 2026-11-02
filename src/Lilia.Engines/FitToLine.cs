namespace Lilia.Engines;

/// <summary>
/// Scales what it wraps down to the line's width when it is wider, and leaves it alone when it fits
/// (2 Nov 2026: a table wider than its column in a two-column paper printed over the other column).
/// graphicx's <c>\resizebox</c> knows the content's natural <c>\width</c> inside its own arguments, so
/// the choice is made by TeX at compile time, from the real width. graphicx is in every preamble.
/// </summary>
public static class FitToLine
{
    public const string Open = @"\resizebox{\ifdim\width>\linewidth\linewidth\else\width\fi}{!}{%";
    public const string Close = "}";
}
