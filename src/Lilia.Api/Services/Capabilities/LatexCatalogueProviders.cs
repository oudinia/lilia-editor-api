using Lilia.Core.Capabilities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services.Capabilities;

/// <summary>
/// The three LaTeX catalogues, wrapped as providers.
///
/// <para><b>Nothing about the tables changes.</b> No migration, no new column,
/// no touched row, and the existing admin reports keep reading them exactly as
/// before. What changes is that three systems answering differently-shaped
/// questions become three answers to one question.</para>
///
/// <para>Each reports the stored <c>coverage_level</c> for every LaTeX target,
/// because that is all the schema knows — the level has no target attached. It
/// is honest for pdflatex/xelatex/lualatex today, wrong to claim for Typst, and
/// it is the seam where per-target data becomes worth collecting.</para>
/// </summary>
public sealed class LatexTokenProvider(LiliaDbContext context) : ICapabilityProvider
{
    public string Name => "latex_tokens";

    /// <summary>Same database as everything else, so if the app is up this is up.</summary>
    public bool IsAvailable => true;

    public bool Handles(Requirement requirement) => requirement is CommandRequirement;

    public async Task<IReadOnlyList<CapabilityVerdict>> ResolveAsync(
        IReadOnlyList<Requirement> requirements, RenderTarget target, CancellationToken ct = default)
    {
        var commands = requirements.OfType<CommandRequirement>().ToList();
        if (commands.Count == 0) return [];

        if (!CatalogueCoverage.AppliesTo(target))
            return [.. commands.Select(c => CapabilityVerdict.Unknown(
                c, Name, "the token catalogue describes the LaTeX toolchain and says nothing about Typst"))];

        // Rows store names without the leading backslash; requirements
        // normalise to include it. Match on the bare form.
        var bare = commands.Select(c => c.Normalised.TrimStart('\\')).Distinct().ToList();

        var rows = await context.LatexTokens
            .Where(t => bare.Contains(t.Name))
            .Select(t => new { t.Name, t.CoverageLevel, t.PackageSlug, t.Notes })
            .ToListAsync(ct);

        var byName = rows.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        return [.. commands.Select(command =>
        {
            var name = command.Normalised.TrimStart('\\');
            if (!byName.TryGetValue(name, out var row))
            {
                // Absent from a hand-authored catalogue of 293 commands means
                // nobody has classified it — not that it is unsupported. LaTeX
                // has thousands of commands; reporting None here would bury a
                // real problem under false alarms until the report is ignored.
                return CapabilityVerdict.Unknown(command, Name, "not in the token catalogue");
            }

            var detail = row.PackageSlug is { Length: > 0 }
                ? $"provided by package {row.PackageSlug}"
                : row.Notes;

            return new CapabilityVerdict(command, CatalogueCoverage.FromLevel(row.CoverageLevel), Name, detail);
        })];
    }
}

/// <summary>Wraps <c>latex_packages</c>.</summary>
public sealed class LatexPackageProvider(LiliaDbContext context) : ICapabilityProvider
{
    public string Name => "latex_packages";
    public bool IsAvailable => true;

    public bool Handles(Requirement requirement) => requirement is PackageRequirement;

    public async Task<IReadOnlyList<CapabilityVerdict>> ResolveAsync(
        IReadOnlyList<Requirement> requirements, RenderTarget target, CancellationToken ct = default)
    {
        var packages = requirements.OfType<PackageRequirement>().ToList();
        if (packages.Count == 0) return [];

        if (!CatalogueCoverage.AppliesTo(target))
            return [.. packages.Select(p => CapabilityVerdict.Unknown(
                p, Name, "Typst has no LaTeX packages; this catalogue does not describe it"))];

        var slugs = packages.Select(p => p.Name).Distinct().ToList();

        var rows = await context.LatexPackages
            .Where(p => slugs.Contains(p.Slug))
            .Select(p => new { p.Slug, p.CoverageLevel, p.CoverageNotes })
            .ToListAsync(ct);

        var bySlug = rows.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);

        return [.. packages.Select(package =>
            bySlug.TryGetValue(package.Name, out var row)
                ? new CapabilityVerdict(package, CatalogueCoverage.FromLevel(row.CoverageLevel), Name, row.CoverageNotes)
                : CapabilityVerdict.Unknown(package, Name, "not in the package catalogue"))];
    }
}

/// <summary>Wraps <c>latex_document_classes</c>.</summary>
public sealed class LatexDocumentClassProvider(LiliaDbContext context) : ICapabilityProvider
{
    public string Name => "latex_document_classes";
    public bool IsAvailable => true;

    public bool Handles(Requirement requirement) => requirement is DocumentClassRequirement;

    public async Task<IReadOnlyList<CapabilityVerdict>> ResolveAsync(
        IReadOnlyList<Requirement> requirements, RenderTarget target, CancellationToken ct = default)
    {
        var classes = requirements.OfType<DocumentClassRequirement>().ToList();
        if (classes.Count == 0) return [];

        if (!CatalogueCoverage.AppliesTo(target))
            return [.. classes.Select(c => CapabilityVerdict.Unknown(
                c, Name, "Typst has no document classes; this catalogue does not describe it"))];

        var slugs = classes.Select(c => c.Name).Distinct().ToList();

        var rows = await context.LatexDocumentClasses
            .Where(c => slugs.Contains(c.Slug))
            .Select(c => new { c.Slug, c.CoverageLevel, c.ShimName, c.DefaultEngine, c.Notes })
            .ToListAsync(ct);

        var bySlug = rows.ToDictionary(r => r.Slug, StringComparer.OrdinalIgnoreCase);

        return [.. classes.Select(cls =>
        {
            if (!bySlug.TryGetValue(cls.Name, out var row))
                return CapabilityVerdict.Unknown(cls, Name, "not in the document-class catalogue");

            var support = CatalogueCoverage.FromLevel(row.CoverageLevel);

            // A class the catalogue answers for names its own preferred engine.
            // Offering it as an alternative is the "and what else would work?"
            // half of the question, which is the part a caller can act on.
            var alternatives = row.DefaultEngine is { Length: > 0 } && !string.Equals(
                    row.DefaultEngine, target.ToWireName(), StringComparison.OrdinalIgnoreCase)
                ? new[] { row.DefaultEngine }
                : [];

            var detail = row.ShimName is { Length: > 0 } ? $"rendered through the {row.ShimName} shim" : row.Notes;

            return new CapabilityVerdict(cls, support, Name, detail, alternatives);
        })];
    }
}

/// <summary>
/// Wraps <c>latex_unicode_map</c> — a replacement macro per code point.
/// </summary>
/// <remarks>
/// <para>This provider answers only <see cref="CodepointRequirement"/>, never
/// <see cref="ScriptRequirement"/>, and the plan is explicit about why:
/// <b>do not extend the unicode map to scripts</b>. A macro per code point
/// works for a few hundred symbols and cannot work for 20,000 CJK code points,
/// nor for Arabic contextual shaping where the glyph depends on its neighbours.
/// Those need a font, which is a different provider's answer.</para>
/// </remarks>
public sealed class LatexUnicodeProvider(LiliaDbContext context) : ICapabilityProvider
{
    public string Name => "latex_unicode_map";
    public bool IsAvailable => true;

    public bool Handles(Requirement requirement) => requirement is CodepointRequirement;

    public async Task<IReadOnlyList<CapabilityVerdict>> ResolveAsync(
        IReadOnlyList<Requirement> requirements, RenderTarget target, CancellationToken ct = default)
    {
        var codepoints = requirements.OfType<CodepointRequirement>().ToList();
        if (codepoints.Count == 0) return [];

        // The map exists because pdflatex cannot take Unicode input directly.
        // Under xelatex, lualatex or Typst the character is passed through, so
        // a replacement macro is not what decides the answer — font coverage
        // is, and that is a different provider.
        if (target is not RenderTarget.Pdflatex)
            return [.. codepoints.Select(c => CapabilityVerdict.Unknown(
                c, Name, $"{target.ToWireName()} takes Unicode directly; coverage depends on the font, not on a macro"))];

        var wanted = codepoints.Select(c => c.Codepoint).Distinct().ToList();

        var rows = await context.LatexUnicodeChars
            .Where(u => wanted.Contains(u.Codepoint))
            .Select(u => new { u.Codepoint, u.CoverageLevel, u.Replacement, u.PackageSlug })
            .ToListAsync(ct);

        var byCodepoint = rows.ToDictionary(r => r.Codepoint);

        return [.. codepoints.Select(cp =>
        {
            if (byCodepoint.TryGetValue(cp.Codepoint, out var row))
            {
                var detail = $"rendered as {row.Replacement}"
                    + (row.PackageSlug is { Length: > 0 } ? $" (package {row.PackageSlug})" : "");
                return new CapabilityVerdict(cp, CatalogueCoverage.FromLevel(row.CoverageLevel), Name, detail);
            }

            // Below U+0080 pdflatex needs no help at all — ASCII is native, and
            // saying Unknown about it would fill a report with noise that
            // trains the reader to skim past the real entries.
            if (cp.Codepoint < 0x80)
                return new CapabilityVerdict(cp, Support.Full, Name, "ASCII");

            // Anything else has no macro. Under pdflatex, which cannot take the
            // character directly either, that is terminal rather than merely
            // missing — no package makes pdflatex render CJK.
            return new CapabilityVerdict(
                cp, Support.Impossible, Name,
                "no replacement macro, and pdflatex cannot take the character directly",
                ["xelatex", "lualatex", "typst"]);
        })];
    }
}
