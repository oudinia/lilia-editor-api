using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>MCP-95 / MCP-99 (run 3): one reading of a list's kind for every emitter.</summary>
public class ListKindTests
{
    [Theory]
    [InlineData("""{"kind":"numbered"}""", "numbered")]
    [InlineData("""{"listType":"numbered"}""", "numbered")]
    [InlineData("""{"listType":"ordered"}""", "numbered")]
    [InlineData("""{"ordered":true}""", "numbered")]
    [InlineData("""{"kind":"description"}""", "description")]
    [InlineData("""{"listType":"description"}""", "description")]
    [InlineData("""{"kind":"bullet","ordered":true}""", "bullet")]
    [InlineData("""{}""", "bullet")]
    public void Every_way_of_writing_it(string json, string kind) =>
        ListKind.Of(JsonDocument.Parse(json).RootElement).Should().Be(kind);

    [Fact]
    public void A_numbered_kind_is_an_enumerate_in_latex()
    {
        var block = new Block
        {
            Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "list",
            Content = JsonDocument.Parse("""{"kind":"numbered","items":[{"text":"One"}]}"""),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLatex(block).Should().Contain(@"\begin{enumerate}");
    }
}
