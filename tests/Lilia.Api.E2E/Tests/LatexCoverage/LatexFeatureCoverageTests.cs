using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.LatexCoverage;

/// <summary>
/// LaTeX feature coverage through the live API — one theory row per catalog
/// feature (<c>Fixtures/latex-features.json</c>), five layers per row:
/// <list type="table">
/// <item><term>api.import</term><description>the feature's <c>document</c> goes through the
///   product's .tex import (<c>POST /api/lilia/imports/latex?autoFinalize=true</c>) and the
///   resulting blocks carry what the catalog's <c>expect</c> says a correct importer produces.</description></item>
/// <item><term>api.export</term><description><c>GET …/export/latex?mode=tex</c> keeps the feature's semantics.</description></item>
/// <item><term>api.compile</term><description>that export compiles with local pdflatex (skip when absent).</description></item>
/// <item><term>api.roundtrip</term><description>import(export(import(tex))) has the same blocks as import(tex).</description></item>
/// <item><term>api.typst</term><description>the document compiles through the product's Typst path
///   (<c>GET …/preview/typst?format=pdf</c>, the engine behind the editor preview and PDF export).</description></item>
/// </list>
///
/// <para><b>Known gaps are strict.</b> A layer listed in
/// <c>Fixtures/latex-known-gaps.json</c> that still fails is recorded as
/// <c>known-gap</c> and does not fail the test; a listed layer that now passes
/// fails the test ("gap closed — remove it"); an unlisted failure fails it.</para>
///
/// <para>Results: set <c>LATEX_COVERAGE_OUT</c> to a file to get one JSON line
/// per feature × layer. The rows are split into one nested class per catalog
/// category so xUnit runs the categories in parallel. Run them all with
/// <c>--filter FullyQualifiedName~LatexFeatureCoverageTests</c>.</para>
/// </summary>
public static class LatexFeatureCoverageTests
{
    public class Structure : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Structure");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Text : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Text");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Lists : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Lists");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Math : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Math");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Theorems : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Theorems");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Tables : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Tables");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Figures : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Figures");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class References : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("References");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Citations : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Citations");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Code : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Code");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Layout : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Layout");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    public class Advanced : LatexFeatureCoverageBase
    {
        public static TheoryData<string> Rows => LatexFeatureCatalog.Ids("Advanced");
        [Theory, MemberData(nameof(Rows))] public Task Feature(string id) => RunFeatureAsync(id);
    }

    /// <summary>Every catalog category has a class above, so no feature is silently not run.</summary>
    public class CatalogIntegrity
    {
        [Fact]
        public void Every_category_has_a_test_class_and_every_gap_names_a_real_layer()
        {
            var covered = typeof(LatexFeatureCoverageTests).GetNestedTypes()
                .Where(t => t.IsSubclassOf(typeof(LatexFeatureCoverageBase))).Select(t => t.Name).ToHashSet();
            var missing = LatexFeatureCatalog.Features.Select(f => f.Category).Distinct().Where(c => !covered.Contains(c)).ToList();
            Assert.True(missing.Count == 0, "categories without a test class: " + string.Join(", ", missing));

            var ids = LatexFeatureCatalog.Features.Select(f => f.Id).ToHashSet();
            var bad = LatexFeatureCatalog.KnownGaps.Keys.Where(k =>
            {
                var slash = k.LastIndexOf('/');
                return slash < 0 || !ids.Contains(k[..slash]) || !LatexFeatureCoverageBase.Layers.Contains(k[(slash + 1)..]);
            }).ToList();
            Assert.True(bad.Count == 0, "latex-known-gaps.json keys that match no feature/layer: " + string.Join(", ", bad));
        }
    }
}

public abstract class LatexFeatureCoverageBase : E2ETestBase
{
    public const string Import = "api.import";
    public const string Export = "api.export";
    public const string Compile = "api.compile";
    public const string Roundtrip = "api.roundtrip";
    public const string TypstLayer = "api.typst";
    public static readonly string[] Layers = [Import, Export, Compile, Roundtrip, TypstLayer];

    private sealed record LayerResult(string Status, string Detail);

    protected async Task RunFeatureAsync(string id)
    {
        var feature = LatexFeatureCatalog.Get(id);
        using var client = await CreateAuthenticatedClientAsync("Owner");
        var api = new LatexCoverageApi(client);
        var results = new Dictionary<string, LayerResult>();
        var fileName = $"latexcov-{id}.tex";

        LatexCoverageApi.ImportResult? first = null, second = null;
        try
        {
            // ── api.import ────────────────────────────────────────────────
            first = await api.ImportTexAsync(feature.Document, fileName);
            JsonElement? doc = null;
            if (first.DocumentId is { } docId)
            {
                doc = await api.GetDocumentAsync(docId);
                results[Import] = Outcome(CheckImport(feature, doc.Value));
            }
            else
            {
                results[Import] = Fail(DescribeNoDocument(first));
            }

            // ── api.export ────────────────────────────────────────────────
            string? tex = null;
            if (first.DocumentId is { } d1)
            {
                var (status, body) = await api.ExportLatexAsync(d1);
                if (status == HttpStatusCode.OK && body.Contains(@"\begin{document}"))
                {
                    tex = body;
                    results[Export] = Outcome(CheckExport(feature, body));
                }
                else
                {
                    results[Export] = Fail($"export returned HTTP {(int)status}: {Short(body)}");
                }
            }
            else
            {
                results[Export] = Fail("no document to export (import produced none)");
            }

            // ── api.compile ───────────────────────────────────────────────
            if (LatexToolchain.PdfLatex is null)
                results[Compile] = new("skip", "pdflatex not on PATH");
            else if (tex is null)
                results[Compile] = Fail("no exported .tex to compile");
            else
            {
                var compiled = await LatexToolchain.CompileLatexAsync(tex);
                results[Compile] = compiled.Ok ? Pass() : Fail(compiled.Detail);
            }

            // ── api.roundtrip ─────────────────────────────────────────────
            if (tex is null || doc is null)
                results[Roundtrip] = Fail("no export to re-import");
            else
            {
                second = await api.ImportTexAsync(tex, fileName);
                if (second.DocumentId is { } d2)
                {
                    var again = await api.GetDocumentAsync(d2);
                    results[Roundtrip] = Outcome(CompareRoundtrip(doc.Value, again));
                }
                else
                {
                    results[Roundtrip] = Fail("re-import: " + DescribeNoDocument(second));
                }
            }

            // ── api.typst ─────────────────────────────────────────────────
            // The product's Typst path (preview + PDF export), compiled by the
            // API's own typst. 503 = Typst could not compile it.
            if (first.DocumentId is not { } d3)
                results[TypstLayer] = Fail("no document to render");
            else
            {
                var (status, bytes) = await api.PreviewTypstPdfAsync(d3);
                var isPdf = bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';
                var text = isPdf ? "" : System.Text.Encoding.UTF8.GetString(bytes);
                if (status == HttpStatusCode.OK && isPdf)
                    results[TypstLayer] = Pass();
                else if (Regex.IsMatch(text, @"typst (binary )?(not found|not installed|unavailable)|No such file or directory", RegexOptions.IgnoreCase))
                    results[TypstLayer] = new("skip", "the API has no typst binary: " + Short(text, 160));
                else
                    results[TypstLayer] = Fail($"typst HTTP {(int)status}: {Short(TypstError(text), 300)}");
            }
        }
        catch (Exception ex)
        {
            foreach (var layer in Layers.Where(l => !results.ContainsKey(l)))
                results[layer] = Fail($"harness error: {ex.GetType().Name}: {Short(ex.Message)}");
        }
        finally
        {
            await api.CleanupAsync(first);
            await api.CleanupAsync(second);
        }

        // ── Known gaps + result file ─────────────────────────────────────
        var problems = new List<string>();
        foreach (var layer in Layers)
        {
            var r = results.TryGetValue(layer, out var got) ? got : Fail("layer did not run");
            var gapKey = $"{id}/{layer}";
            var listed = LatexFeatureCatalog.KnownGaps.ContainsKey(gapKey);
            var status = r.Status;

            if (r.Status == "fail" && listed) status = "known-gap";
            else if (r.Status == "fail") problems.Add($"{layer}: {r.Detail}");
            else if (r.Status == "pass" && listed)
                problems.Add($"{layer}: gap closed — remove \"{gapKey}\" from latex-known-gaps.json");

            CoverageResultWriter.Write(id, layer, status, r.Status == "pass" ? "" : r.Detail);
        }

        Assert.True(problems.Count == 0, $"{id}:\n  " + string.Join("\n  ", problems));
    }

    // ── import checks ────────────────────────────────────────────────────

    private static List<string> CheckImport(LatexFeature feature, JsonElement doc)
    {
        var failures = new List<string>();
        var blocks = Blocks(doc);
        var e = feature.Expect;

        if (blocks.Count == 0) return ["the document has no blocks"];

        var empties = blocks.Count(b => b.Type == "paragraph" && string.IsNullOrWhiteSpace(b.Text));
        if (empties > 0) failures.Add($"{empties} empty paragraph block(s) — content mapped to nothing");

        var cursor = 0;
        foreach (var want in e.Blocks)
        {
            var at = -1;
            for (var i = cursor; i < blocks.Count; i++)
                if (Matches(blocks[i], want)) { at = i; break; }
            if (at < 0)
            {
                failures.Add($"expected block {want} not found (after #{cursor}); got {Summary(blocks)}");
                break; // later expectations would only repeat the same miss
            }
            cursor = at + 1;
        }

        if (e.ExactTypes is { } exact && !exact.SequenceEqual(blocks.Select(b => b.Type)))
            failures.Add($"block types [{string.Join(", ", blocks.Select(b => b.Type))}], expected exactly [{string.Join(", ", exact)}]");

        var all = string.Join("\n", blocks.Select(b => b.Text));
        foreach (var t in e.DocText.Where(t => !Has(all, t)))
            failures.Add($"text {Q(t)} missing from the document");
        foreach (var t in e.Absent.Where(t => all.Contains(t, StringComparison.Ordinal)))
            failures.Add($"leaked {Q(t)} into block text: {Context(all, t)}");

        if (e.BibKeys.Count > 0)
        {
            var keys = BibKeys(doc);
            var missing = e.BibKeys.Where(k => !keys.Contains(k)).ToList();
            if (missing.Count > 0)
                failures.Add($"bibliography lacks [{string.Join(", ", missing)}] (has [{string.Join(", ", keys)}])");
        }
        return failures;
    }

    private static bool Matches(ActualBlock block, ExpectedBlock want)
    {
        if (!string.Equals(block.Type, want.Type, StringComparison.Ordinal)) return false;
        if (want.Text.Any(t => !Has(block.Text, t))) return false;
        foreach (var (key, expected) in want.Fields)
        {
            if (block.Content.ValueKind != JsonValueKind.Object || !block.Content.TryGetProperty(key, out var actual))
                return false;
            if (!JsonEquals(expected, actual)) return false;
        }
        return true;
    }

    private static bool JsonEquals(JsonElement a, JsonElement b) => a.ValueKind switch
    {
        JsonValueKind.String => b.ValueKind == JsonValueKind.String && a.GetString() == b.GetString(),
        JsonValueKind.Number => b.ValueKind == JsonValueKind.Number && a.GetDouble() == b.GetDouble(),
        JsonValueKind.True or JsonValueKind.False => b.ValueKind == a.ValueKind,
        _ => a.GetRawText() == b.GetRawText(),
    };

    // ── export checks ────────────────────────────────────────────────────

    private static List<string> CheckExport(LatexFeature feature, string tex)
    {
        var failures = new List<string>();
        var body = tex[tex.IndexOf(@"\begin{document}", StringComparison.Ordinal)..];
        var e = feature.Expect;

        if (body.Contains(@"\textbackslash") && !feature.Tex.Contains("textbackslash"))
            failures.Add($"raw LaTeX escaped into printed text: {Context(body, @"\textbackslash")}");
        foreach (var marker in new[] { "[Unsupported block type", "% Error rendering block" })
            if (body.Contains(marker)) failures.Add($"exporter wrote {Q(marker)}: {Context(body, marker)}");

        foreach (var t in e.Export.Where(t => !Has(body, t)))
            failures.Add($"export lacks {Q(t)}");
        foreach (var t in e.ExportAbsent.Where(t => Has(body, t)))
            failures.Add($"export has forbidden {Q(t)}");
        foreach (var t in e.ExportFull.Where(t => !Has(tex, t)))
            failures.Add($"export (incl. preamble) lacks {Q(t)}");

        if (failures.Count > 0)
            failures.Add("body: " + Short(StripBoilerplate(body), 260));
        return failures;
    }

    // ── roundtrip ────────────────────────────────────────────────────────

    /// <summary>
    /// Blocks compare by type + canonical content (object keys sorted, string
    /// whitespace collapsed). Two normalisations, both artefacts of how an
    /// export is shaped rather than of the feature: a <c>title</c> block the
    /// re-import synthesises from the exporter's unconditional
    /// <c>\title{…}\maketitle</c> is ignored unless the source had one, and a
    /// figure's <c>src</c> compares by file name (the export moves images
    /// under <c>figures/</c>).
    /// </summary>
    private static List<string> CompareRoundtrip(JsonElement original, JsonElement again)
    {
        var a = Blocks(original);
        var b = Blocks(again);
        if (!a.Any(x => x.Type == "title")) b = b.Where(x => x.Type != "title").ToList();

        var left = a.Select(Canonical).ToList();
        var right = b.Select(Canonical).ToList();
        var failures = new List<string>();

        for (var i = 0; i < System.Math.Max(left.Count, right.Count); i++)
        {
            var l = i < left.Count ? left[i] : "(none)";
            var r = i < right.Count ? right[i] : "(none)";
            if (l != r)
            {
                failures.Add($"block #{i} differs: {Short(l, 160)} → {Short(r, 160)}" +
                             (left.Count != right.Count ? $" ({left.Count} blocks → {right.Count})" : ""));
                break;
            }
        }

        var k1 = BibKeys(original);
        var k2 = BibKeys(again);
        if (!k1.SetEquals(k2))
            failures.Add($"bibliography [{string.Join(", ", k1)}] → [{string.Join(", ", k2)}]");
        return failures;
    }

    private static string Canonical(ActualBlock block) => block.Type + ":" + CanonicalJson(block.Content, null);

    private static string CanonicalJson(JsonElement e, string? property) => e.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Name + ":" + CanonicalJson(p.Value, p.Name))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", e.EnumerateArray().Select(x => CanonicalJson(x, property))) + "]",
        JsonValueKind.String => JsonSerializer.Serialize(NormaliseString(e.GetString() ?? "", property)),
        _ => e.GetRawText(),
    };

    private static string NormaliseString(string s, string? property)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return property == "src" ? Path.GetFileName(s) : s;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private sealed record ActualBlock(string Type, JsonElement Content, string Text);

    private static List<ActualBlock> Blocks(JsonElement doc)
    {
        if (!doc.TryGetProperty("blocks", out var arr) || arr.ValueKind != JsonValueKind.Array) return new();
        return arr.EnumerateArray()
            .OrderBy(b => b.TryGetProperty("sortOrder", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0)
            .Select(b =>
            {
                var content = b.TryGetProperty("content", out var c) ? c.Clone() : default;
                return new ActualBlock(b.GetProperty("type").GetString() ?? "", content, string.Join("\n", Strings(content)));
            })
            .ToList();
    }

    private static IEnumerable<string> Strings(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                yield return e.GetString() ?? "";
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    foreach (var s in Strings(p.Value)) yield return s;
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray())
                    foreach (var s in Strings(x)) yield return s;
                break;
        }
    }

    private static HashSet<string> BibKeys(JsonElement doc)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (!doc.TryGetProperty("bibliography", out var bib) || bib.ValueKind != JsonValueKind.Array) return keys;
        foreach (var entry in bib.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
            foreach (var p in entry.EnumerateObject())
                if (p.Name.Contains("key", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    keys.Add(p.Value.GetString() ?? "");
        return keys;
    }

    /// <summary>A plain substring, or a regex when prefixed with <c>re:</c>.</summary>
    private static bool Has(string haystack, string needle) =>
        needle.StartsWith("re:", StringComparison.Ordinal)
            ? Regex.IsMatch(haystack, needle[3..])
            : haystack.Contains(needle, StringComparison.Ordinal);

    /// <summary>The 503 body is {error, detail, fallback}; detail holds typst's own message.</summary>
    private static string TypstError(string body)
    {
        try
        {
            var json = JsonDocument.Parse(body).RootElement;
            if (json.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String)
            {
                var detail = d.GetString() ?? "";
                var err = detail.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("error", StringComparison.Ordinal));
                return err ?? detail;
            }
        }
        catch (JsonException)
        {
        }
        return body;
    }

    private static string DescribeNoDocument(LatexCoverageApi.ImportResult r) =>
        r.Status == "COMPLETED"
            ? "job COMPLETED without a document — the import was held for review (auto-finalize gate refused it)"
            : $"import job {r.Status}: {Short(r.Error ?? "")}";

    private static string Summary(List<ActualBlock> blocks) =>
        "[" + string.Join(", ", blocks.Select(b => $"{b.Type}:{Short(b.Content.GetRawText(), 90)}")) + "]";

    private static string StripBoilerplate(string body) =>
        Regex.Replace(body.Replace(@"\begin{document}", "").Replace(@"\maketitle", "").Replace(@"\end{document}", ""), @"\s+", " ").Trim();

    private static string Context(string text, string needle)
    {
        var i = text.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return "";
        var start = System.Math.Max(0, i - 40);
        return "…" + Short(text.Substring(start, System.Math.Min(text.Length - start, needle.Length + 80)), 140) + "…";
    }

    private static string Q(string s) => "\"" + s + "\"";

    private static string Short(string s, int max = 200)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > max ? s[..max] + "…" : s;
    }

    private static LayerResult Pass() => new("pass", "");
    private static LayerResult Fail(string detail) => new("fail", detail);

    private static LayerResult Outcome(List<string> failures) =>
        failures.Count == 0 ? Pass() : Fail(Short(string.Join("; ", failures), 600));
}
