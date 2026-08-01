namespace Lilia.Core.Capabilities;

/// <summary>
/// One provider's answer about one requirement against one target.
/// </summary>
/// <param name="Requirement">What was asked about.</param>
/// <param name="Support">How well the target satisfies it.</param>
/// <param name="Source">
/// Which provider said so. Present because verdicts are merged from several
/// providers and a surprising answer is untraceable without it — the catalogues
/// are hand-authored, so "which table claims this" is the first question anyone
/// asks.
/// </param>
/// <param name="Detail">
/// Something a person can act on: what to install, which engine would work,
/// what the substitution actually does.
/// </param>
/// <param name="Alternatives">
/// Other things that would satisfy the requirement — the "and what else would
/// work?" half of the question. A font that covers the character, an engine
/// that supports the command. Empty when the provider has nothing to offer,
/// which is not the same as there being nothing.
/// </param>
public sealed record CapabilityVerdict(
    Requirement Requirement,
    Support Support,
    string Source,
    string? Detail = null,
    IReadOnlyList<string>? Alternatives = null)
{
    public IReadOnlyList<string> Alternatives { get; init; } = Alternatives ?? [];

    /// <summary>
    /// The verdict to use when a provider cannot answer — as opposed to
    /// answering "no".
    /// </summary>
    public static CapabilityVerdict Unknown(Requirement requirement, string source, string? detail = null) =>
        new(requirement, Support.Unknown, source, detail);
}

/// <summary>
/// Resolves requirements against a target. One per catalogue.
///
/// <para>Each existing catalogue becomes an implementation <b>unchanged</b> —
/// the tables, their rows and their admin reports stay exactly as they are.
/// What changes is that they stop being five systems that each answer a
/// differently-shaped question, and become five answers to the same one.</para>
/// </summary>
public interface ICapabilityProvider
{
    /// <summary>Name reported in <see cref="CapabilityVerdict.Source"/>.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this provider can currently reach its data.
    /// </summary>
    /// <remarks>
    /// <para><b>The rule this interface exists to enforce.</b> A provider that
    /// cannot reach its catalogue must report <c>false</c> here and return
    /// <see cref="Support.Unknown"/> — never an empty result, which a caller
    /// reads as "nothing is wrong".</para>
    ///
    /// <para>P2.5 met this exactly: a font catalogue that could not be reached
    /// would have returned an empty list of uncovered characters, and the
    /// caller would have understood "this font is fine" — a confident wrong
    /// answer about the one thing the catalogue exists to prevent. The font
    /// catalogue lives on a <i>different database server</i> from the rest, so
    /// this is a normal operating condition, not a hypothetical.</para>
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// Which requirements this provider has any opinion about, so the resolver
    /// need not ask a package catalogue about code points.
    /// </summary>
    bool Handles(Requirement requirement);

    /// <summary>
    /// Answer for the requirements this provider handles.
    /// </summary>
    /// <remarks>
    /// Takes the whole batch rather than one requirement at a time: every
    /// provider is backed by a database, and a verdict per round trip would
    /// make a document-sized question expensive enough that callers would start
    /// avoiding it.
    /// </remarks>
    Task<IReadOnlyList<CapabilityVerdict>> ResolveAsync(
        IReadOnlyList<Requirement> requirements,
        RenderTarget target,
        CancellationToken ct = default);
}
