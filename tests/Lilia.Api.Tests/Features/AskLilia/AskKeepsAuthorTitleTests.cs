using FluentAssertions;
using Lilia.Api.Services;
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
}
