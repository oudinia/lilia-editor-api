namespace Lilia.Core.Capabilities;

/// <summary>
/// Something a document needs in order to render.
///
/// <para><b>Why this type exists.</b> Four hand-authored catalogues answer the
/// same question in four different shapes — <c>latex_tokens</c> (293 rows),
/// <c>latex_packages</c> (57), <c>latex_document_classes</c> (13) and
/// <c>latex_unicode_map</c>, each with its own table, its own admin report, and
/// three of them with their own <c>coverage_level</c> column. Font coverage
/// became the fifth, and not even in the same database — it lives on a separate
/// Neon project reached through <c>ConnectionStrings:LatexFacts</c>.</para>
///
/// <para>All of them answer: <i>given this requirement and this target, is it
/// satisfied, and what else would work?</i> A requirement is the subject half of
/// that question, stated once so the catalogues can become providers rather than
/// systems.</para>
///
/// <para><b>Deliberately not an entity.</b> Requirements are derived from block
/// content on demand, never stored. Persisting them would create a sixth
/// catalogue to keep in step with the document, which is the problem rather than
/// the fix.</para>
/// </summary>
public abstract record Requirement
{
    /// <summary>
    /// Stable, human-readable identity — <c>command:\setmainfont</c>,
    /// <c>codepoint:U+4E2D</c>. Used for grouping, logging and de-duplication,
    /// so it must not change once a provider has reported against it.
    /// </summary>
    public abstract string Key { get; }

    /// <summary>How to name this in something a person reads.</summary>
    public abstract string Describe();
}

/// <summary>A LaTeX control sequence, e.g. <c>\setmainfont</c>.</summary>
public sealed record CommandRequirement(string Name) : Requirement
{
    public override string Key => $"command:{Normalised}";

    /// <summary>
    /// With exactly one leading backslash. Callers pull command names from
    /// several places — parsed source, catalogue rows, hand-written provider
    /// tables — and they do not agree on whether the backslash is included.
    /// Two spellings of the same command would otherwise resolve separately and
    /// one of them would find no provider at all.
    /// </summary>
    public string Normalised => "\\" + Name.TrimStart('\\');

    public override string Describe() => Normalised;
}

/// <summary>A LaTeX package, e.g. <c>fontspec</c>.</summary>
public sealed record PackageRequirement(string Name) : Requirement
{
    public override string Key => $"package:{Name}";
    public override string Describe() => $"package {Name}";
}

/// <summary>A document class, e.g. <c>beamer</c>.</summary>
public sealed record DocumentClassRequirement(string Name) : Requirement
{
    public override string Key => $"class:{Name}";
    public override string Describe() => $"document class {Name}";
}

/// <summary>
/// A single character that has to render, identified by code point.
/// </summary>
/// <remarks>
/// A code point rather than a <c>char</c>, so astral characters are one
/// requirement instead of two surrogate halves — the same distinction P2.5's
/// font coverage had to make. Asking whether a font covers half of a surrogate
/// pair is meaningless.
/// </remarks>
public sealed record CodepointRequirement(int Codepoint) : Requirement
{
    public override string Key => $"codepoint:U+{Codepoint:X4}";
    public override string Describe() => $"U+{Codepoint:X4} ({char.ConvertFromUtf32(Codepoint)})";
}

/// <summary>
/// A whole writing system — <c>Han</c>, <c>Arabic</c>, <c>Devanagari</c>.
/// </summary>
/// <remarks>
/// <para>Distinct from <see cref="CodepointRequirement"/> on purpose, and the
/// plan is explicit about why: <b>do not extend the unicode map to scripts</b>.
/// A macro per code point works for a few hundred symbols and cannot work for
/// 20,000 CJK code points or for Arabic contextual shaping, where the glyph
/// depends on neighbours. Those need a <i>font</i>, not a replacement.</para>
///
/// <para>Emitting the script as its own requirement is what lets a provider
/// answer "this needs a CJK font" once, instead of answering 20,000 times and
/// still being wrong about shaping.</para>
/// </remarks>
public sealed record ScriptRequirement(string Script) : Requirement
{
    public override string Key => $"script:{Script}";
    public override string Describe() => $"{Script} script";
}

/// <summary>A named font family the document asks for.</summary>
public sealed record FontRequirement(string Family) : Requirement
{
    public override string Key => $"font:{Family}";
    public override string Describe() => $"font {Family}";
}
