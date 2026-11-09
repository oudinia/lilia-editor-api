using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// "Supported" as in backed by evidence is not a question about what Lilia supports (9 Nov 2026): the cost
/// experiments' "Which claim in the chapter is least supported?" went to Coverage every time.
/// </summary>
public class AskEvidenceRoutingTests
{
    private readonly AskLiliaRouter _router = new();

    [Theory]
    [InlineData("Which claim in the chapter is least supported?")]
    [InlineData("Is my conclusion well supported by the data?")]
    [InlineData("Which results are poorly supported?")]
    [InlineData("Find the unsupported claims in my discussion.")]
    [InlineData("Are these statements supported by the literature?")]
    public void Backed_by_evidence_is_not_coverage(string message)
    {
        // Since the Review skill (9 Nov 2026), these are review questions.
        _router.Route(message).SkillId.Should().Be("lilia-review");
    }

    [Theory]
    [InlineData("Is pstricks supported?")]
    [InlineData("Is the chemfig package supported in Lilia?")]
    [InlineData("Does Lilia support beamer themes?")]
    [InlineData("Is this supported: \\usepackage{forest}")]
    public void Lilia_support_is_still_coverage(string message)
    {
        _router.Route(message).SkillId.Should().Be("lilia-coverage");
    }
}
