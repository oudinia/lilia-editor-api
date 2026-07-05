using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.AI;

namespace Lilia.Api.Services;

/// <summary>Per-type byte ceilings + text cap for attachment processing.</summary>
public sealed record AttachmentLimits(int MaxImageBytes, int MaxPdfBytes, int MaxFileBytes, int MaxTextChars);

/// <summary>
/// Turns request attachments into model content parts:
///   • images/PDFs → native <see cref="DataContent"/> (provider image/document blocks);
///   • .docx → OpenXML plain-text extraction, inlined;
///   • text files → inlined fenced.
/// Oversized / unreadable / unsupported items become a short text note so nothing
/// silently vanishes. Pure + limit-injected, so it's unit-testable without the
/// full service.
/// </summary>
public static class AskAttachmentBuilder
{
    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "csv", "tsv", "tex", "bib", "txt", "md", "markdown", "json", "log" };

    public static IReadOnlyList<AIContent> Build(IReadOnlyList<AskAttachment>? attachments, AttachmentLimits limits)
    {
        if (attachments is null || attachments.Count == 0) return Array.Empty<AIContent>();

        var parts = new List<AIContent>();
        foreach (var att in attachments)
        {
            if (att is null || string.IsNullOrWhiteSpace(att.DataBase64)) continue;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(att.DataBase64); }
            catch { parts.Add(new TextContent($"[Attachment \"{att.Name}\" could not be decoded.]")); continue; }

            var media = (att.MediaType ?? string.Empty).ToLowerInvariant();
            var ext = (att.Name.Contains('.') ? att.Name[(att.Name.LastIndexOf('.') + 1)..] : string.Empty).ToLowerInvariant();

            if (media.StartsWith("image/"))
            {
                if (Oversized(parts, att.Name, bytes.Length, limits.MaxImageBytes)) continue;
                parts.Add(new DataContent(bytes, media));
            }
            else if (media == "application/pdf" || ext == "pdf")
            {
                if (Oversized(parts, att.Name, bytes.Length, limits.MaxPdfBytes)) continue;
                parts.Add(new DataContent(bytes, "application/pdf"));
            }
            else if (ext == "docx" || media.Contains("wordprocessingml"))
            {
                if (Oversized(parts, att.Name, bytes.Length, limits.MaxFileBytes)) continue;
                var text = ExtractDocxText(bytes);
                parts.Add(new TextContent(text is null
                    ? $"[Attachment \"{att.Name}\" — could not read the .docx.]"
                    : $"Attached document \"{att.Name}\":\n\n{Cap(text, limits.MaxTextChars)}"));
            }
            else if (media.StartsWith("text/") || TextExtensions.Contains(ext))
            {
                if (Oversized(parts, att.Name, bytes.Length, limits.MaxFileBytes)) continue;
                parts.Add(new TextContent($"Attached file \"{att.Name}\":\n\n```\n{Cap(Encoding.UTF8.GetString(bytes), limits.MaxTextChars)}\n```"));
            }
            else
            {
                parts.Add(new TextContent($"[Attachment \"{att.Name}\" — unsupported type; convert it to text/CSV, an image, or PDF.]"));
            }
        }
        return parts;
    }

    private static bool Oversized(List<AIContent> parts, string name, int actual, int max)
    {
        if (actual <= max) return false;
        parts.Add(new TextContent($"[Attachment \"{name}\" is too large ({actual / (1024 * 1024)} MB; max {max / (1024 * 1024)} MB).]"));
        return true;
    }

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max] + "\n…(truncated)";

    // Lightweight .docx → plain text (paragraph-per-line) via OpenXML — avoids
    // the heavy import pipeline / external services for an inline attachment.
    private static string? ExtractDocxText(byte[] bytes)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(bytes);
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return null;
            var sb = new StringBuilder();
            foreach (var para in body.Descendants<Paragraph>())
            {
                var t = para.InnerText;
                if (!string.IsNullOrWhiteSpace(t)) sb.AppendLine(t);
            }
            var text = sb.Length > 0 ? sb.ToString() : body.InnerText;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch { return null; }
    }
}
