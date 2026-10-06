using System.Text;
using System.Text.Json;

namespace Lilia.Engines;

/// <summary>
/// A slide block as a beamer frame. One writer for the preview (RenderService) and the .tex/.zip
/// and PDF export (LaTeXExportService), each passing its own escaping and inline formatting.
/// Only valid when the document class is beamer: a slide block in an article doc makes LaTeX
/// complain about the frame environment, an accepted v1 trade-off.
/// </summary>
public static class SlideLatex
{
    /// <param name="content">The slide block's content: title, subtitle, content, notes, layout.</param>
    /// <param name="escape">Plain-text escaping for the frame title and subtitle.</param>
    /// <param name="inline">Inline formatting (math, bold, …) for the body and the notes.</param>
    public static string Frame(JsonElement content, Func<string, string> escape, Func<string, string> inline)
    {
        var title = content.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        var subtitle = content.TryGetProperty("subtitle", out var s) ? s.GetString() ?? "" : "";
        var body = content.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        var notes = content.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
        var layout = content.TryGetProperty("layout", out var l) ? l.GetString() ?? "default" : "default";

        var sb = new StringBuilder();
        sb.Append("\\begin{frame}");
        if (!string.IsNullOrWhiteSpace(title))
            sb.Append('{').Append(escape(title)).Append('}');
        if (!string.IsNullOrWhiteSpace(subtitle))
            sb.Append('{').Append(escape(subtitle)).Append('}');
        sb.AppendLine();

        var rendered = inline(body);
        switch (layout)
        {
            case "centered":
                sb.AppendLine("\\centering").AppendLine(rendered);
                break;
            case "title-only":
                // Body suppressed for title-only slides.
                break;
            case "two-column":
                // Split on the first line containing "---" (user convention).
                var split = rendered.Split(new[] { "\n---\n" }, 2, StringSplitOptions.None);
                sb.AppendLine("\\begin{columns}");
                sb.AppendLine("\\column{0.5\\textwidth}").AppendLine(split[0]);
                if (split.Length > 1)
                    sb.AppendLine("\\column{0.5\\textwidth}").AppendLine(split[1]);
                sb.AppendLine("\\end{columns}");
                break;
            default:
                sb.AppendLine(rendered);
                break;
        }

        if (!string.IsNullOrWhiteSpace(notes))
            sb.Append("\\note{").Append(inline(notes)).AppendLine("}");
        sb.Append("\\end{frame}");
        return sb.ToString();
    }

    /// <summary>
    /// In a beamer deck a footnote block belongs to the slide before it: left between frames,
    /// beamer gave it a page of its own (6 Oct 2026). Moves each such footnote inside the nearest
    /// preceding frame, just before its \end{frame}, and empties its own chunk. A footnote before
    /// the first slide stays where it is.
    /// </summary>
    /// <param name="types">The block types, in order.</param>
    /// <param name="rendered">Each block's LaTeX, in the same order; changed in place.</param>
    public static void AttachFootnotes(IReadOnlyList<string> types, IList<string> rendered)
    {
        var lastFrame = -1;
        for (var i = 0; i < rendered.Count && i < types.Count; i++)
        {
            if (string.Equals(types[i], "footnote", StringComparison.OrdinalIgnoreCase))
            {
                if (lastFrame < 0 || string.IsNullOrWhiteSpace(rendered[i])) continue;
                var frame = rendered[lastFrame];
                var end = frame.LastIndexOf("\\end{frame}", StringComparison.Ordinal);
                if (end < 0) continue;
                rendered[lastFrame] = frame[..end] + rendered[i].Trim() + "\n" + frame[end..];
                rendered[i] = "";
            }
            else if (string.Equals(types[i], "slide", StringComparison.OrdinalIgnoreCase)
                     && rendered[i].Contains("\\end{frame}", StringComparison.Ordinal))
            {
                lastFrame = i;
            }
        }
    }
}
