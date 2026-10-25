using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;

namespace Lilia.Engines.Publishers;

/// <summary>One thing a publisher would reject or query. <c>error</c> blocks a submission without a
/// confirmation; <c>warning</c> is likely to be queried; <c>info</c> is worth knowing.</summary>
public sealed record ComplianceFinding(string Severity, string Code, string Message, string? BlockId = null);

/// <summary>The findings, the verdict (<c>passed</c>, <c>warnings</c> or <c>errors</c>) and where the rules come from.</summary>
public sealed record ComplianceReport(string? Profile, string Status, IReadOnlyList<ComplianceFinding> Findings, string? GuidelinesUrl);

/// <summary>
/// The publisher compliance check (publisher templates step 7, requirement FR-6, 25 Oct 2026): what a
/// document with a publisher profile or class still lacks or overrides before it is sent. It reads the
/// document and the LaTeX Lilia emits for it; it never compiles (the document check does that).
///
/// <para>Rules: the class and options against the profile; the metadata the profile requires (title,
/// authors, affiliations, ACM's country per author, abstract, keywords); citations and references that
/// would print ? or ??; figures without an image, or in a format pdflatex can't include; template
/// sample text left in; and the author's own preamble or raw LaTeX changing the layout (margins,
/// spacing, fonts) or the bibliography style, which publishers reject.</para>
/// </summary>
public static partial class PublisherCompliance
{
    public static ComplianceReport Check(Document doc, IReadOnlyList<Block> blocks, string emittedLatex)
    {
        var profile = PublisherProfiles.Find(doc.PublisherProfile);
        var cls = PublisherClasses.Canonical(doc.LatexDocumentClass);
        var findings = new List<ComplianceFinding>();
        if (profile is null && cls is null)
            return new ComplianceReport(null, "passed", findings, null);

        // ── The class and its options ──
        if (profile is not null)
        {
            if (!string.Equals(cls, profile.DocumentClass, StringComparison.Ordinal))
                findings.Add(new("error", "class", $"{profile.Publisher} {profile.Venue} uses the {profile.DocumentClass} class; this document is set to {doc.LatexDocumentClass ?? "article"}."));
            else if (Options(doc.LatexDocumentClassOptions) is var mine && !mine.SetEquals(Options(profile.ClassOptions)))
                findings.Add(new("warning", "class_options",
                    $"The class options were changed from the profile's ({Show(profile.ClassOptions)}) to {Show(doc.LatexDocumentClassOptions)}."));
        }

        // ── The metadata the publisher requires ──
        var required = profile?.RequiredMetadata ?? ["title", "authors", "abstract"];
        var titleBlock = blocks.FirstOrDefault(b => b.Type == BlockTypes.Title);
        var title = Text(titleBlock, "title") is { Length: > 0 } t ? t : doc.Title;
        var authors = TitleAuthors.Parse(Text(titleBlock, "author"));
        if (required.Contains("title") && (string.IsNullOrWhiteSpace(title) || title.Trim() == "Untitled"))
            findings.Add(new("error", "title", "The document has no title.", titleBlock?.Id.ToString()));
        if (required.Contains("authors") && authors.Count == 0)
            findings.Add(new("error", "authors", "No author is given in the title block.", titleBlock?.Id.ToString()));
        if (required.Contains("affiliations"))
            foreach (var a in authors.Where(a => a.Lines.Count == 0))
                findings.Add(new("warning", "affiliation", $"{a.Name} has no affiliation (a line under the name in the title block).", titleBlock?.Id.ToString()));
        if (required.Contains("country"))
            foreach (var a in authors.Where(a => a.Lines.Count < 2))
                findings.Add(new("warning", "country",
                    $"ACM wants a country for each author: put it as the last line of {a.Name}'s affiliation.", titleBlock?.Id.ToString()));
        if (required.Contains("abstract") && !blocks.Any(b => b.Type == BlockTypes.Abstract && HasText(b)))
            findings.Add(new("error", "abstract", "The document has no abstract."));
        if (required.Contains("keywords") && RenderService.TitleKeywords(new Document { Blocks = blocks.ToList() }).Count == 0)
            findings.Add(new("warning", "keywords", "No keywords: add them in the title block.", titleBlock?.Id.ToString()));

        // ── Citations and references that would print ? or ?? ──
        var audit = new EmittedLatexAudit(emittedLatex);
        // The export's references are in references.bib, not inline: a key is known when the document
        // (or a bibliography block, as BibTeX or as text) has its entry.
        var known = (doc.BibliographyEntries ?? []).Select(e => e.CiteKey)
            .Concat(BibliographyBlocks.BibTeX(blocks).Select(e => e.CiteKey))
            .Concat(BibliographyBlocks.Text(blocks).Select(e => e.Key))
            .Concat(audit.BibliographyKeys)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var c in audit.Citations.Where(c => c.Key != "*" && !known.Contains(c.Key)).DistinctBy(c => c.Key))
            findings.Add(new("error", "citation", $"Citation '{c.Key}' has no bibliography entry: it prints ?.", c.BlockId?.ToString()));
        foreach (var r in audit.DanglingReferences.DistinctBy(r => r.Key))
            findings.Add(new("error", "reference", $"Reference to '{r.Key}', a label the document never writes: it prints ??.", r.BlockId?.ToString()));

        // ── Figures ──
        var engine = string.IsNullOrWhiteSpace(doc.LatexEngine) ? "pdflatex" : doc.LatexEngine;
        foreach (var f in blocks.Where(b => b.Type == BlockTypes.Figure))
        {
            var sources = FigureFile.Sources([f]).Select(s => s.Src).ToList();
            if (sources.Count == 0)
                findings.Add(new("error", "figure_missing", "A figure has no image: it prints as an empty frame.", f.Id.ToString()));
            foreach (var name in sources.Select(s => FigureFile.Name(s)))
            {
                var ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext is ".gif" or ".webp" or ".svg" or ".bmp" or ".tif" or ".tiff")
                    findings.Add(new("error", "figure_format", $"{name}: {engine} can't include {ext[1..].ToUpperInvariant()} images; use PDF, PNG or JPG.", f.Id.ToString()));
                else if (ext == ".eps" && engine == "pdflatex")
                    findings.Add(new("info", "figure_format", $"{name}: EPS is converted to PDF when compiled; a PDF of it is safer.", f.Id.ToString()));
            }
        }

        // ── Sample text left from a template ──
        foreach (var b in blocks)
            if (SampleText().Match(b.Content?.RootElement.GetRawText() ?? "") is { Success: true } m)
                findings.Add(new("warning", "sample_text", $"Template text is still in the document (\"{m.Value}\").", b.Id.ToString()));

        // ── The author's own LaTeX changing the layout or the references ──
        var own = new List<(string Source, string? BlockId)> { (doc.CustomPreamble ?? "", null), (ImportedPackages(doc.LatexPackages), null) };
        own.AddRange(blocks.Where(b => b.Type == BlockTypes.Embed && (Text(b, "engine") ?? "latex") == "latex")
            .Select(b => (Text(b, "code") ?? "", (string?)b.Id.ToString())));
        foreach (var (source, blockId) in own)
        {
            foreach (Match m in LayoutOverride().Matches(source))
                findings.Add(new("error", "layout_override",
                    $"{m.Value.Trim()} changes the publisher's layout; {profile?.Publisher ?? "publishers"} reject submissions that do.", blockId));
            if (BibStyle().Match(source) is { Success: true } bs)
                findings.Add(new("warning", "bibliography_style",
                    $"{bs.Value} sets its own reference style; the export uses {PublisherProfiles.BibliographyStyleFor(doc.PublisherProfile, cls)}.", blockId));
        }

        // ── Packages the publisher doesn't accept (ACM's TAPS list) ──
        if (PublisherPackages.Accepted(cls) is { } accepted)
        {
            var mine = new HashSet<string>(PublisherPackages.Loaded(doc.CustomPreamble ?? "").Concat(PublisherPackages.Loaded(ImportedPackages(doc.LatexPackages))), StringComparer.Ordinal);
            var publisher = profile?.Publisher ?? "The publisher";
            foreach (var package in PublisherPackages.Loaded(emittedLatex).Where(p => !accepted.Contains(p)))
                findings.Add(mine.Contains(package)
                    ? new("error", "package_not_accepted", $"{package} isn't on {publisher}'s list of accepted packages: its production system rejects the source. Remove it, or do without it.")
                    : new("warning", "package_not_accepted", $"{package} isn't on {publisher}'s list of accepted packages; Lilia loads it for your {PublisherPackages.FeatureOf(package) ?? "document"}. Do without that feature, or ask {publisher} first."));
            if (cls == "acmart")
                foreach (var package in mine.Where(PublisherPackages.AcmartLoads.Contains))
                    findings.Add(new("info", "package_in_class", $"acmart already loads {package}: your \\usepackage{{{package}}} can go."));
        }

        findings = findings.DistinctBy(f => (f.Code, f.Message, f.BlockId)).ToList();
        var status = findings.Any(f => f.Severity == "error") ? "errors"
            : findings.Any(f => f.Severity == "warning") ? "warnings" : "passed";
        return new ComplianceReport(profile?.Id, status, findings, profile?.GuidelinesUrl);
    }

    // The packages an import kept (a JSON list of {name, options}), as the lines they load.
    private static string ImportedPackages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.ValueKind != JsonValueKind.Array) return "";
            return string.Join("\n", d.RootElement.EnumerateArray()
                .Where(p => p.ValueKind == JsonValueKind.Object && p.TryGetProperty("name", out _))
                .Select(p => $"\\usepackage{{{p.GetProperty("name").GetString()}}}"));
        }
        catch (JsonException) { return ""; }
    }

    private static HashSet<string> Options(string? options) =>
        (options ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static string Show(string? options) => string.IsNullOrWhiteSpace(options) ? "none" : options;

    private static string? Text(Block? b, string field) =>
        b?.Content?.RootElement is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static bool HasText(Block b) =>
        (Text(b, "text") ?? Text(b, "content") ?? b.Content?.RootElement.GetRawText() ?? "").Trim('{', '}', ' ', '"').Length > 0;

    [GeneratedRegex(@"\\usepackage(?:\[[^\]]*\])?\{(?:geometry|fullpage|times|mathptmx|lmodern|setspace|titlesec|savetrees|a4wide)\}|\\(?:new)?geometry\{|\\setlength\{?\\(?:textwidth|textheight|oddsidemargin|evensidemargin|topmargin|columnsep|baselineskip|parskip)\b|\\linespread\{|\\renewcommand\{?\\baselinestretch|\\(?:addtolength)\{?\\(?:textwidth|textheight)")]
    private static partial Regex LayoutOverride();

    [GeneratedRegex(@"\\bibliographystyle\{[^}]*\}")]
    private static partial Regex BibStyle();

    [GeneratedRegex(@"(?i)\b(?:lorem ipsum|insert (?:your )?title here|your name here|author name|title of the paper|ACM Reference Format:? [A-Z]\. Author)\b")]
    private static partial Regex SampleText();
}
