using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.AI;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for <see cref="WebCitationExtractor"/> — verifies that web-search
/// citations (MEAI <see cref="CitationAnnotation"/> entries on the answer text)
/// are pulled into structured <see cref="WebCitation"/>s, deduped by URL.
/// </summary>
public class WebCitationExtractorTests
{
    private static ChatResponse ResponseWith(params CitationAnnotation[] annotations)
    {
        var text = new TextContent("answer")
        {
            Annotations = annotations.Cast<AIAnnotation>().ToList(),
        };
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent> { text }));
    }

    [Fact]
    public void Extract_PullsTitleUrlSnippet()
    {
        var resp = ResponseWith(new CitationAnnotation
        {
            Title = "FIFA — Golden Boot",
            Url = new Uri("https://www.fifa.com/golden-boot"),
            Snippet = "Messi leads with 7 goals",
        });

        var cites = WebCitationExtractor.Extract(resp);

        cites.Should().ContainSingle();
        cites[0].Title.Should().Be("FIFA — Golden Boot");
        cites[0].Url.Should().Be("https://www.fifa.com/golden-boot");
        cites[0].Snippet.Should().Be("Messi leads with 7 goals");
    }

    [Fact]
    public void Extract_DedupesByUrl_AndDropsUrlless()
    {
        var resp = ResponseWith(
            new CitationAnnotation { Title = "A", Url = new Uri("https://ex.com/x") },
            new CitationAnnotation { Title = "A (dup)", Url = new Uri("https://ex.com/x") },
            new CitationAnnotation { Title = "no url", Url = null });

        var cites = WebCitationExtractor.Extract(resp);

        cites.Should().ContainSingle();
        cites[0].Url.Should().Be("https://ex.com/x");
        cites[0].Title.Should().Be("A"); // first wins
    }

    [Fact]
    public void Extract_Empty_WhenNoAnnotations()
    {
        var resp = new ChatResponse(new ChatMessage(ChatRole.Assistant, "plain answer"));
        WebCitationExtractor.Extract(resp).Should().BeEmpty();
    }
}
