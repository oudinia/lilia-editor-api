using Microsoft.Extensions.AI;

namespace Lilia.Api.Services;

/// <summary>Reads what a model call used out of the chat client's usage (3 Nov 2026, prompt caching).</summary>
public static class AiUsage
{
    /// <summary>
    /// The Anthropic adapter counts cache reads and writes inside <see cref="UsageDetails.InputTokenCount"/>, the
    /// reads again in <see cref="UsageDetails.CachedInputTokenCount"/> and the writes under
    /// <c>CacheCreationInputTokens</c> in the additional counts.
    /// </summary>
    public static AiTokenUse From(UsageDetails? usage)
    {
        if (usage is null) return AiTokenUse.None;
        var writes = usage.AdditionalCounts is { } extra && extra.TryGetValue("CacheCreationInputTokens", out var w) ? w : 0;
        return new AiTokenUse(
            Input: (int)(usage.InputTokenCount ?? 0),
            Output: (int)(usage.OutputTokenCount ?? 0),
            CacheWrite: (int)writes,
            CacheRead: (int)(usage.CachedInputTokenCount ?? 0));
    }
}
