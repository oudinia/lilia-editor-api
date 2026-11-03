namespace Lilia.Api.Services;

public class AiOptions
{
    public AnthropicOptions Anthropic { get; set; } = new();
    public string DefaultModel { get; set; } = "claude-sonnet-5";
    public Dictionary<string, string> Models { get; set; } = new();
    /// <summary>
    /// Ask Lilia's model and effort per skill tier (3 Nov 2026): "fast" skills (polish, coverage, tutor) on a
    /// cheaper model at low effort. A tier with no model uses the catalog's default; no effort, the model's own.
    /// </summary>
    public Dictionary<string, AiTierOptions> Tiers { get; set; } = new();
}

public class AiTierOptions
{
    public string? Model { get; set; }
    /// <summary>low, medium, high, xhigh or max (Anthropic's output_config.effort).</summary>
    public string? Effort { get; set; }
}

public class AnthropicOptions
{
    public string ApiKey { get; set; } = string.Empty;
}
