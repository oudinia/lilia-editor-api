using System.Text.Json;

namespace Lilia.Core.Blocks;

/// <summary>
/// Reads an equation block's content, whichever shape it is stored in.
///
/// <para><b>The shape being moved to.</b></para>
/// <code>
/// { notation: "latex", source: "\\mathcal{L} = …", ast: &lt;tree | null&gt; }
/// </code>
///
/// <para><b>The shape most rows are still in.</b> <c>{ latex: "…" }</c>.</para>
///
/// <para>Every reader goes through here so the two can coexist: `source` wins,
/// `latex` is the fallback, and nothing has to know which era a row came from.
/// That is the whole point of landing this before the write side — readers stop
/// caring about the shape first, so changing what gets written is then a change
/// nobody downstream can notice.</para>
///
/// <para><b>Why rename at all — the argument is not tidiness.</b> A string can
/// only be printed; a tree can be used. Validating without compiling,
/// accessible math (PDF/UA needs structure — a rasterised equation has none),
/// semantic search, and symbol lookup (24% of all math questions in the corpus,
/// stable over 14 years) are each blocked by math being an opaque string. The
/// reserved <c>ast</c> field is where that stops being true.</para>
///
/// <para><b>What was NOT worth doing.</b> An earlier plan called for writing
/// <c>ast: null</c> into every equation block now, to "reserve" the field before
/// beta made the migration expensive. Block content is a schemaless
/// <c>JsonDocument</c>: absent and null are indistinguishable to every reader,
/// so that migration does not exist and pre-writing nulls buys nothing. The part
/// that genuinely gets more expensive with every authored document is the
/// <c>latex</c> → <c>source</c> rename, because afterwards you must either
/// migrate rows or support both shapes forever. Hence this file.</para>
/// </summary>
public static class EquationContent
{
    /// <summary>The only notation currently written, and the assumed default.</summary>
    public const string LatexNotation = "latex";

    private const string SourceProperty = "source";
    private const string LegacySourceProperty = "latex";
    private const string NotationProperty = "notation";
    private const string AstProperty = "ast";

    /// <summary>
    /// The equation's source text, verbatim. Prefers <c>source</c>, falls back
    /// to the legacy <c>latex</c>, and returns "" when neither is present —
    /// callers already treat an empty equation as an empty equation rather than
    /// an error, so this keeps that behaviour exactly.
    /// </summary>
    public static string ReadSource(JsonElement content)
    {
        if (TryReadString(content, SourceProperty, out var source)) return source;
        if (TryReadString(content, LegacySourceProperty, out var legacy)) return legacy;
        return "";
    }

    /// <summary>
    /// The notation the source is written in. Defaults to <c>latex</c>: every
    /// row predating this field is LaTeX, and so is everything the editor
    /// writes today. Callers should not branch on it yet — it exists so that
    /// the day a second notation appears, the rows already say which is which.
    /// </summary>
    public static string ReadNotation(JsonElement content) =>
        TryReadString(content, NotationProperty, out var notation) ? notation : LatexNotation;

    /// <summary>
    /// The parsed tree, when one has been stored. Always false today — no
    /// parser writes it yet (that is P3.4). Present so that populating it later
    /// is purely additive, and so callers can be written against it now.
    /// </summary>
    public static bool TryReadAst(JsonElement content, out JsonElement ast)
    {
        ast = default;
        if (content.ValueKind != JsonValueKind.Object) return false;
        if (!content.TryGetProperty(AstProperty, out var value)) return false;
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return false;
        ast = value;
        return true;
    }

    /// <summary>
    /// True when this content still carries only the legacy <c>latex</c>
    /// property. Used by the write path to decide whether it has anything to
    /// normalise, and by tests to prove the fallback is actually exercised.
    /// </summary>
    public static bool IsLegacyShape(JsonElement content) =>
        content.ValueKind == JsonValueKind.Object
        && !HasNonEmpty(content, SourceProperty)
        && HasNonEmpty(content, LegacySourceProperty);

    private static bool TryReadString(JsonElement content, string property, out string value)
    {
        value = "";
        if (content.ValueKind != JsonValueKind.Object) return false;
        if (!content.TryGetProperty(property, out var element)) return false;
        if (element.ValueKind != JsonValueKind.String) return false;

        var text = element.GetString();
        // An explicit empty string is not a usable source: falling through to
        // the other property is what lets a half-migrated row still render.
        if (string.IsNullOrEmpty(text)) return false;

        value = text;
        return true;
    }

    private static bool HasNonEmpty(JsonElement content, string property) =>
        TryReadString(content, property, out _);
}
