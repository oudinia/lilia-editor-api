using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Services.Semantic;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>The semantic router's rule, without the model (19 Oct): keywords when clear, meaning otherwise.</summary>
public class SemanticRouterRuleTests
{
    private static readonly AskLiliaRouter Skills = new();
    private static AskRoute Words(string skill, double confidence) => new(skill, skill, confidence, "");

    [Fact]
    public void Clear_keywords_win_whatever_the_meaning_says() =>
        SemanticAskRouter.Decide(Words("lilia-citations", 0.8), new Classified("lilia-table", 0.6f, "lilia-figure", 0.1f), Skills)
            .SkillId.Should().Be("lilia-citations");

    [Fact]
    public void No_keywords_and_a_confident_meaning_takes_the_meaning() =>
        SemanticAskRouter.Decide(Words("lilia-document-architect", 0.2), new Classified("lilia-tutor", 0.3f, "lilia-figure", 0.29f), Skills)
            .SkillId.Should().Be("lilia-tutor");

    [Fact]
    public void A_one_word_match_is_overridden_only_by_a_clear_win()
    {
        SemanticAskRouter.Decide(Words("lilia-tutor", 0.65), new Classified("lilia-equation", 0.3f, "lilia-tutor", 0.28f), Skills)
            .SkillId.Should().Be("lilia-tutor", "a 0.02 lead is not clear");
        SemanticAskRouter.Decide(Words("lilia-tutor", 0.65), new Classified("lilia-equation", 0.3f, "lilia-tutor", 0.2f), Skills)
            .SkillId.Should().Be("lilia-equation");
    }

    [Fact]
    public void A_doubtful_meaning_leaves_the_keywords_and_no_model_changes_nothing()
    {
        SemanticAskRouter.Decide(Words("lilia-document-architect", 0.2), new Classified("lilia-tutor", 0.1f, null, 0f), Skills)
            .SkillId.Should().Be("lilia-document-architect");
        SemanticAskRouter.Decide(Words("lilia-polish", 0.65), null, Skills).SkillId.Should().Be("lilia-polish");
    }
}
