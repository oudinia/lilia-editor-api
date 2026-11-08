namespace Lilia.Api.Services;

/// <summary>
/// The one Anthropic client (9 Nov 2026). The SDK's own timeout governs: the HttpClient it made by default kept
/// .NET's 100 s, so a long answer (a 4,000-word chapter takes 105 to 165 s) failed with "Ask Lilia failed to
/// respond" and the work was lost. <c>AI:Anthropic:TimeoutSeconds</c>, default 300.
/// </summary>
public static class AnthropicClientFactory
{
    public const int DefaultTimeoutSeconds = 300;

    public static Anthropic.AnthropicClient Create(string apiKey, int timeoutSeconds = DefaultTimeoutSeconds) => new()
    {
        ApiKey = apiKey,
        HttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
        Timeout = TimeSpan.FromSeconds(timeoutSeconds),
    };
}
