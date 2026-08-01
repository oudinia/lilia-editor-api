namespace Lilia.Core.Capabilities;

/// <summary>
/// How well a target satisfies a requirement.
///
/// <para>The five named levels map the values already in
/// <c>latex_tokens.coverage_level</c> — <c>full</c>, <c>shimmed</c>,
/// <c>partial</c>, <c>none</c>, <c>unsupported</c> — so wrapping the existing
/// catalogues loses nothing.</para>
///
/// <para><b><see cref="Unknown"/> is the one that is not in the database, and
/// it is the reason this is an enum rather than a bool.</b> Every failure this
/// plan exists to fix has the same shape: the system produced plausible output,
/// knew something had gone wrong, and told nobody. A missing answer that reads
/// as "fine" is that failure in miniature. P2.5 hit it directly — a font
/// catalogue that could not be reached would have returned an empty list of
/// uncovered characters, which the caller reads as "this font is fine": a
/// confident wrong answer about the exact thing the catalogue exists to
/// prevent.</para>
/// </summary>
public enum Support
{
    /// <summary>
    /// Nobody could answer. Not a verdict — the absence of one.
    /// </summary>
    /// <remarks>
    /// Deliberately first so that <c>default(Support)</c> is Unknown. A struct
    /// that was never populated then reports ignorance rather than claiming
    /// full support, which is the direction an accident should fail in.
    /// </remarks>
    Unknown = 0,

    /// <summary>Cannot work on this target and no amount of setup changes that.</summary>
    /// <remarks>
    /// Distinct from <see cref="None"/> because the remedies differ. CJK under
    /// pdflatex is Impossible — the engine has no path to it. A missing package
    /// is None: install it and the answer changes.
    /// </remarks>
    Impossible = 1,

    /// <summary>Not available as things stand, but obtainable.</summary>
    None = 2,

    /// <summary>Works incompletely — some of it will be wrong or missing.</summary>
    Partial = 3,

    /// <summary>Works through a substitution rather than natively.</summary>
    Shimmed = 4,

    /// <summary>Natively supported.</summary>
    Full = 5,
}

public static class SupportExtensions
{
    /// <summary>
    /// Whether this verdict lets rendering proceed without a caveat.
    /// </summary>
    /// <remarks>
    /// <see cref="Support.Unknown"/> is <b>not</b> satisfactory. Treating "I
    /// could not tell" as "yes" is precisely the silent-success failure the
    /// enum exists to prevent, and it is the reading a caller falls into by
    /// accident unless the type refuses it.
    /// </remarks>
    public static bool IsSatisfied(this Support support) =>
        support is Support.Full or Support.Shimmed;

    /// <summary>
    /// Whether this verdict is worth telling somebody about.
    /// </summary>
    public static bool NeedsReporting(this Support support) =>
        support is not (Support.Full or Support.Shimmed);

    /// <summary>
    /// The more pessimistic of two verdicts, used when providers disagree.
    /// </summary>
    /// <remarks>
    /// <para>Pessimism wins on purpose. Overstating support produces a document
    /// that fails at compile time or, worse, renders with characters silently
    /// dropped; understating it produces a warning about something that turns
    /// out fine. Only one of those is recoverable by the person reading it.</para>
    ///
    /// <para><see cref="Support.Unknown"/> does not compete: a real answer from
    /// any provider beats no answer, and Unknown survives only when nothing
    /// answered at all. Otherwise a single silent provider would drag every
    /// requirement down to Unknown and the report would be useless.</para>
    /// </remarks>
    public static Support WorseOf(this Support a, Support b)
    {
        if (a == Support.Unknown) return b;
        if (b == Support.Unknown) return a;
        return a < b ? a : b;
    }
}
