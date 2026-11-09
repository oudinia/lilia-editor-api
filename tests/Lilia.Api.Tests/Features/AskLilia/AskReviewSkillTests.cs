using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// The Review skill (9 Nov 2026, the owner): direct written feedback on the author's own document. Its questions went to
/// Compile Doctor, Coverage or the Tutor by accident; it never writes, like Challenge.
/// </summary>
public class AskReviewSkillTests
{
    private readonly AskLiliaRouter _router = new();

    [Theory]
    [InlineData("Review my paper and tell me what to fix")]
    [InlineData("Which section is weakest, and why?")]
    [InlineData("Which claim in the chapter is least supported?")]
    [InlineData("Is my sample size enough for these results?")]
    [InlineData("What would a tough reviewer say about this paper?")]
    [InlineData("What is missing from this paper before I submit it?")]
    [InlineData("Quels sont les points faibles de mon article ?")]
    [InlineData("Relis mon chapitre et donne-moi un avis")]
    [InlineData("¿Cuáles son los puntos débiles de mi artículo?")]
    [InlineData("Revisa mi artículo y dime qué falta")]
    public void Review_questions_reach_review(string message) =>
        _router.Route(message).SkillId.Should().Be("lilia-review");

    [Theory]
    [InlineData("Challenge me as reviewer 2", "lilia-challenge")]
    [InlineData("Rehearse my thesis defence with me", "lilia-challenge")]
    [InlineData("Polish the related work section", "lilia-polish")]
    [InlineData("Is pstricks supported?", "lilia-coverage")]
    [InlineData("Draft a short research paper on sleep and memory", "lilia-document-architect")]
    public void Neighbouring_skills_keep_theirs(string message, string skill) =>
        _router.Route(message).SkillId.Should().Be(skill);

    [Fact]
    public void Review_never_writes_and_has_its_guidance()
    {
        AskLiliaService.IsReadOnlySkill("lilia-review").Should().BeTrue();
        AskLiliaService.IsReadOnlySkill("lilia-challenge").Should().BeTrue();
        AskLiliaService.IsReadOnlySkill("lilia-document-architect").Should().BeFalse();
        AiSkillGuidance.Get("lilia-review").Should().Contain("Lilia Review").And.Contain("Never edit the document")
            .And.Contain("Never show\nblock ids");
    }
}
