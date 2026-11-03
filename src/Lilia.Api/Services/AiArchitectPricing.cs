namespace Lilia.Api.Services;

/// <summary>
/// What a model call costs, in dollars and in credits. Prices are Anthropic's list prices in USD per
/// 1,000,000 tokens (3 Nov 2026; they were Sonnet 4 and Opus 4 prices before, 1.5x to 3x today's). The
/// catalog's credit rates (<c>ai_models</c>) are these prices at <see cref="UsdPerCredit"/>; a test checks
/// the two agree. The authoritative spend record is the integer credit ledger.
/// </summary>
public static class AiArchitectPricing
{
    // USD per 1M tokens (input, output), by model id.
    private static readonly Dictionary<string, (decimal Input, decimal Output)> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-sonnet-5"] = (2.00m, 10.00m),
        ["claude-sonnet-5-5"] = (2.00m, 10.00m),
        ["claude-opus-5"] = (5.00m, 25.00m),
        ["claude-opus-5-5"] = (4.00m, 20.00m),
        ["claude-opus-4-8"] = (5.00m, 25.00m),
        ["claude-sonnet-4-6"] = (3.00m, 15.00m),
        ["claude-haiku-4-5"] = (1.00m, 5.00m),
        // Haiku 5.5 is $0.10 / $0.50 up to 100,000 prompt tokens and five times that beyond. Ask Lilia often
        // sends more, so it is priced at the higher rate: never undercharged (3 Nov 2026, the fast skills).
        ["claude-haiku-5-5"] = (0.50m, 2.50m),
        ["claude-fable-5-1"] = (10.00m, 50.00m),
    };

    /// <summary>What a credit is worth: the peg the plans were set with (100 a month ≈ $0.60).</summary>
    public const decimal UsdPerCredit = 0.006m;

    public static decimal ComputeCostUsd(string model, int inputTokens, int outputTokens) =>
        ComputeCostUsd(model, new AiTokenUse(inputTokens, outputTokens));

    /// <summary>With the prompt cache priced in (3 Nov 2026): writes at 1.25× the input rate, reads at 0.1×.</summary>
    public static decimal ComputeCostUsd(string model, AiTokenUse use)
    {
        var (inRate, outRate) = RatesFor(model);
        var input = use.Uncached + use.CacheWrite * AiCatalogService.CacheWriteRate + use.CacheRead * AiCatalogService.CacheReadRate;
        var cost = (input / 1_000_000m * inRate) + (use.Output / 1_000_000m * outRate);
        return decimal.Round(cost, 6);
    }

    public static decimal CreditsToUsd(int credits)
        => decimal.Round(Math.Max(0, credits) * UsdPerCredit, 4);

    /// <summary>
    /// Convert an estimated USD surcharge (e.g. server-side web-search fees that
    /// bill outside the per-token model) into integer credits using the same peg
    /// the budget bar uses. Ceils so any non-zero surcharge debits at least 1.
    /// </summary>
    public static int UsdToCredits(decimal usd)
        => usd <= 0m ? 0 : (int)Math.Ceiling(usd / UsdPerCredit);

    private static (decimal Input, decimal Output) RatesFor(string model) => PricePerMillion(model);

    /// <summary>A model's price in USD per million tokens; an unknown id is priced by its family.</summary>
    public static (decimal Input, decimal Output) PricePerMillion(string? model)
    {
        if (model is not null && Prices.TryGetValue(model, out var price)) return price;
        var m = model?.ToLowerInvariant() ?? string.Empty;
        if (m.Contains("opus")) return Prices["claude-opus-5"];
        if (m.Contains("haiku")) return Prices["claude-haiku-4-5"];
        return Prices["claude-sonnet-5"];
    }
}
