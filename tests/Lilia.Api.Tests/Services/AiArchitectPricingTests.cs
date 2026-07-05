using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for the credit/USD peg in <see cref="AiArchitectPricing"/> — in
/// particular <c>UsdToCredits</c>, which converts a server-side tool surcharge
/// (e.g. web-search fees) into the integer credits actually debited.
/// </summary>
public class AiArchitectPricingTests
{
    [Theory]
    [InlineData(0.0, 0)]      // nothing spent → nothing debited
    [InlineData(-0.5, 0)]     // guard against negatives
    [InlineData(0.001, 1)]    // any non-zero surcharge debits at least 1 credit
    [InlineData(0.006, 1)]    // exactly one credit's worth
    [InlineData(0.01, 2)]     // one web search ($0.01 @ $0.006/credit) → ceil(1.67) = 2
    [InlineData(0.03, 5)]     // three searches ($0.03) → ceil(5.0) = 5
    public void UsdToCredits_CeilsToWholeCredits(decimal usd, int expected)
    {
        AiArchitectPricing.UsdToCredits(usd).Should().Be(expected);
    }

    [Fact]
    public void UsdToCredits_IsNeverNegative_AndRoundTripsThroughCreditsToUsd()
    {
        // A surcharge of N credits' worth of USD must debit at least N credits back.
        var usd = AiArchitectPricing.CreditsToUsd(4);          // 4 credits → USD
        AiArchitectPricing.UsdToCredits(usd).Should().BeGreaterThanOrEqualTo(4);
    }
}
