using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// The compile verdict names a failing block as the author sees it (9 Nov 2026: it listed raw block ids).
/// </summary>
public class AskBlockPlaceTests
{
    private static BlockDto B(Guid id, string type, string json, int order) =>
        new(id, Guid.Empty, type, JsonDocument.Parse(json).RootElement.Clone(), order, null, 0, DateTime.UtcNow, DateTime.UtcNow);

    private readonly Guid _eq = Guid.NewGuid();
    private readonly Guid _first = Guid.NewGuid();
    private List<BlockDto> Doc() =>
    [
        B(_first, "paragraph", """{"text":"Opening"}""", 0),
        B(Guid.NewGuid(), "heading", """{"text":"Background","level":1}""", 1),
        B(Guid.NewGuid(), "paragraph", """{"text":"Some text"}""", 2),
        B(_eq, "equation", """{"latex":"\\badcommand"}""", 3),
    ];

    [Theory]
    [InlineData("en", "the equation in “Background”")]
    [InlineData("fr", "l'équation dans « Background »")]
    [InlineData("es", "la ecuación en «Background»")]
    public void A_block_is_named_by_its_kind_and_section(string lang, string expected) =>
        AskLiliaService.BlockPlace(Doc(), _eq.ToString(), lang).Should().Be(expected);

    [Fact]
    public void A_block_before_any_heading_is_at_the_top() =>
        AskLiliaService.BlockPlace(Doc(), _first.ToString(), "en").Should().Be("the paragraph at the top");

    [Fact]
    public void An_unknown_block_falls_back_to_its_id()
    {
        var id = Guid.NewGuid().ToString();
        AskLiliaService.BlockPlace(Doc(), id, "en").Should().Be($"`{id}`");
        AskLiliaService.BlockPlace(null, id, "en").Should().Be($"`{id}`");
    }
}
