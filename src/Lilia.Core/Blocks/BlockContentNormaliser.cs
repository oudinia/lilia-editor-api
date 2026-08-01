using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lilia.Core.Blocks;

/// <summary>
/// The single place block content is shaped before it is stored.
///
/// <para>There was no such place. <c>BlockService</c> wrote
/// <c>JsonDocument.Parse(dto.Content…)</c> straight through at four separate
/// sites, and nothing else in the codebase normalised content at all — so
/// "write the new shape" had no seam to hook and every writer would have had to
/// remember independently. This is that seam.</para>
///
/// <para><b>Deliberately conservative.</b> Content is schemaless by design and
/// most block types are free-form, so this only touches what it understands and
/// copies everything else through untouched. An unknown type, a non-object, or
/// malformed JSON is returned exactly as it arrived: normalisation is not a
/// validation layer, and refusing to store something because this file did not
/// recognise it would be a much worse failure than storing it unchanged.</para>
/// </summary>
public static class BlockContentNormaliser
{
    /// <summary>
    /// Shape content for storage. Returns a NEW <see cref="JsonDocument"/> the
    /// caller owns; the input is never mutated.
    /// </summary>
    /// <param name="macros">
    /// Macros the document defines for itself, from
    /// <see cref="Capabilities.PreambleMacroCollector"/>. Supplied because
    /// 26.2% of real documents containing maths define one, and an equation
    /// using <c>\R</c> cannot be understood without knowing what this document
    /// means by it. Null is fine — the AST is then built from the source as
    /// written, which is correct for the documents that define nothing.
    /// </param>
    public static JsonDocument Normalise(
        string? blockType,
        JsonElement content,
        IReadOnlyDictionary<string, Capabilities.MacroDefinition>? macros = null)
    {
        if (!IsEquation(blockType) || content.ValueKind != JsonValueKind.Object)
            return Clone(content);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(content.GetRawText());
        }
        catch (JsonException)
        {
            return Clone(content);
        }

        if (node is not JsonObject obj) return Clone(content);

        var source = EquationContent.ReadSource(content);

        // Nothing to anchor to — an equation block with no source at all is
        // legitimate (a freshly inserted, still-empty one), and inventing
        // properties for it would just add noise to every new block.
        if (string.IsNullOrEmpty(source)) return Clone(content);

        obj["source"] = source;

        // `latex` is kept in step with `source` rather than dropped. Readers
        // outside this codebase — an export already on disk, a client built
        // against the old shape — still expect it, and the cost of keeping it
        // is one duplicated string per equation. Removing it is a later step,
        // once nothing reads it and existing rows have been backfilled.
        obj["latex"] = source;

        obj["notation"] ??= EquationContent.LatexNotation;

        // The equation as structure rather than text.
        //
        // `source` stays verbatim and authoritative — LaTeX export emits
        // exactly what the author typed, and nothing here changes that. The
        // tree is for the questions a string cannot answer: whether a fraction
        // has an empty denominator without compiling to find out, structure for
        // accessible PDF, search by meaning rather than by characters.
        //
        // Written on save rather than derived on read because the document's
        // macros are known here and are not known later — an equation reaching
        // a reader has no preamble attached to it.
        //
        // An earlier note here said `ast` was deliberately not written. That was
        // about writing `null` to reserve the field, which bought nothing in
        // schemaless JSON. Writing a real tree is a different proposition.
        if (TryBuildAst(source, macros, out var ast))
        {
            obj["ast"] = ast;
        }
        else
        {
            // Absent, never null or partial. A reader must be able to tell
            // "nobody could parse this" from "this parsed to nothing", and the
            // only honest signal for the first is that the key is not there.
            obj.Remove("ast");
        }

        return JsonDocument.Parse(obj.ToJsonString());
    }

    /// <summary>Convenience for callers holding raw text rather than an element.</summary>
    public static JsonDocument NormaliseRaw(string? blockType, string rawJson)
    {
        try
        {
            using var parsed = JsonDocument.Parse(rawJson);
            return Normalise(blockType, parsed.RootElement);
        }
        catch (JsonException)
        {
            // Malformed JSON is the caller's problem to report, not ours to
            // swallow — parse again outside the try so it throws as it always did.
            return JsonDocument.Parse(rawJson);
        }
    }

    private static bool IsEquation(string? blockType) =>
        string.Equals(blockType, "equation", StringComparison.OrdinalIgnoreCase);

    private static readonly Services.MathParser.LaTeXMathParser Parser = new();

    private static readonly JsonSerializerOptions AstOptions = new()
    {
        // Stored, not displayed. Indenting would multiply the size of every
        // equation block for nobody's benefit.
        WriteIndented = false,
    };

    /// <summary>
    /// Build the tree, or report that it could not be built.
    /// </summary>
    /// <remarks>
    /// <para><b>Never throws, and never fails a save.</b> This runs on every
    /// equation write, including half-typed ones — <c>\frac{</c> is a normal
    /// intermediate state, not an error to reject. The parser was measured
    /// against all 180 equations in the corpus and against the awkward inputs a
    /// person produces mid-edit, but a save must not depend on that holding for
    /// input nobody has seen yet.</para>
    ///
    /// <para>Macros are expanded first because the parser cannot know that
    /// <c>\R</c> is <c>\mathbb{R}</c> in this document. Expansion that hits its
    /// depth limit still yields usable maths, so the tree is built from what
    /// came back rather than abandoned.</para>
    /// </remarks>
    private static bool TryBuildAst(
        string source,
        IReadOnlyDictionary<string, Capabilities.MacroDefinition>? macros,
        out JsonNode? ast)
    {
        ast = null;

        try
        {
            var expanded = macros is { Count: > 0 }
                ? Capabilities.MacroExpander.Expand(source, macros).Source
                : source;

            if (string.IsNullOrWhiteSpace(expanded)) return false;

            var node = Parser.Parse(expanded);
            if (node is null) return false;

            ast = JsonSerializer.SerializeToNode(node, AstOptions);
            return ast is not null;
        }
        catch
        {
            // Whatever went wrong, the author's equation is still saved with its
            // source intact. Losing the tree costs a feature; losing the save
            // costs their work.
            return false;
        }
    }

    private static JsonDocument Clone(JsonElement content) =>
        JsonDocument.Parse(content.GetRawText());
}
