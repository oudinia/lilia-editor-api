using Lilia.Core.Capabilities;

namespace Lilia.Api.Services.Capabilities;

/// <summary>The resolved answer for one requirement against one target.</summary>
/// <param name="Requirement">What was asked about.</param>
/// <param name="Support">The merged verdict — the most pessimistic real answer.</param>
/// <param name="Verdicts">Every provider's answer, kept so a surprising result can be traced.</param>
public sealed record ResolvedRequirement(
    Requirement Requirement,
    Support Support,
    IReadOnlyList<CapabilityVerdict> Verdicts)
{
    /// <summary>Alternatives offered by any provider, de-duplicated.</summary>
    public IReadOnlyList<string> Alternatives =>
        [.. Verdicts.SelectMany(v => v.Alternatives).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The first detail worth showing — the one from the deciding verdict.</summary>
    public string? Detail =>
        Verdicts.Where(v => v.Support == Support).Select(v => v.Detail).FirstOrDefault(d => d is { Length: > 0 })
        ?? Verdicts.Select(v => v.Detail).FirstOrDefault(d => d is { Length: > 0 });
}

/// <param name="Target">What this was resolved against.</param>
/// <param name="Requirements">Every requirement asked about, satisfied or not.</param>
/// <param name="UnavailableProviders">
/// Providers that could not reach their data. Surfaced rather than logged: a
/// clean report from a resolver with a silent provider is not the same as a
/// clean report, and the caller is the only one who can tell the difference.
/// </param>
public sealed record CapabilityReport(
    RenderTarget Target,
    IReadOnlyList<ResolvedRequirement> Requirements,
    IReadOnlyList<string> UnavailableProviders)
{
    /// <summary>Requirements that will not render cleanly, worst first.</summary>
    public IReadOnlyList<ResolvedRequirement> Problems =>
        [.. Requirements.Where(r => r.Support.NeedsReporting()).OrderBy(r => r.Support)];

    /// <summary>
    /// Whether the target satisfies everything asked of it.
    /// </summary>
    /// <remarks>
    /// False when anything is Unknown, and false when a provider was
    /// unavailable. "Nothing known to be wrong" is not "nothing wrong", and
    /// conflating them here would undo the whole point of the model at the last
    /// step, in the one property callers are most likely to branch on.
    /// </remarks>
    public bool IsFullySatisfied =>
        UnavailableProviders.Count == 0 && Requirements.All(r => r.Support.IsSatisfied());
}

/// <summary>
/// Fans a document's requirements out across every provider and merges the
/// answers.
/// </summary>
public sealed class CapabilityResolver(IEnumerable<ICapabilityProvider> providers, ILogger<CapabilityResolver> logger)
{
    private readonly IReadOnlyList<ICapabilityProvider> _providers = [.. providers];

    public async Task<CapabilityReport> ResolveAsync(
        IReadOnlyList<Requirement> requirements, RenderTarget target, CancellationToken ct = default)
    {
        // Duplicates collapse first. A document repeats the same character
        // thousands of times, and resolving each occurrence would make a
        // document-sized question expensive enough that callers stop asking.
        var distinct = requirements.Distinct().ToList();

        var unavailable = _providers.Where(p => !p.IsAvailable).Select(p => p.Name).ToList();
        foreach (var name in unavailable)
        {
            logger.LogWarning(
                "[Capabilities] Provider {Provider} is unavailable; its requirements will report Unknown " +
                "rather than being silently omitted.", name);
        }

        var byRequirement = distinct.ToDictionary(r => r, _ => new List<CapabilityVerdict>());

        foreach (var provider in _providers)
        {
            var handled = distinct.Where(provider.Handles).ToList();
            if (handled.Count == 0) continue;

            IReadOnlyList<CapabilityVerdict> verdicts;
            try
            {
                verdicts = await provider.ResolveAsync(handled, target, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A provider that throws must not take the report down, and
                // must not vanish from it either. Its requirements stay in the
                // result carrying Unknown, which reads as "could not check"
                // rather than "fine".
                logger.LogError(ex, "[Capabilities] Provider {Provider} failed; reporting Unknown for its requirements",
                    provider.Name);

                verdicts = [.. handled.Select(r => CapabilityVerdict.Unknown(
                    r, provider.Name, $"provider failed: {ex.Message}"))];
                unavailable.Add(provider.Name);
            }

            foreach (var verdict in verdicts)
            {
                if (byRequirement.TryGetValue(verdict.Requirement, out var list)) list.Add(verdict);
            }
        }

        var resolved = distinct.Select(requirement =>
        {
            var verdicts = byRequirement[requirement];

            // Aggregate over the whole set rather than seeding with the first
            // verdict, so a requirement nobody answered lands on Unknown
            // instead of dropping out of the report entirely. Every requirement
            // asked about comes back, always.
            var support = verdicts.Aggregate(Support.Unknown, (acc, v) => acc.WorseOf(v.Support));

            return new ResolvedRequirement(requirement, support, verdicts);
        }).ToList();

        return new CapabilityReport(target, resolved, [.. unavailable.Distinct()]);
    }
}
