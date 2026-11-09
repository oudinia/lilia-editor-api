using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// A long answer isn't cut off at 100 s (9 Nov 2026): the cost experiments' 14th turn of a long conversation failed
/// after 102 s on "the configured HttpClient.Timeout of 100 seconds", .NET's default, while the SDK's own timeout
/// was 10 minutes. The HttpClient now has none, and the SDK's timeout governs.
/// </summary>
public class AnthropicClientFactoryTests
{
    [Fact]
    public void The_http_client_never_times_out_first()
    {
        var client = AnthropicClientFactory.Create("sk-test");
        client.HttpClient.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
        client.Timeout.Should().Be(TimeSpan.FromSeconds(AnthropicClientFactory.DefaultTimeoutSeconds));
    }

    [Fact]
    public void The_timeout_is_the_configured_one()
    {
        AnthropicClientFactory.Create("sk-test", 120).Timeout.Should().Be(TimeSpan.FromSeconds(120));
    }

    /// <summary>A chapter written as text stopped mid-sentence at 8,192 and reached the document empty.</summary>
    [Fact]
    public void A_long_answer_has_room_for_a_chapter()
    {
        AskLiliaService.MaxOutputTokens.Should().BeGreaterThanOrEqualTo(16384);
    }
}
