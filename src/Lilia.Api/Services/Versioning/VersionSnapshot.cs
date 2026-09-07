using System.Text.Json;
using Lilia.Core.Entities;

namespace Lilia.Api.Services.Versioning;

/// <summary>
/// What a document version actually contains, and how to read one back.
///
/// <para>This exists as pure functions over JSON for two reasons. It was written
/// three times — once in <c>CreateVersionAsync</c>, once in
/// <c>CreateAutoVersionAsync</c>, and a third, different shape was read back in
/// <c>RestoreVersionAsync</c> — and the three had already drifted apart, which is
/// how restore came to silently discard things the snapshot had faithfully
/// stored. One definition means writing and reading cannot disagree.</para>
///
/// <para>And it makes the behaviour testable. Blocks and snapshots are mapped as
/// <c>JsonDocument</c>, which the in-memory EF provider throws on, so anything
/// that needs a DbContext cannot be unit tested here at all. Keeping the rules in
/// pure functions puts the part that was wrong under test without a database.</para>
/// </summary>
public static class VersionSnapshot
{
    /// <summary>
    /// Bumped when the shape changes. Snapshots written before versioning was
    /// fixed have no marker; <see cref="ReadBlocks"/> and
    /// <see cref="ApplySettings"/> stay lenient so they still restore as well as
    /// they ever did rather than throwing on a document someone saved in July.
    /// </summary>
    public const int CurrentSchema = 2;

    /// <summary>A block as stored in a snapshot, with the identity intact.</summary>
    /// <param name="Id">
    /// Preserved deliberately. Restore used to mint a fresh Guid for every block,
    /// which broke anything pointing at one: the LaTeX emitter writes
    /// <c>\label{blk-&lt;id&gt;}</c>, so cross-references died; comments anchored
    /// to a block were orphaned; and the block_validations cache, keyed on
    /// block_id, was invalidated wholesale. It also meant restoring the same
    /// version twice produced two different documents.
    /// </param>
    /// <param name="ParentId">
    /// Also preserved. It was written into the snapshot and then ignored on the
    /// way back, so every nested block — list items, theorem bodies — was
    /// restored as a top-level sibling and the structure flattened.
    /// </param>
    public sealed record SnapshotBlock(
        Guid Id,
        string Type,
        JsonElement Content,
        int SortOrder,
        Guid? ParentId,
        int Depth,
        string? Path,
        string Status);

    public sealed record SnapshotBibEntry(
        Guid Id,
        string CiteKey,
        string EntryType,
        JsonElement Data,
        string? FormattedText);

    /// <summary>
    /// Settings that change what the document compiles to.
    ///
    /// <para>The old snapshot carried five of these. Everything else — the
    /// document class, the column layout, margins, the preamble, the engine —
    /// was outside the snapshot, so restoring an older version left today's
    /// layout on yesterday's content. A version that does not describe the
    /// document's appearance is not a version of the document.</para>
    ///
    /// <para>Excluded on purpose: sharing (<c>IsPublic</c>, <c>ShareSlug</c>,
    /// <c>LinkExpiresAt</c>) and lifecycle (<c>Status</c>, <c>LastOpenedAt</c>).
    /// Rolling content back should not silently republish a document or revoke a
    /// link someone is using.</para>
    /// </summary>
    public static readonly string[] SettingKeys =
    [
        "title", "language", "paperSize", "orientation", "fontFamily", "fontSize",
        "columns", "columnSeparator", "balancedColumns",
        "marginTop", "marginBottom", "marginLeft", "marginRight",
        "headerText", "footerText", "pageNumbering",
        "documentCategory", "latexDocumentClass", "latexDocumentClassOptions",
        "customPreamble", "latexEngine",
    ];

    public static object Build(
        Document document,
        IEnumerable<Block> blocks,
        IEnumerable<BibliographyEntry> bibliography) => new
        {
            schema = CurrentSchema,
            title = document.Title,
            language = document.Language,
            paperSize = document.PaperSize,
            orientation = document.Orientation,
            fontFamily = document.FontFamily,
            fontSize = document.FontSize,
            columns = document.Columns,
            columnSeparator = document.ColumnSeparator,
            balancedColumns = document.BalancedColumns,
            marginTop = document.MarginTop,
            marginBottom = document.MarginBottom,
            marginLeft = document.MarginLeft,
            marginRight = document.MarginRight,
            headerText = document.HeaderText,
            footerText = document.FooterText,
            pageNumbering = document.PageNumbering,
            documentCategory = document.DocumentCategory,
            latexDocumentClass = document.LatexDocumentClass,
            latexDocumentClassOptions = document.LatexDocumentClassOptions,
            customPreamble = document.CustomPreamble,
            latexEngine = document.LatexEngine,
            blocks = blocks.OrderBy(b => b.SortOrder).Select(b => new
            {
                id = b.Id,
                type = b.Type,
                content = b.Content.RootElement,
                sortOrder = b.SortOrder,
                parentId = b.ParentId,
                depth = b.Depth,
                path = b.Path,
                status = b.Status,
            }),
            bibliography = bibliography.Select(e => new
            {
                id = e.Id,
                citeKey = e.CiteKey,
                entryType = e.EntryType,
                data = e.Data.RootElement,
                formattedText = e.FormattedText,
            }),
        };

    public static JsonDocument Serialise(
        Document document,
        IEnumerable<Block> blocks,
        IEnumerable<BibliographyEntry> bibliography) =>
        JsonDocument.Parse(JsonSerializer.Serialize(Build(document, blocks, bibliography)));

    public static int SchemaOf(JsonElement snapshot) =>
        snapshot.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.Number
            ? s.GetInt32()
            : 1;

    /// <summary>
    /// Blocks in a snapshot, in sort order, with identity and nesting intact.
    /// Missing fields fall back rather than throw, so a schema-1 snapshot still
    /// restores what it does carry.
    /// </summary>
    public static List<SnapshotBlock> ReadBlocks(JsonElement snapshot)
    {
        var result = new List<SnapshotBlock>();
        if (!snapshot.TryGetProperty("blocks", out var blocks) ||
            blocks.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var b in blocks.EnumerateArray())
        {
            result.Add(new SnapshotBlock(
                Id: ReadGuid(b, "id") ?? Guid.NewGuid(),
                Type: ReadString(b, "type") ?? "paragraph",
                Content: b.TryGetProperty("content", out var c)
                    ? c.Clone()
                    : JsonDocument.Parse("{}").RootElement.Clone(),
                SortOrder: ReadInt(b, "sortOrder") ?? 0,
                ParentId: ReadGuid(b, "parentId"),
                Depth: ReadInt(b, "depth") ?? 0,
                Path: ReadString(b, "path"),
                Status: ReadString(b, "status") ?? "draft"));
        }

        return result.OrderBy(b => b.SortOrder).ToList();
    }

    public static List<SnapshotBibEntry> ReadBibliography(JsonElement snapshot)
    {
        var result = new List<SnapshotBibEntry>();
        if (!snapshot.TryGetProperty("bibliography", out var bib) ||
            bib.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var e in bib.EnumerateArray())
        {
            result.Add(new SnapshotBibEntry(
                Id: ReadGuid(e, "id") ?? Guid.NewGuid(),
                CiteKey: ReadString(e, "citeKey") ?? string.Empty,
                EntryType: ReadString(e, "entryType") ?? "misc",
                Data: e.TryGetProperty("data", out var d)
                    ? d.Clone()
                    : JsonDocument.Parse("{}").RootElement.Clone(),
                FormattedText: ReadString(e, "formattedText")));
        }

        return result;
    }

    /// <summary>
    /// Puts the snapshot's settings back on the document. A key the snapshot does
    /// not carry leaves the document's current value alone — an older snapshot
    /// should not blank a setting that did not exist when it was taken.
    /// </summary>
    public static void ApplySettings(JsonElement snapshot, Document document)
    {
        document.Title = ReadString(snapshot, "title") ?? document.Title;
        document.Language = ReadString(snapshot, "language") ?? document.Language;
        document.PaperSize = ReadString(snapshot, "paperSize") ?? document.PaperSize;
        document.Orientation = ReadString(snapshot, "orientation") ?? document.Orientation;
        document.FontFamily = ReadString(snapshot, "fontFamily") ?? document.FontFamily;
        document.FontSize = ReadInt(snapshot, "fontSize") ?? document.FontSize;
        document.Columns = ReadInt(snapshot, "columns") ?? document.Columns;
        document.ColumnSeparator = ReadString(snapshot, "columnSeparator") ?? document.ColumnSeparator;
        document.BalancedColumns = ReadBool(snapshot, "balancedColumns") ?? document.BalancedColumns;

        // Nullable strings: present-but-null in the snapshot is a real value —
        // the author cleared the margin — so only a missing key falls back.
        if (Has(snapshot, "marginTop")) document.MarginTop = ReadString(snapshot, "marginTop");
        if (Has(snapshot, "marginBottom")) document.MarginBottom = ReadString(snapshot, "marginBottom");
        if (Has(snapshot, "marginLeft")) document.MarginLeft = ReadString(snapshot, "marginLeft");
        if (Has(snapshot, "marginRight")) document.MarginRight = ReadString(snapshot, "marginRight");
        if (Has(snapshot, "headerText")) document.HeaderText = ReadString(snapshot, "headerText");
        if (Has(snapshot, "footerText")) document.FooterText = ReadString(snapshot, "footerText");
        if (Has(snapshot, "pageNumbering")) document.PageNumbering = ReadString(snapshot, "pageNumbering");
        if (Has(snapshot, "documentCategory")) document.DocumentCategory = ReadString(snapshot, "documentCategory");
        if (Has(snapshot, "latexDocumentClass")) document.LatexDocumentClass = ReadString(snapshot, "latexDocumentClass");
        if (Has(snapshot, "latexDocumentClassOptions")) document.LatexDocumentClassOptions = ReadString(snapshot, "latexDocumentClassOptions");
        if (Has(snapshot, "customPreamble")) document.CustomPreamble = ReadString(snapshot, "customPreamble");

        document.LatexEngine = ReadString(snapshot, "latexEngine") ?? document.LatexEngine;
    }

    /// <summary>
    /// A stable identity for a snapshot's meaning, ignoring how it was written.
    ///
    /// Comparing raw JSON would report a difference whenever the serialiser
    /// emitted keys in a different order or spaced them differently, which would
    /// make every restore add a redundant "Before restore" version. This
    /// compares the settings and the blocks that actually define the document.
    /// </summary>
    public static string Fingerprint(JsonElement snapshot)
    {
        var parts = new List<string>();
        foreach (var key in SettingKeys)
        {
            parts.Add(snapshot.TryGetProperty(key, out var v)
                ? $"{key}={v.ValueKind}:{v.ToString()}"
                : $"{key}=~");
        }

        foreach (var b in ReadBlocks(snapshot))
        {
            parts.Add($"b:{b.Id}:{b.Type}:{b.SortOrder}:{b.ParentId}:{b.Depth}:{Canonical(b.Content)}");
        }

        foreach (var e in ReadBibliography(snapshot).OrderBy(x => x.CiteKey, StringComparer.Ordinal))
        {
            parts.Add($"c:{e.CiteKey}:{e.EntryType}:{Canonical(e.Data)}");
        }

        return string.Join("\u0001", parts);
    }

    /// <summary>
    /// JSON with object keys in a fixed order, so two encodings of the same
    /// content compare equal. Block content is authored JSON and its key order
    /// is not meaningful; without this, re-serialising a document would read as
    /// an edit and restore would preserve a "Before restore" version that is
    /// identical to the one before it.
    /// </summary>
    internal static string Canonical(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var props = e.EnumerateObject()
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => $"{JsonSerializer.Serialize(p.Name)}:{Canonical(p.Value)}");
                return "{" + string.Join(",", props) + "}";
            case JsonValueKind.Array:
                return "[" + string.Join(",", e.EnumerateArray().Select(Canonical)) + "]";
            case JsonValueKind.String:
                return JsonSerializer.Serialize(e.GetString());
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return "null";
            default:
                return e.GetRawText();
        }
    }

    private static bool Has(JsonElement e, string name) => e.TryGetProperty(name, out _);

    private static string? ReadString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? ReadInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

    private static bool? ReadBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) &&
        v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    private static Guid? ReadGuid(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.String &&
        Guid.TryParse(v.GetString(), out var g)
            ? g
            : null;
}
