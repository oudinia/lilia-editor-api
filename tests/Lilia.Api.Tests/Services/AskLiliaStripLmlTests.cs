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
}
