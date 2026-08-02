using System.Text.RegularExpressions;

namespace Lilia.Engines;

/// <summary>
/// Where a package's engine requirement comes from. Implemented by whichever host
/// owns the catalog — the interface lives here so <see cref="EngineResolver"/> can
/// use it without Lilia.Engines depending on a host.
/// </summary>
public interface IEngineRequirementSource
{
    /// <summary>
    /// The engine <paramref name="packageSlug"/> requires, or null if it runs
    /// anywhere or the package is unknown. Must be synchronous and cheap: this is
    /// on the path of every compile, so implementations serve from memory.
    /// </summary>
    LatexEngine? RequiredEngine(string packageSlug);
}

/// <summary>
/// Which engine a document needs.
///
/// <para>Detection used to be <see cref="EngineDetector"/> alone: a regex holding
/// eleven hard-coded command names. That answers a lexical question adequately,
/// but it cannot know about a package nobody thought to add to the list, and
/// getting it wrong in that direction is expensive — the document is handed to an
/// engine that cannot read it, and the author is told their content is broken.</para>
///
/// <para>This layers the measured catalog on top: every <c>\usepackage</c> in the
/// source is resolved against recorded requirements first, and the regex remains
/// as the floor for commands that imply an engine without naming a package
/// (<c>\setmainfont</c>, <c>\directlua</c>). Adding a package becomes a row, not a
/// deploy.</para>
///
/// <para>It stays a <em>fast path</em>, not an authority. Being wrong here costs a
/// compile that the retry in the tools runner then corrects; the value of getting
/// it right is not correctness but not spending the compile at all.</para>
/// </summary>
public interface IEngineResolver
{
    LatexEngine Resolve(string? latex);
}

public sealed class EngineResolver : IEngineResolver
{
    // \usepackage[opts]{a,b,c} — captures the brace contents so each slug can be
    // resolved separately. Deliberately permissive about whitespace and options,
    // matching the fix to EngineDetector's own package pattern.
    private static readonly Regex UsePackage = new(
        @"\\usepackage\s*(?:\[[^\]]*\])?\s*\{([^}]*)\}",
        RegexOptions.Compiled);

    private readonly IEngineRequirementSource _requirements;

    public EngineResolver(IEngineRequirementSource requirements) => _requirements = requirements;

    public LatexEngine Resolve(string? latex)
    {
        if (string.IsNullOrEmpty(latex)) return LatexEngine.Pdflatex;

        // The regex floor first: it is the cheaper check and covers the commands
        // that imply an engine without loading a package.
        var engine = EngineDetector.Detect(latex);
        if (engine == LatexEngine.Lualatex) return engine;

        foreach (Match m in UsePackage.Matches(latex))
        {
            foreach (var raw in m.Groups[1].Value.Split(','))
            {
                var slug = raw.Trim();
                if (slug.Length == 0) continue;

                var required = _requirements.RequiredEngine(slug);
                // Escalate only — a package that runs on pdflatex must never pull
                // a document that needs lualatex back down.
                if (required is { } r && r > engine) engine = r;
            }
        }

        return engine;
    }
}

/// <summary>
/// The resolver with no catalog behind it: the regex floor and nothing more.
/// Lets a host wire engine resolution before it has anywhere to read
/// requirements from, without pretending it has richer knowledge than it does.
/// </summary>
public sealed class RegexOnlyEngineRequirements : IEngineRequirementSource
{
    public LatexEngine? RequiredEngine(string packageSlug) => null;
}
