using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

public class AskLiliaStripLmlTests
{
    [Fact]
    public void StripLmlFences_ExtractsFencedBlock()
    {
        var raw = "Here is the LML:\n\n```lml\n@paragraph\n  Hello\n```\n\nThanks.";
        var got = AskLiliaService.StripLmlFences(raw);
        Assert.Contains("@paragraph", got);
        Assert.Contains("Hello", got);
        Assert.DoesNotContain("```", got);
        Assert.DoesNotContain("Thanks", got);
    }

    [Fact]
    public void StripLmlFences_PlainLmlUnchanged()
    {
        var raw = "@heading[level=1]\n  Intro\n";
        Assert.Equal(raw.Trim(), AskLiliaService.StripLmlFences(raw));
    }

    [Theory]
    [InlineData("write a cv for Albert Einstein", "lilia-document-architect")]
    [InlineData("draft my resume please", "lilia-document-architect")]
    [InlineData("curriculum vitae for a physicist", "lilia-document-architect")]
    public void Router_CvMessages_RouteToArchitect(string message, string expectedSkill)
    {
        var router = new AskLiliaRouter();
        var route = router.Route(message);
        Assert.Equal(expectedSkill, route.SkillId);
    }

    [Theory]
    [InlineData("cv", "cv")]
    [InlineData("resume", "cv")]
    [InlineData("résumé", "cv")]
    [InlineData("paper", "article")]
    [InlineData("thesis", "book")]
    [InlineData("report", "report")]
    [InlineData("book", "book")]
    [InlineData("article", "article")]
    [InlineData("not-a-kind", null)]
    public void NormalizeCategory_MapsAliases(string raw, string? expected)
    {
        Assert.Equal(expected, AskLiliaService.NormalizeCategory(raw));
    }

    [Theory]
    [InlineData("moderncv", "cv")]
    [InlineData("altacv", "cv")]
    [InlineData("book", "book")]
    [InlineData("report", "report")]
    [InlineData("article", "article")]
    [InlineData("memoir", "book")]
    public void CategoryFromClass_InfersKind(string cls, string expected)
    {
        Assert.Equal(expected, AskLiliaService.CategoryFromClass(cls));
    }
}
