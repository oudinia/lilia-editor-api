using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;

namespace Lilia.Engines;

/// <summary>What a label points at. Derived from the block, never stored.</summary>
public enum ReferenceKind
{
    Unknown,
    Section,
    Figure,
    Table,
    Equation,
    Theorem,
    Algorithm,
    Listing,
}

/// <summary>A label defined somewhere in the document.</summary>
/// <param name="Key">As the author wrote it: <c>tab:results</c>.</param>
/// <param name="Number">What <c>\ref</c> prints, from the last compile's .aux.
/// Null when the document has not been compiled — a number nobody measured is
/// not a number we will invent.</param>
public sealed record ReferenceTarget(
    string Key,
    ReferenceKind Kind,
    Guid BlockId,
    string? Caption,
    string? Number = null,
    int? Page = null);

/// <summary>A place the document points at a label.</summary>
/// <param name="Form">ref | eqref | cref | Cref | autoref | at — the last being
/// Lilia's own <c>@ref{}</c> shorthand.</param>
public sealed record ReferenceUse(string Key, Guid BlockId, string Form);

/// <param name="Kind">dangling | duplicate | unused</param>
public sealed record ReferenceProblem(string Kind, string Key, IReadOnlyList<Guid> BlockIds);

public sealed record ReferenceReport(
    IReadOnlyList<ReferenceTarget> Targets,
    IReadOnlyList<ReferenceUse> Uses,
    IReadOnlyList<ReferenceProblem> Problems);

/// <summary>
/// Every label a document defines, every place it points at one, and where those
/// two disagree.
///
/// <para><b>Derived, never stored.</b> An index maintained on save would be
/// faster to query and would eventually disagree with the document. That exact
/// shape has cost this project three times — the coverage catalog drifting from
/// the parser, the editor's labels panel bound to a payload it does not match,
/// and a token count quoted from a doc long after reality moved. The blocks are
/// already loaded to render the document, so walking them is a pass over data we
/// have in hand and it cannot be stale.</para>
///
/// <para><b>Numbers come from LaTeX, not from here.</b> Nothing in this file
/// counts floats. Numbering is <c>\numberwithin</c>, chapter resets,
/// <c>\appendix</c>, starred variants and float order — each one a way to be
/// confidently wrong. The compiler already writes the answer into the .aux and
/// <see cref="AuxPageMap.ReadAll"/> reads it; this class only joins it on.</para>
/// </summary>
public static class ReferenceIndex
{
    /// <summary>
    /// The reference commands, and Lilia's own <c>@ref{}</c>. Deliberately not
    /// matching <c>\cite</c>: a citation points outside the document, is served
    /// by the bibliography, and folding the two together here would report every
    /// citation as a dangling reference.
    /// </summary>
    private static readonly Regex UsePattern = new(
        @"[\\@](?<form>eqref|autoref|cref|Cref|ref)\s*\{(?<key>[^{}]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static ReferenceKind KindOf(string blockType) => blockType switch
    {
        BlockTypes.Table => ReferenceKind.Table,
        BlockTypes.Figure => ReferenceKind.Figure,
        BlockTypes.Equation => ReferenceKind.Equation,
        BlockTypes.Heading => ReferenceKind.Section,
        BlockTypes.Theorem => ReferenceKind.Theorem,
        BlockTypes.Algorithm => ReferenceKind.Algorithm,
        BlockTypes.Code => ReferenceKind.Listing,
        _ => ReferenceKind.Unknown,
    };

    /// <summary>
    /// Build the report. <paramref name="auxContent"/> is the .aux of the last
    /// successful compile, when there has been one; without it every number is
    /// null and the caller says so rather than guessing.
    /// </summary>
    public static ReferenceReport Build(IEnumerable<Block> blocks, string? auxContent = null)
    {
        var numbers = AuxPageMap.ReadAll(auxContent)
            .GroupBy(l => l.Key, StringComparer.Ordinal)
            // Last write wins, as in the page map: a label redefined within one
            // run leaves the later entry as the one the PDF reflects.
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        return Build(blocks, numbers);
    }

    /// <summary>
    /// Build the report with numbers already read — the ones the last PDF
    /// compile stored (<see cref="LabelNumbers"/>), so the index costs no
    /// compile. The .aux overload is a projection over this, so the two cannot
    /// disagree about how a number is joined on.
    /// </summary>
    public static ReferenceReport Build(
        IEnumerable<Block> blocks, IReadOnlyDictionary<string, AuxLabel> numbers)
    {

        var targets = new List<ReferenceTarget>();
        var uses = new List<ReferenceUse>();

        foreach (var block in blocks.OrderBy(b => b.SortOrder))
        {
            var root = block.Content?.RootElement ?? default;
            if (root.ValueKind != JsonValueKind.Object) continue;

            foreach (var (label, caption) in LabelsIn(root))
            {
                // The key the compiled document defines, by the same rule the
                // exporter writes it with — so a bare "main" on an equation is
                // eq:main here too, and the panel agrees with the PDF. Sub-figure
                // labels live on figure blocks, so the block's type is theirs.
                var key = LabelKey.Effective(block.Type, label);
                numbers.TryGetValue(key, out var aux);
                targets.Add(new ReferenceTarget(
                    key, KindOf(block.Type), block.Id, caption, aux.Number, aux.Page));
            }

            // Uses are found by scanning the block's raw JSON rather than by
            // knowing where every block type keeps its prose. A \ref can sit in
            // a paragraph, a caption, a table cell or a theorem body, and a
            // scanner that only knew about paragraphs would miss most of them.
            foreach (Match m in UsePattern.Matches(root.GetRawText()))
            {
                var form = m.Groups["form"].Value;
                uses.Add(new ReferenceUse(
                    m.Groups["key"].Value.Trim(),
                    block.Id,
                    m.Value[0] == '@' ? "at" : form));
            }
        }

        return new ReferenceReport(targets, uses, Problems(targets, uses));
    }

    /// <summary>
    /// The labels a block defines: its own, plus any its sub-figures carry.
    /// Every block type keeps its label in the same place — <c>content.label</c>
    /// — which is why this needs no per-type knowledge.
    /// </summary>
    private static IEnumerable<(string Key, string? Caption)> LabelsIn(JsonElement content)
    {
        var caption = content.TryGetProperty("caption", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;

        if (content.TryGetProperty("label", out var l)
            && l.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(l.GetString()))
        {
            yield return (l.GetString()!.Trim(), caption);
        }

        if (!content.TryGetProperty("subfigures", out var subs) || subs.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var sub in subs.EnumerateArray())
        {
            if (sub.ValueKind != JsonValueKind.Object) continue;
            if (!sub.TryGetProperty("label", out var sl)
                || sl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(sl.GetString()))
            {
                continue;
            }

            yield return (
                sl.GetString()!.Trim(),
                sub.TryGetProperty("caption", out var sc) && sc.ValueKind == JsonValueKind.String
                    ? sc.GetString()
                    : null);
        }
    }

    private static List<ReferenceProblem> Problems(
        List<ReferenceTarget> targets, List<ReferenceUse> uses)
    {
        var problems = new List<ReferenceProblem>();
        var defined = targets.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);

        // Dangling: the one that actually costs the author. LaTeX sets "??" in
        // the PDF and compiles successfully, so nothing else reports it.
        foreach (var group in uses.Where(u => !defined.Contains(u.Key))
                     .GroupBy(u => u.Key, StringComparer.Ordinal))
        {
            problems.Add(new ReferenceProblem(
                "dangling", group.Key, group.Select(u => u.BlockId).Distinct().ToList()));
        }

        // Duplicate: LaTeX takes the last silently, so half the references point
        // somewhere the author did not mean.
        foreach (var group in targets.GroupBy(t => t.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problems.Add(new ReferenceProblem(
                "duplicate", group.Key, group.Select(t => t.BlockId).ToList()));
        }

        // Unused: information, not an error. A label with no reference yet is a
        // draft in progress far more often than it is a mistake, and reporting
        // it as a fault would train authors to ignore the list.
        var used = uses.Select(u => u.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var target in targets.Where(t => !used.Contains(t.Key)))
        {
            problems.Add(new ReferenceProblem("unused", target.Key, [target.BlockId]));
        }

        return problems;
    }
}
