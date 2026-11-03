namespace Lilia.Api.Services;

/// <summary>
/// What a model call used (3 Nov 2026, prompt caching). <paramref name="Input"/> is every input token, cached or
/// not; <paramref name="CacheWrite"/> and <paramref name="CacheRead"/> are the parts of it written to and read
/// from the prompt cache, which Anthropic bills at 1.25× and 0.1× the input price.
/// </summary>
public sealed record AiTokenUse(int Input, int Output, int CacheWrite = 0, int CacheRead = 0)
{
    /// <summary>The input billed at the full price.</summary>
    public int Uncached => Math.Max(0, Input - CacheWrite - CacheRead);

    public static AiTokenUse operator +(AiTokenUse a, AiTokenUse b) =>
        new(a.Input + b.Input, a.Output + b.Output, a.CacheWrite + b.CacheWrite, a.CacheRead + b.CacheRead);

    public static readonly AiTokenUse None = new(0, 0);
}
