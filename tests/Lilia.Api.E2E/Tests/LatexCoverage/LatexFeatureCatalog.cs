using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lilia.Api.E2E.Tests.LatexCoverage;

/// <summary>
/// The shared LaTeX feature catalog (<c>Fixtures/latex-features.json</c>) and
/// the known-gap list (<c>Fixtures/latex-known-gaps.json</c>). The UI suite in
/// the web repo keeps an identical copy of the catalog; <c>id</c> and
/// <c>tex</c> are the shared contract, <c>document</c> is the exact .tex both
/// suites import.
/// </summary>
public static class LatexFeatureCatalog
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<IReadOnlyList<LatexFeature>> _features = new(() =>
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(Fixture("latex-features.json")), Json)
            ?? throw new InvalidOperationException("latex-features.json is empty");
        return file.Features;
    });

    private static readonly Lazy<IReadOnlyDictionary<string, string>> _gaps = new(() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Fixture("latex-known-gaps.json")), Json)
            ?.Where(kv => !kv.Key.StartsWith('$')) // "$comment" keys are documentation
            .ToDictionary(kv => kv.Key, kv => kv.Value)
        ?? new Dictionary<string, string>());

    public static IReadOnlyList<LatexFeature> Features => _features.Value;

    /// <summary>"featureId/layer" → the reason the layer is known to fail.</summary>
    public static IReadOnlyDictionary<string, string> KnownGaps => _gaps.Value;

    public static LatexFeature Get(string id) =>
        Features.SingleOrDefault(f => f.Id == id) ?? throw new InvalidOperationException($"No feature '{id}' in the catalog");

    public static TheoryData<string> Ids(params string[] categories)
    {
        var data = new TheoryData<string>();
        foreach (var f in Features.Where(f => categories.Contains(f.Category)))
            data.Add(f.Id);
        return data;
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private sealed class CatalogFile
    {
        public List<LatexFeature> Features { get; set; } = new();
    }
}

public sealed class LatexFeature
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Tex { get; set; } = "";
    public string? Note { get; set; }
    public string Document { get; set; } = "";
    public FeatureExpectation Expect { get; set; } = new();

    public override string ToString() => Id;
}

public sealed class FeatureExpectation
{
    public List<ExpectedBlock> Blocks { get; set; } = new();
    public List<string>? ExactTypes { get; set; }
    public List<string> DocText { get; set; } = new();
    public List<string> Absent { get; set; } = new();
    public List<string> BibKeys { get; set; } = new();
    public List<string> Export { get; set; } = new();
    public List<string> ExportAbsent { get; set; } = new();
    public List<string> ExportFull { get; set; } = new();
}

public sealed class ExpectedBlock
{
    public string Type { get; set; } = "";
    public List<string> Text { get; set; } = new();
    public Dictionary<string, JsonElement> Fields { get; set; } = new();

    public override string ToString() =>
        $"{Type}{(Text.Count > 0 ? " [" + string.Join(" | ", Text) + "]" : "")}" +
        $"{(Fields.Count > 0 ? " {" + string.Join(", ", Fields.Select(f => $"{f.Key}={f.Value.GetRawText()}")) + "}" : "")}";
}
