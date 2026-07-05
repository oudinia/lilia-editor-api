using Microsoft.Extensions.AI;

namespace Lilia.Api.Services;

/// <summary>
/// A source the assistant cited from live web search — surfaced to the client so
/// it can render a "Sources" list and (later) seed bibliography entries.
/// </summary>
public sealed record WebCitation(string? Title, string Url, string? Snippet);

/// <summary>
/// Pulls structured citations out of a chat response. Anthropic's web-search tool
/// attaches source references to the answer text; the Microsoft.Extensions.AI
/// adapter maps them to <see cref="CitationAnnotation"/> entries on the content's
/// <see cref="AIContent.Annotations"/>. We dedupe by URL and cap the snippet so
/// the payload stays small.
/// </summary>
public static class WebCitationExtractor
{
    private const int MaxSnippet = 300;

    public static IReadOnlyList<WebCitation> Extract(ChatResponse response)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<WebCitation>();
        foreach (var content in response.Messages.SelectMany(m => m.Contents))
        {
            if (content.Annotations is null) continue;
            foreach (var ann in content.Annotations.OfType<CitationAnnotation>())
            {
                var url = ann.Url?.ToString();
                if (string.IsNullOrWhiteSpace(url) || !seen.Add(url)) continue;
                var snippet = ann.Snippet;
                if (snippet is { Length: > MaxSnippet }) snippet = snippet[..MaxSnippet];
                list.Add(new WebCitation(ann.Title, url, snippet));
            }
        }
        return list;
    }
}
