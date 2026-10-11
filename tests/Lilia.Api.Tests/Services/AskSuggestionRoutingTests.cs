using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The suggestions Ask Lilia offers in the editor (web: AskLiliaPanel's SUGGESTIONS
/// and READER_SUGGESTIONS) each land on the skill they stand for. The user, 11 Oct:
/// the suggestions were "not adapted to Lilia's skills" ("Summarise this document"
/// went to the document architect, which drafts). Change both lists together.
/// </summary>
public class AskSuggestionRoutingTests
{
    private readonly AskLiliaRouter _router = new();

    [Theory]
    [InlineData("Draft an outline: sections with one line each", "lilia-document-architect")]
    [InlineData("Cite a paper from its DOI or arXiv id", "lilia-citations")]
    [InlineData("Why won't this compile? Fix this error", "lilia-compile-doctor")]
    [InlineData("Write the equation for…", "lilia-equation")]
    [InlineData("Turn this data into a booktabs table", "lilia-table")]
    [InlineData("Draw a TikZ diagram of…", "lilia-figure")]
    [InlineData("Polish the selected paragraph", "lilia-polish")]
    [InlineData("Format for IEEE, two-column", "lilia-journal")]
    // A reader (view only): questions, answered by the tutor.
    [InlineData("Explain this document in five lines", "lilia-tutor")]
    [InlineData("Explain the main result in plain words", "lilia-tutor")]
    [InlineData("What is each equation saying?", "lilia-tutor")]
    [InlineData("Explain which sources it relies on", "lilia-tutor")]
    public void Each_suggestion_reaches_its_skill(string suggestion, string skill)
    {
        _router.Route(suggestion).SkillId.Should().Be(skill);
    }
}
