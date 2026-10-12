using System.Globalization;
using System.Text.Json;

namespace Lilia.Engines;

/// <summary>
/// An image figure's width as LaTeX. The stored value is a fraction of the column (0.95), or a
/// percentage (95, "95%"). It printed rounded to one decimal (0.9) and against <c>\textwidth</c>,
/// which in two columns is the whole page: the figure overflowed its column (MCP-62). The
/// exporter ignored it altogether.
/// </summary>
public static class FigureWidth
{
    public const double Default = 0.8;

    /// <summary>The fraction of the line, clamped to 0.05–1.</summary>
    public static double Fraction(JsonElement content)
    {
        double? raw = null;
        if (content.ValueKind == JsonValueKind.Object && content.TryGetProperty("width", out var w))
        {
            if (w.ValueKind == JsonValueKind.Number && w.TryGetDouble(out var d)) raw = d;
            else if (w.ValueKind == JsonValueKind.String
                     && double.TryParse((w.GetString() ?? "").Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                raw = p;
        }
        if (raw is not { } v || double.IsNaN(v) || v <= 0) return Default;
        // A little over 1 is a fraction too wide for the line; 5 and more is a percentage.
        if (v > 1) v = v < 5 ? 1 : v / 100;
        return Math.Clamp(v, 0.05, 1);
    }

    /// <summary>e.g. <c>0.95\linewidth</c>: the column inside a figure, the page inside figure*.</summary>
    public static string Latex(JsonElement content) =>
        Fraction(content).ToString("0.###", CultureInfo.InvariantCulture) + @"\linewidth";
}
