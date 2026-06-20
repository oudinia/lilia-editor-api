namespace Lilia.Api.Services;

/// <summary>
/// Per-token cost estimation for architect calls. Rates are in USD per
/// 1,000,000 tokens and are deliberately simple, conservative defaults (the
/// Anthropic Sonnet list price as of mid-2026). They are used only to report
/// an estimated <c>costUsd</c> + remaining budget to the client; the
/// authoritative spend record is the integer credit ledger.
/// </summary>
public static class AiArchitectPricing
{
    // USD per 1M tokens.
    private const decimal SonnetInputPerMillion = 3.00m;
    private const decimal SonnetOutputPerMillion = 15.00m;
    private const decimal OpusInputPerMillion = 15.00m;
    private const decimal OpusOutputPerMillion = 75.00m;
    private const decimal HaikuInputPerMillion = 0.80m;
    private const decimal HaikuOutputPerMillion = 4.00m;

    // 1 credit ≈ 1000 tokens (EntitlementService convention). At Sonnet's
    // blended rate this is a small fraction of a cent; we surface a USD figure
    // so the editor can show a budget bar. Treat a credit as ~$0.006.
    private const decimal UsdPerCredit = 0.006m;

    public static decimal ComputeCostUsd(string model, int inputTokens, int outputTokens)
    {
        var (inRate, outRate) = RatesFor(model);
        var cost = (inputTokens / 1_000_000m * inRate) + (outputTokens / 1_000_000m * outRate);
        return decimal.Round(cost, 6);
    }

    public static decimal CreditsToUsd(int credits)
        => decimal.Round(Math.Max(0, credits) * UsdPerCredit, 4);

    private static (decimal Input, decimal Output) RatesFor(string model)
    {
        var m = model?.ToLowerInvariant() ?? string.Empty;
        if (m.Contains("opus")) return (OpusInputPerMillion, OpusOutputPerMillion);
        if (m.Contains("haiku")) return (HaikuInputPerMillion, HaikuOutputPerMillion);
        // Default to Sonnet rates (the architect's default model).
        return (SonnetInputPerMillion, SonnetOutputPerMillion);
    }
}
