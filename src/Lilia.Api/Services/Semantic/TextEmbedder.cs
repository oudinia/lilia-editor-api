using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Lilia.Api.Services.Semantic;

/// <summary>Turns a short text into a vector whose closeness to another means closeness of meaning.</summary>
public interface ITextEmbedder
{
    /// <summary>False when the model files are absent or failed to load: callers fall back to what they did before.</summary>
    bool Ready { get; }

    /// <summary>The unit-length embedding of <paramref name="text"/>, or null when not <see cref="Ready"/>.</summary>
    float[]? Embed(string text);
}

/// <summary>
/// multilingual-e5-small (MIT), run on this server with ONNX Runtime (19 Oct; the user: "a free
/// solution to classify for Lilia"). 384 numbers per text, in 100 languages, a few milliseconds on
/// the processor; nothing leaves the machine. The files come from <c>ops/models.sh</c> (pinned,
/// checksummed) into <c>Models:Dir</c>; without them <see cref="Ready"/> is false and nothing breaks.
///
/// <para>E5 expects "query: " before every text; vectors are the mean of the last layer over the
/// real tokens, normalised. The SentencePiece ids are the model's minus one (fairseq's offset:
/// 0–3 are &lt;s&gt; &lt;pad&gt; &lt;/s&gt; &lt;unk&gt;), so 1 is added and &lt;s&gt; … &lt;/s&gt; put around.</para>
/// </summary>
public sealed class E5Embedder : ITextEmbedder, IDisposable
{
    public const string ModelName = "multilingual-e5-small";
    private const int MaxTokens = 128;
    private const long Bos = 0, Eos = 2, Unk = 3;

    private readonly Lazy<(InferenceSession Session, SentencePieceTokenizer Tokenizer)?> _loaded;
    private readonly ILogger<E5Embedder> _logger;

    public E5Embedder(IConfiguration configuration, ILogger<E5Embedder> logger)
    {
        _logger = logger;
        var root = configuration["Models:Dir"] is { Length: > 0 } d ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/lilia/models");
        var dir = Path.Combine(root, ModelName);
        _loaded = new Lazy<(InferenceSession, SentencePieceTokenizer)?>(() => Load(dir), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>For tests: a model directory given directly.</summary>
    public E5Embedder(string modelDir, ILogger<E5Embedder> logger)
    {
        _logger = logger;
        _loaded = new Lazy<(InferenceSession, SentencePieceTokenizer)?>(() => Load(modelDir), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private (InferenceSession, SentencePieceTokenizer)? Load(string dir)
    {
        var model = Path.Combine(dir, "model_quantized.onnx");
        var spm = Path.Combine(dir, "sentencepiece.bpe.model");
        if (!File.Exists(model) || !File.Exists(spm))
        {
            _logger.LogInformation("[Semantic] {Model} not installed in {Dir} (ops/models.sh): keyword routing only", ModelName, dir);
            return null;
        }
        try
        {
            var options = new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            var session = new InferenceSession(model, options);
            using var stream = File.OpenRead(spm);
            var tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false);
            _logger.LogInformation("[Semantic] {Model} loaded", ModelName);
            return (session, tokenizer);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "[Semantic] {Model} failed to load: keyword routing only", ModelName);
            return null;
        }
    }

    public bool Ready => _loaded.Value is not null;

    /// <summary>The model's token ids for <paramref name="text"/>, with "query: " and the sentence marks.</summary>
    internal long[] Ids(string text)
    {
        var tokenizer = _loaded.Value!.Value.Tokenizer;
        var pieces = tokenizer.EncodeToIds("query: " + text.Trim());
        var ids = new List<long>(Math.Min(pieces.Count, MaxTokens - 2) + 2) { Bos };
        foreach (var id in pieces.Take(MaxTokens - 2)) ids.Add(id == 0 ? Unk : id + 1);
        ids.Add(Eos);
        return ids.ToArray();
    }

    public float[]? Embed(string text)
    {
        if (_loaded.Value is not { } m) return null;
        var ids = Ids(text);
        var n = ids.Length;
        var inputs = new List<NamedOnnxValue>();
        foreach (var name in m.Session.InputMetadata.Keys)
        {
            var values = name switch
            {
                "input_ids" => ids,
                "attention_mask" => Enumerable.Repeat(1L, n).ToArray(),
                _ => new long[n],   // token_type_ids, when the export has them
            };
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values, new[] { 1, n })));
        }
        using var results = m.Session.Run(inputs);
        var hidden = results.First().AsTensor<float>();   // [1, n, 384]
        var dim = hidden.Dimensions[2];
        var v = new float[dim];
        for (var t = 0; t < n; t++)
            for (var k = 0; k < dim; k++) v[k] += hidden[0, t, k];
        double norm = 0;
        for (var k = 0; k < dim; k++) { v[k] /= n; norm += v[k] * v[k]; }
        var inv = (float)(1 / Math.Sqrt(norm));
        for (var k = 0; k < dim; k++) v[k] *= inv;
        return v;
    }

    public static float Cosine(float[] a, float[] b)
    {
        float s = 0;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    public void Dispose()
    {
        if (_loaded.IsValueCreated && _loaded.Value is { } m) m.Session.Dispose();
    }
}
