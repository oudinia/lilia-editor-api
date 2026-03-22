using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Models.AiAssistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for AiAssistantService — testing heuristic mode only (no real AI calls).
/// All tests use a placeholder API key so the service falls back to heuristic logic.
/// </summary>
public class AiAssistantServiceTests : IDisposable
{
    private readonly AiAssistantService _service;

    public AiAssistantServiceTests()
    {
        var chatClientMock = new Mock<IChatClient>();
        var loggerMock = new Mock<ILogger<AiAssistantService>>();
        var options = Options.Create(new AiOptions
        {
            Anthropic = new AnthropicOptions { ApiKey = "sk-placeholder" },
            DefaultModel = "test-model"
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiSettings:RateLimitPerHour"] = "100"
            })
            .Build();

        _service = new AiAssistantService(chatClientMock.Object, options, loggerMock.Object, configuration);

        // Clear rate limits between test runs
        AiAssistantService.ResetRateLimits();
    }

    public void Dispose()
    {
        AiAssistantService.ResetRateLimits();
    }

    #region Math Generation — Heuristic Patterns

    [Fact]
    public async Task GenerateMath_FractionDescription_GeneratesLatex()
    {
        var result = await _service.GenerateMathAsync("fraction of a over b", null);

        result.Latex.Should().Contain("\\frac");
        result.Latex.Should().Contain("a");
        result.Latex.Should().Contain("b");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task GenerateMath_DefiniteIntegral_GeneratesLatex()
    {
        var result = await _service.GenerateMathAsync("integral of f(x) from 0 to 1", null);

        result.Latex.Should().Contain("\\int");
        result.Latex.Should().Contain("0");
        result.Latex.Should().Contain("1");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task GenerateMath_IndefiniteIntegral_GeneratesLatex()
    {
        var result = await _service.GenerateMathAsync("integral of x", null);

        result.Latex.Should().Contain("\\int");
        result.Latex.Should().Contain("x");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task GenerateMath_Summation_GeneratesLatex()
    {
        var result = await _service.GenerateMathAsync("sum of x_i from i=1 to n", null);

        result.Latex.Should().Contain("\\sum");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task GenerateMath_SquareRoot_GeneratesLatex()
    {
        var result = await _service.GenerateMathAsync("square root of x", null);

        result.Latex.Should().Contain("\\sqrt");
        result.Latex.Should().Contain("x");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task GenerateMath_Matrix_GeneratesLatex()
    {
        var result = await _service.GenerateMathAsync("matrix 2 by 3", null);

        result.Latex.Should().Contain("\\begin{pmatrix}");
        result.Latex.Should().Contain("\\end{pmatrix}");
        result.Latex.Should().Contain("&"); // Column separator
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task GenerateMath_Limit_GeneratesLatex()
    {
        var result = await _service.GenerateMathAsync("limit of f(x) as x approaches 0", null);

        result.Latex.Should().Contain("\\lim");
        result.Latex.Should().Contain("\\to");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task GenerateMath_PartialDerivative_GeneratesLatex()
    {
        var result = await _service.GenerateMathAsync("partial derivative of f with respect to x", null);

        result.Latex.Should().Contain("\\partial");
        result.Latex.Should().Contain("f");
        result.Latex.Should().Contain("x");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task GenerateMath_GreekLetter_GeneratesLatex()
    {
        var result = await _service.GenerateMathAsync("alpha", null);

        result.Latex.Should().Contain("\\alpha");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task GenerateMath_UnknownDescription_ReturnsAsIs()
    {
        var result = await _service.GenerateMathAsync("something completely unknown", null);

        result.Latex.Should().Be("something completely unknown");
        result.Confidence.Should().BeLessThan(0.5);
    }

    #endregion

    #region Math Fix — Heuristic Patterns

    [Fact]
    public async Task FixMath_MissingClosingBrace_FixesBraces()
    {
        var result = await _service.FixMathAsync(@"\frac{a}{b", null);

        result.FixedLatex.Should().Contain("}");
        var openCount = result.FixedLatex.Count(c => c == '{');
        var closeCount = result.FixedLatex.Count(c => c == '}');
        openCount.Should().Be(closeCount);
        result.Changes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FixMath_FracWithoutBraces_AddsBraces()
    {
        var result = await _service.FixMathAsync(@"\fracab", null);

        result.FixedLatex.Should().Contain(@"\frac{a}{b}");
        result.Changes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FixMath_SqrtWithoutBraces_AddsBraces()
    {
        var result = await _service.FixMathAsync(@"\sqrtx", null);

        result.FixedLatex.Should().Contain(@"\sqrt{x}");
        result.Changes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FixMath_DoubleBackslash_FixesCommand()
    {
        var result = await _service.FixMathAsync(@"\\frac{a}{b}", null);

        result.FixedLatex.Should().StartWith(@"\frac");
        result.FixedLatex.Should().NotStartWith(@"\\frac");
        result.Changes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FixMath_UnbalancedDollarSign_FixesDollar()
    {
        var result = await _service.FixMathAsync(@"$x^2", null);

        var dollarCount = result.FixedLatex.Count(c => c == '$');
        (dollarCount % 2).Should().Be(0);
        result.Changes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FixMath_WellFormedExpression_NoChanges()
    {
        var result = await _service.FixMathAsync(@"\frac{a}{b} = c", null);

        result.FixedLatex.Should().Be(@"\frac{a}{b} = c");
        result.Changes.Should().BeEmpty();
        result.Confidence.Should().Be(0.9);
    }

    [Fact]
    public async Task FixMath_IntegralTypo_FixesCommand()
    {
        var result = await _service.FixMathAsync(@"\integral_0^1 f(x) dx", null);

        result.FixedLatex.Should().Contain(@"\int");
        result.FixedLatex.Should().NotContain(@"\integral");
        result.Changes.Should().NotBeEmpty();
    }

    #endregion

    #region Writing Improvement — Heuristic Patterns

    [Fact]
    public async Task ImproveWriting_LowercaseStart_CapitalizesFirst()
    {
        var result = await _service.ImproveWritingAsync("the experiment was successful.", "improve", null);

        result.ImprovedText.Should().StartWith("T");
        result.Changes.Should().Contain(c => c.Contains("Capitalized"));
        result.Action.Should().Be("improve");
    }

    [Fact]
    public async Task ImproveWriting_MultipleSpaces_Collapses()
    {
        var result = await _service.ImproveWritingAsync("Text  with   extra  spaces.", "improve", null);

        result.ImprovedText.Should().NotContain("  ");
        result.Changes.Should().Contain(c => c.Contains("spaces"));
    }

    [Fact]
    public async Task ImproveWriting_Formalize_AddsPeriod()
    {
        var result = await _service.ImproveWritingAsync("The results are significant", "formalize", null);

        result.ImprovedText.Should().EndWith(".");
        result.Action.Should().Be("formalize");
    }

    [Fact]
    public async Task ImproveWriting_Shorten_RemovesFillers()
    {
        var result = await _service.ImproveWritingAsync(
            "It is important to note that the results are significant.", "shorten", null);

        result.ImprovedText.Should().NotContainEquivalentOf("it is important to note that");
        result.Changes.Should().Contain(c => c.Contains("filler"));
    }

    [Fact]
    public async Task ImproveWriting_InvalidAction_DefaultsToImprove()
    {
        var result = await _service.ImproveWritingAsync("some text", "invalid_action", null);

        result.Action.Should().Be("improve");
    }

    [Fact]
    public async Task ImproveWriting_Expand_ReturnsCleanedUp()
    {
        var result = await _service.ImproveWritingAsync("Short text.", "expand", null);

        result.Action.Should().Be("expand");
        result.Changes.Should().NotBeEmpty();
    }

    #endregion

    #region Block Classification — Heuristic Patterns

    [Theory]
    [InlineData(@"\frac{a}{b} = c")]
    [InlineData(@"\int_0^1 f(x) dx")]
    [InlineData(@"\sum_{i=1}^{n} x_i")]
    [InlineData(@"$$ E = mc^2 $$")]
    [InlineData(@"\begin{equation} x = y \end{equation}")]
    public async Task ClassifyBlock_EquationContent_SuggestsEquation(string content)
    {
        var result = await _service.ClassifyBlockAsync(content);

        result.SuggestedType.Should().Be("equation");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Theory]
    [InlineData("function hello() { return 1; }")]
    [InlineData("def main():\n    print('hello')")]
    [InlineData("class MyService { }")]
    [InlineData("import numpy as np")]
    [InlineData("public static void Main(string[] args)")]
    public async Task ClassifyBlock_CodeContent_SuggestsCode(string content)
    {
        var result = await _service.ClassifyBlockAsync(content);

        result.SuggestedType.Should().Be("code");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Theory]
    [InlineData("INTRODUCTION")]
    [InlineData("1. Introduction")]
    [InlineData("# Overview")]
    public async Task ClassifyBlock_HeadingContent_SuggestsHeading(string content)
    {
        var result = await _service.ClassifyBlockAsync(content);

        result.SuggestedType.Should().Be("heading");
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task ClassifyBlock_ListContent_SuggestsList()
    {
        var result = await _service.ClassifyBlockAsync("- First item\n- Second item\n- Third item");

        result.SuggestedType.Should().Be("list");
    }

    [Fact]
    public async Task ClassifyBlock_TableContent_SuggestsTable()
    {
        var result = await _service.ClassifyBlockAsync("Name | Age\nAlice | 30\nBob | 25");

        result.SuggestedType.Should().Be("table");
    }

    [Fact]
    public async Task ClassifyBlock_PlainText_SuggestsParagraph()
    {
        var result = await _service.ClassifyBlockAsync(
            "This is a normal paragraph of text about the results of our experiment.");

        result.SuggestedType.Should().Be("paragraph");
        result.Confidence.Should().Be(0.9);
    }

    [Fact]
    public async Task ClassifyBlock_EmptyContent_DefaultsToParagraph()
    {
        var result = await _service.ClassifyBlockAsync("");

        result.SuggestedType.Should().Be("paragraph");
        result.Confidence.Should().Be(0.5);
    }

    #endregion

    #region Rate Limiting

    [Fact]
    public void CheckRateLimit_UnderLimit_AllowsRequests()
    {
        for (var i = 0; i < 10; i++)
        {
            _service.CheckRateLimit("test-user-1").Should().BeTrue();
        }
    }

    [Fact]
    public void CheckRateLimit_AtLimit_DeniesRequests()
    {
        // Create a service with very low rate limit
        var chatClientMock = new Mock<IChatClient>();
        var loggerMock = new Mock<ILogger<AiAssistantService>>();
        var options = Options.Create(new AiOptions
        {
            Anthropic = new AnthropicOptions { ApiKey = "sk-placeholder" },
            DefaultModel = "test-model"
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiSettings:RateLimitPerHour"] = "5"
            })
            .Build();

        var service = new AiAssistantService(chatClientMock.Object, options, loggerMock.Object, configuration);

        // Use a unique user ID for this test
        var userId = $"rate-limit-test-{Guid.NewGuid()}";

        // Fill up the limit
        for (var i = 0; i < 5; i++)
        {
            service.CheckRateLimit(userId).Should().BeTrue();
        }

        // Next request should be denied
        service.CheckRateLimit(userId).Should().BeFalse();
    }

    [Fact]
    public void CheckRateLimit_DifferentUsers_IndependentLimits()
    {
        var chatClientMock = new Mock<IChatClient>();
        var loggerMock = new Mock<ILogger<AiAssistantService>>();
        var options = Options.Create(new AiOptions
        {
            Anthropic = new AnthropicOptions { ApiKey = "sk-placeholder" },
            DefaultModel = "test-model"
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiSettings:RateLimitPerHour"] = "3"
            })
            .Build();

        var service = new AiAssistantService(chatClientMock.Object, options, loggerMock.Object, configuration);

        var userA = $"user-a-{Guid.NewGuid()}";
        var userB = $"user-b-{Guid.NewGuid()}";

        // Fill user A's limit
        for (var i = 0; i < 3; i++)
            service.CheckRateLimit(userA).Should().BeTrue();

        // User A is denied
        service.CheckRateLimit(userA).Should().BeFalse();

        // User B should still be allowed
        service.CheckRateLimit(userB).Should().BeTrue();
    }

    #endregion
}
