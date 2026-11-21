namespace Lilia.Api.Features.BulkImport;

/// <summary>The <c>BulkImport</c> settings (appsettings.json), with safe defaults.</summary>
public sealed class BulkImportOptions
{
    public const string Section = "BulkImport";

    /// <summary>
    /// An amqp URI with its vhost (<c>amqp://user:pass@127.0.0.1:5672/lilia</c>). Set: the slice's messages go to
    /// RabbitMQ queues. Empty (CI, tests, dev): to durable local queues of the same names. Never logged.
    /// </summary>
    public string RabbitMq { get; set; } = "";

    /// <summary>Pages per chunk read by Mathpix on its own.</summary>
    public int PagesPerChunk { get; set; } = 10;

    /// <summary>Chunks read by Mathpix at once in this process (the ocr queue's parallelism).</summary>
    public int MaxParallelChunks { get; set; } = 4;

    /// <summary>Chunks of one author in flight at once, so one author's 41 files never starve another's.</summary>
    public int MaxChunksPerAuthor { get; set; } = 2;

    public int MaxFilesPerBatch { get; set; } = 50;

    /// <summary>One PDF per request, under Cloudflare's 100 MB body cap.</summary>
    public long MaxFileBytes { get; set; } = 95L * 1024 * 1024;

    /// <summary>Reads of a chunk before it is marked failed.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// The wait before each retry of a chunk, in seconds (backoff); the last is used for later ones. Null: 10 s, then 60 s
    /// (a default array would be added to by the configuration binder, not replaced).
    /// </summary>
    public double[]? RetryDelaysSeconds { get; set; }

    /// <summary>How long a chunk refused by its author's bound waits before it is tried again.</summary>
    public double FairnessDelaySeconds { get; set; } = 3;

    /// <summary>How often Mathpix's status is asked while a chunk is read: its pages read so far, for per-page progress.</summary>
    public double PollSeconds { get; set; } = 3;

    /// <summary>The longest one chunk's reading may take at Mathpix before the attempt fails.</summary>
    public int ReadTimeoutMinutes { get; set; } = 15;

    /// <summary>The longest the tex.zip conversion may take once Mathpix has read a chunk.</summary>
    public int TexZipWaitSeconds { get; set; } = 180;

    /// <summary>The shortest gap between two progress messages of one file on the hub (about 2 a second).</summary>
    public int ProgressIntervalMs { get; set; } = 500;

    /// <summary>How long splitting one PDF with poppler (the fallback) may take.</summary>
    public int SplitTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// A chunk read or split started this long ago and not finished is taken as lost (a restart) and sent again; a chunk
    /// with a Mathpix pdf_id picks that reading up rather than paying for a new one.
    /// </summary>
    public int StaleAfterMinutes { get; set; } = 20;

    /// <summary>How often lost work is looked for; 0 turns the sweep off.</summary>
    public int SweepEverySeconds { get; set; } = 60;

    /// <summary>
    /// A page whose precision (how sure Mathpix was of it, 0–1, from its lines.json) is under this is a warning to check
    /// it against the scan (the precision score, 21 Nov 2026).
    /// </summary>
    public double LowConfidence { get; set; } = 0.80;

    /// <summary>Compile each finished file's document once in the background, to time it (21 Nov 2026).</summary>
    public bool CompileWhenDone { get; set; } = true;

    public TimeSpan RetryDelay(int attempt)
    {
        var delays = RetryDelaysSeconds is { Length: > 0 } d ? d : [10, 60];
        var i = Math.Clamp(attempt - 1, 0, delays.Length - 1);
        return TimeSpan.FromSeconds(Math.Max(0, delays[i]));
    }
}
