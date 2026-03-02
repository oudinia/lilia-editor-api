namespace Lilia.Api.Services;

public class AiOptions
{
    public AnthropicOptions Anthropic { get; set; } = new();
    public string DefaultModel { get; set; } = "claude-sonnet-4-5-20250929";
    public Dictionary<string, string> Models { get; set; } = new();
}

public class AnthropicOptions
{
    public string ApiKey { get; set; } = string.Empty;
}
