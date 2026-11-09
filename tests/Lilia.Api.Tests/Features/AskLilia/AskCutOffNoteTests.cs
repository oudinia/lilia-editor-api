using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// A reply the model stopped at the output limit says so (9 Nov 2026): a chapter written as text stopped mid-word,
/// its LML block never closed, the document stayed empty, and nothing told the author.
/// </summary>
public class AskCutOffNoteTests
{
    [Fact]
    public void A_cut_off_reply_closes_its_open_block_and_says_so()
    {
        var reply = AskLiliaService.WithCutOffNote("Here's the chapter.\n\n```lml\n@heading[level=1]\n  Intro\n\n@paragraph\n  Molecules are gra", ChatFinishReason.Length);

        reply.Should().Contain("Molecules are gra\n```");
        reply.Should().EndWith("Ask me to **continue**, or ask for one section at a time.");
        System.Text.RegularExpressions.Regex.Matches(reply, "^```", System.Text.RegularExpressions.RegexOptions.Multiline).Count
            .Should().Be(2, "the open block is closed, so the note isn't rendered as code");
    }

    [Fact]
    public void A_cut_off_reply_with_closed_blocks_only_gets_the_note()
    {
        var reply = AskLiliaService.WithCutOffNote("```lml\n@paragraph\n  Done\n```\n\nAnd then the next sec", ChatFinishReason.Length);
        reply.Should().StartWith("```lml\n@paragraph\n  Done\n```\n\nAnd then the next sec\n\n⚠️ **Cut off**");
    }

    [Fact]
    public void An_empty_cut_off_reply_is_the_note()
    {
        AskLiliaService.WithCutOffNote("", ChatFinishReason.Length).Should().StartWith("⚠️ **Cut off**");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stop")]
    [InlineData("tool_calls")]
    public void A_finished_reply_is_unchanged(string? finish)
    {
        var reason = finish switch { "stop" => ChatFinishReason.Stop, "tool_calls" => ChatFinishReason.ToolCalls, _ => (ChatFinishReason?)null };
        AskLiliaService.WithCutOffNote("```lml\n@paragraph\n  Hi", reason).Should().Be("```lml\n@paragraph\n  Hi");
    }
}
