using Lilia.Core.Capabilities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services.Capabilities;

/// <summary>
/// Wraps P2.5's font coverage — the catalogue that lives on a different
/// database server.
/// </summary>
/// <remarks>
/// <para><b>This is why <see cref="ICapabilityProvider.IsAvailable"/> is on the
/// interface.</b> Every other provider reads the application database, so if
/// the API is answering at all, its catalogue is reachable. The font facts are
/// on a separate Neon project behind <c>ConnectionStrings:LatexFacts</c>, which
/// can be unconfigured in development and unreachable in production while
/// everything else works perfectly.</para>
///
/// <para>P2.5 already established the rule this depends on: with no catalogue,
/// report unavailable rather than returning an empty list of uncovered
/// characters. An empty list reads as "this font is fine" — a confident wrong
/// answer about the exact failure the catalogue exists to prevent.</para>
/// </remarks>
public sealed class FontCoverageProvider(IFontCoverageService fonts) : ICapabilityProvider
{
    public string Name => "fonts";

    public bool IsAvailable => fonts.IsAvailable;

    public bool Handles(Requirement requirement) =>
        requirement is CodepointRequirement or ScriptRequirement;

    public async Task<IReadOnlyList<CapabilityVerdict>> ResolveAsync(
        IReadOnlyList<Requirement> requirements, RenderTarget target, CancellationToken ct = default)
    {
        var handled = requirements.Where(Handles).ToList();
        if (handled.Count == 0) return [];

        if (!IsAvailable)
        {
            // Say so per requirement, so the gap is visible next to the thing
            // it concerns rather than as one global caveat nobody connects to
            // the character that will go missing.
            return [.. handled.Select(r => CapabilityVerdict.Unknown(
                r, Name, "the font catalogue is not configured, so coverage could not be checked"))];
        }

        // pdflatex has no font layer to consult: it cannot load system fonts at
        // all, so which fonts exist says nothing about what it can render. The
        // unicode-map provider is what answers there.
        if (target is RenderTarget.Pdflatex)
        {
            return [.. handled.Select(r => CapabilityVerdict.Unknown(
                r, Name, "pdflatex cannot load fonts by family; coverage is decided by the replacement map"))];
        }

        var verdicts = new List<CapabilityVerdict>(handled.Count);

        foreach (var requirement in handled)
        {
            switch (requirement)
            {
                case CodepointRequirement cp:
                {
                    var covering = await fonts.FontsCoveringAsync([cp.Codepoint], ct);
                    if (covering.Count == 0)
                    {
                        verdicts.Add(new CapabilityVerdict(
                            cp, Support.None, Name, "no font in the catalogue covers this character"));
                        break;
                    }

                    // Portability, from P2.5: a system font renders here and
                    // breaks the moment the .tex reaches a collaborator or
                    // Overleaf. Measured 4,269 tex-tree against 931 system, so
                    // roughly one font in six is that trap. Offering portable
                    // fonts first is the difference between an alternative that
                    // travels and one that only works on this machine.
                    var portable = covering.Where(f => f.IsPortable).Select(f => f.Family).ToList();
                    var alternatives = (portable.Count > 0 ? portable : covering.Select(f => f.Family).ToList())
                        .Take(5).ToList();

                    verdicts.Add(new CapabilityVerdict(
                        cp, Support.Full, Name,
                        portable.Count > 0
                            ? "covered by a font that travels with the document"
                            : "covered, but only by fonts installed on this machine",
                        alternatives));
                    break;
                }

                case ScriptRequirement script:
                {
                    // A script is not a code point, and the catalogue is
                    // indexed by code point. Answering it properly means
                    // sampling representative characters — deliberately not
                    // guessed here. Reporting Unknown keeps the gap visible
                    // instead of inventing a verdict for a whole writing
                    // system.
                    verdicts.Add(CapabilityVerdict.Unknown(
                        script, Name,
                        "script-level coverage is not yet derived from the font catalogue"));
                    break;
                }
            }
        }

        return verdicts;
    }
}

/// <summary>
/// Wraps <c>typst_translation_gaps</c> — the only catalogue that is about
/// Typst, and the only one whose rows already carry a target.
/// </summary>
public sealed class TypstGapProvider(LiliaDbContext context) : ICapabilityProvider
{
    public string Name => "typst_translation_gaps";
    public bool IsAvailable => true;

    public bool Handles(Requirement requirement) =>
        requirement is CommandRequirement or PackageRequirement;

    public async Task<IReadOnlyList<CapabilityVerdict>> ResolveAsync(
        IReadOnlyList<Requirement> requirements, RenderTarget target, CancellationToken ct = default)
    {
        // Gaps describe what LaTeX cannot become in Typst. Against a LaTeX
        // target they are simply not the question.
        if (target is not RenderTarget.Typst) return [];

        var handled = requirements.Where(Handles).ToList();
        if (handled.Count == 0) return [];

        var gaps = await context.TypstTranslationGaps
            .Select(g => new { g.GapKey, g.Category, g.BlockingSeverity, g.MitigationStatus, g.Notes })
            .ToListAsync(ct);

        var verdicts = new List<CapabilityVerdict>();

        foreach (var requirement in handled)
        {
            // Gap keys are dotted paths — "package.tikz", "math.two-letter-
            // identifier" — so a package requirement matches on its tail.
            var name = requirement switch
            {
                PackageRequirement p => p.Name,
                CommandRequirement c => c.Normalised.TrimStart('\\'),
                _ => null,
            };
            if (name is null) continue;

            var gap = gaps.FirstOrDefault(g =>
                g.GapKey.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
            if (gap is null) continue;

            // A shipped mitigation means the gap was closed — api#93 closed
            // math.two-letter-identifier this way. Reporting a closed gap as a
            // problem would be worse than saying nothing.
            var support = gap.MitigationStatus?.ToLowerInvariant() switch
            {
                "shipped" => Support.Full,
                "workaround" => Support.Shimmed,
                _ => gap.BlockingSeverity?.ToLowerInvariant() switch
                {
                    "error" => Support.Impossible,
                    "warn" => Support.Partial,
                    _ => Support.Partial,
                },
            };

            verdicts.Add(new CapabilityVerdict(requirement, support, Name, gap.Notes, ["pdflatex", "lualatex"]));
        }

        return verdicts;
    }
}
