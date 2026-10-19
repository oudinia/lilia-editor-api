namespace Lilia.Api.Services.Semantic;

/// <summary>What a text was classified as: the best label, its score, and the runner-up's.</summary>
public sealed record Classified(string Label, float Score, string? Second, float SecondScore)
{
    /// <summary>How clearly the best label won: its score minus the runner-up's.</summary>
    public float Margin => Score - SecondScore;
}

/// <summary>
/// Classifies a text by the examples it is closest to (19 Oct). Each label has a handful of example
/// texts; a text gets the label whose three nearest examples are, on average, nearest to it. Changing
/// a label is changing its examples: no training. Example vectors are computed once, on first use.
/// </summary>
public sealed class ExampleClassifier
{
    private readonly ITextEmbedder _embedder;
    private readonly IReadOnlyDictionary<string, string[]> _examples;
    private readonly Lazy<(IReadOnlyList<(string Label, float[] Vector)> Vectors, float[] Mean)> _vectors;
    private readonly int _k;
    private readonly bool _center;

    /// <param name="k">How many of a label's nearest examples count.</param>
    /// <param name="center">Take the examples' average out of every vector first: this model's vectors all
    /// lean the same way (every pair scores about 0.85), and what is left tells the labels apart.</param>
    public ExampleClassifier(ITextEmbedder embedder, IReadOnlyDictionary<string, string[]> examples, int k = 3, bool center = true)
    {
        _embedder = embedder;
        _examples = examples;
        _k = k;
        _center = center;
        _vectors = new Lazy<(IReadOnlyList<(string, float[])>, float[])>(() =>
        {
            var raw = _examples.SelectMany(kv => kv.Value.Select(e => (kv.Key, _embedder.Embed(e)!))).ToList();
            var mean = new float[raw[0].Item2.Length];
            if (_center)
            {
                foreach (var (_, v) in raw) for (var i = 0; i < mean.Length; i++) mean[i] += v[i] / raw.Count;
            }
            return (raw.Select(x => (x.Key, Centered(x.Item2, mean))).ToList(), mean);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static float[] Centered(float[] v, float[] mean)
    {
        var c = new float[v.Length];
        double norm = 0;
        for (var i = 0; i < v.Length; i++) { c[i] = v[i] - mean[i]; norm += c[i] * c[i]; }
        var inv = norm > 0 ? (float)(1 / Math.Sqrt(norm)) : 0f;
        for (var i = 0; i < c.Length; i++) c[i] *= inv;
        return c;
    }

    public bool Ready => _embedder.Ready;

    /// <summary>The label of <paramref name="text"/>, or null when the model is not there.</summary>
    public Classified? Classify(string text)
    {
        if (!_embedder.Ready || string.IsNullOrWhiteSpace(text)) return null;
        var raw = _embedder.Embed(text);
        if (raw is null) return null;
        var (vectors, mean) = _vectors.Value;
        var v = Centered(raw, mean);
        var ranked = vectors
            .GroupBy(x => x.Label)
            .Select(g =>
            {
                var top = g.Select(x => E5Embedder.Cosine(v, x.Vector)).OrderByDescending(s => s).Take(_k).ToList();
                return (Label: g.Key, Score: top.Average());
            })
            .OrderByDescending(x => x.Score)
            .ToList();
        var second = ranked.Count > 1 ? ranked[1] : (Label: (string?)null, Score: 0f);
        return new Classified(ranked[0].Label, ranked[0].Score, second.Label, second.Score);
    }
}
