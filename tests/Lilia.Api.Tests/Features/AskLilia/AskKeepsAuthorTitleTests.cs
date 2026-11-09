using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Moq;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// apply_lml keeps a title the author chose (9 Nov 2026: the launch bot's "Thesis chapter" became "Renewable Energy
/// Storage" when Ask Lilia drafted into it). Only an empty or placeholder title is named by the draft.
/// </summary>
public class AskKeepsAuthorTitleTests
{
    [Theory]
    [InlineData("Thesis chapter")]
    [InlineData("Article court")]
    [InlineData("Untitled thoughts on graphs")]
    public void A_title_the_author_chose_is_kept(string title) =>
        AskLiliaService.HasAuthorTitle(title).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Untitled")]
    [InlineData("Untitled Document")]
    [InlineData("untitled document")]
    [InlineData("Document sans titre")]
    [InlineData("Sans titre")]
    [InlineData("Documento sin título")]
    [InlineData("Sin título")]
    public void An_empty_or_placeholder_title_can_be_named_by_the_draft(string? title) =>
        AskLiliaService.HasAuthorTitle(title).Should().BeFalse();

    // 9 Nov 2026, bot task #49: apply_lml kept "My named paper", then the draft called set_title and renamed it.
    [Theory]
    [InlineData("Draft a 2-page paper on sleep and memory")]
    [InlineData("Rédige un article de deux pages sur le sommeil")]
    [InlineData("Add a table comparing three sleep conditions")]
    public async Task Set_title_keeps_the_authors_title_when_the_request_does_not_ask_for_one(string question)
    {
        var h = new AskLiliaHarness();
        var doc = AskLiliaHarness.Doc() with { Title = "My named paper" };
        var tools = h.Tools(doc, allowWrite: true, question: question);

        var r = await AskLiliaHarness.Call(AskLiliaHarness.Fn(tools, "set_title"), ("title", "Sleep and Memory: Mechanisms of Consolidation"));

        r.GetProperty("error").GetString().Should().Contain("My named paper");
        h.Blocks.Verify(b => b.CreateBlockAsync(It.IsAny<Guid>(), It.IsAny<CreateBlockDto>()), Times.Never);
    }

    [Theory]
    [InlineData("Rename it to Sleep and Memory", "My named paper")]
    [InlineData("Change the title to Sleep and Memory", "My named paper")]
    [InlineData("Draft a paper titled Sleep and Memory", "My named paper")]
    [InlineData("Change le titre en Sommeil et mémoire", "My named paper")]
    [InlineData("Cambia el título", "My named paper")]
    [InlineData("Draft a 2-page paper on sleep and memory", "Untitled document")]
    public async Task Set_title_writes_when_asked_or_when_the_title_is_a_placeholder(string question, string title)
    {
        var h = new AskLiliaHarness();
        h.Blocks.Setup(b => b.CreateBlockAsync(It.IsAny<Guid>(), It.IsAny<CreateBlockDto>()))
            .ReturnsAsync(AskLiliaHarness.Block(Guid.NewGuid(), "title"));
        var doc = AskLiliaHarness.Doc() with { Title = title };
        var tools = h.Tools(doc, allowWrite: true, question: question);

        var r = await AskLiliaHarness.Call(AskLiliaHarness.Fn(tools, "set_title"), ("title", "Sleep and Memory"));

        r.GetProperty("ok").GetBoolean().Should().BeTrue();
        h.Blocks.Verify(b => b.CreateBlockAsync(doc.Id, It.IsAny<CreateBlockDto>()), Times.Once);
    }

    [Fact]
    public async Task Set_title_with_the_same_title_sets_only_author_and_date()
    {
        var h = new AskLiliaHarness();
        h.Blocks.Setup(b => b.CreateBlockAsync(It.IsAny<Guid>(), It.IsAny<CreateBlockDto>()))
            .ReturnsAsync(AskLiliaHarness.Block(Guid.NewGuid(), "title"));
        var doc = AskLiliaHarness.Doc() with { Title = "My named paper" };
        var tools = h.Tools(doc, allowWrite: true, question: "Add me as author: Ana Ruiz");

        var r = await AskLiliaHarness.Call(AskLiliaHarness.Fn(tools, "set_title"), ("title", "my named paper "), ("author", "Ana Ruiz"));

        r.GetProperty("ok").GetBoolean().Should().BeTrue();
    }
}
