namespace Lilia.Core.Entities;

/// <summary>
/// Bulk import (21 Nov 2026, the owner: mass conversion of scanned maths notebooks, the Forkosh case of ~41 PDFs of
/// ~150 pages). An author drops many PDFs at once; each is a <see cref="BulkImportFile"/>, split into
/// <see cref="BulkImportChunk"/>s of a few pages that Mathpix reads in parallel. The file's document exists from the
/// split on, one placeholder block per chunk, and grows as each chunk's reading replaces its placeholder. The batch is
/// what one drop made.
/// </summary>
public class BulkImportBatch
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    /// <summary><see cref="BulkImportStatuses"/>: open (no file yet), processing, done, partial, failed, cancelled.</summary>
    public string Status { get; set; } = BulkImportStatuses.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual User? User { get; set; }
    public virtual ICollection<BulkImportFile> Files { get; set; } = new List<BulkImportFile>();
}

/// <summary>One PDF of a batch: its original kept in storage, its exact pages, and the document it becomes.</summary>
public class BulkImportFile
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    /// <summary>The original PDF in object storage (<c>IStorageService</c>), kept for a retry and as each chunk's source.</summary>
    public string StorageKey { get; set; } = string.Empty;
    /// <summary>SHA-256 of the original, hex: with a chunk's pages, the key of its cached reading.</summary>
    public string? SourceHash { get; set; }
    public int Pages { get; set; }
    /// <summary>queued, splitting, reading, done, partial, failed, cancelled (<see cref="BulkImportStatuses"/>).</summary>
    public string Status { get; set; } = BulkImportStatuses.Queued;
    /// <summary>The document, set as soon as the file is split; null again if the document is erased.</summary>
    public Guid? DocumentId { get; set; }
    /// <summary>Whether a chunk's preamble is on the document yet: the first chunk placed sets it whole, later ones add missing macros.</summary>
    public bool PreambleApplied { get; set; }
    public string? Error { get; set; }
    /// <summary>The import's warnings (each chunk's LaTeX import diagnostics), summed.</summary>
    public int Warnings { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual BulkImportBatch? Batch { get; set; }
    public virtual ICollection<BulkImportChunk> Chunks { get; set; } = new List<BulkImportChunk>();
}

/// <summary>A few pages of a file (<c>BulkImport:PagesPerChunk</c>), read by Mathpix on its own.</summary>
public class BulkImportChunk
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }
    public string UserId { get; set; } = string.Empty;
    /// <summary>Its place in the file, from 0.</summary>
    public int Index { get; set; }
    /// <summary>The file's first and last page in this chunk, from 1, inclusive.</summary>
    public int FirstPage { get; set; }
    public int LastPage { get; set; }
    /// <summary>
    /// queued (waiting its turn), sent (on the ocr queue), reading (at Mathpix), read (waiting to be placed), done (placed),
    /// failed, cancelled (<see cref="BulkImportStatuses"/>).
    /// </summary>
    public string Status { get; set; } = BulkImportStatuses.Queued;
    public int Attempts { get; set; }
    /// <summary>The chunk's own PDF in object storage, until it is placed.</summary>
    public string? ChunkKey { get; set; }
    /// <summary>Mathpix's pdf_id for the read in flight: a read interrupted by a restart is picked up, not paid twice.</summary>
    public string? MathpixPdfId { get; set; }
    /// <summary>Pages Mathpix has read so far (its <c>num_pages_completed</c>), for per-page progress.</summary>
    public int PagesCompleted { get; set; }
    /// <summary>The <see cref="MathpixChunkCache"/> key: SHA-256 of the source file's hash and the chunk's pages.</summary>
    public string? ContentHash { get; set; }
    /// <summary>The read result (Mathpix's tex.zip) in object storage.</summary>
    public string? ResultKey { get; set; }
    /// <summary>True when the result came from the cache: no Mathpix call, nothing charged.</summary>
    public bool FromCache { get; set; }
    /// <summary>The pages charged to the author for this chunk: 0 until Mathpix's reading is stored, never for a failure.</summary>
    public int ChargedPages { get; set; }
    public string? Error { get; set; }
    /// <summary>The placeholder block on the document that the chunk's blocks replace.</summary>
    public Guid? PlaceholderBlockId { get; set; }
    /// <summary>When the chunk was last claimed for reading: a read stuck past the stale limit is sent again.</summary>
    public DateTime? StartedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int PageCount => LastPage - FirstPage + 1;

    public virtual BulkImportFile? File { get; set; }
}

/// <summary>
/// What Mathpix made of some pages, by <see cref="BulkImportChunk.ContentHash"/>: the same pages read again (a
/// re-import, the same file twice) reuse the result without a Mathpix call.
/// </summary>
public class MathpixChunkCache
{
    public string Hash { get; set; } = string.Empty;
    public string ResultKey { get; set; } = string.Empty;
    public int Pages { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>The statuses of bulk import batches, files and chunks.</summary>
public static class BulkImportStatuses
{
    public const string Open = "open";
    public const string Processing = "processing";
    public const string Queued = "queued";
    public const string Splitting = "splitting";
    public const string Reading = "reading";
    /// <summary>A chunk handed to the ocr queue, not yet at Mathpix (an author has at most <c>MaxChunksPerAuthor</c> sent or reading).</summary>
    public const string Sent = "sent";
    /// <summary>A chunk read and stored, waiting for its place in the document.</summary>
    public const string Read = "read";
    public const string Done = "done";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    /// <summary>A file in one of these has nothing left to do.</summary>
    public static readonly string[] FileFinished = [Done, Partial, Failed, Cancelled];

    /// <summary>A chunk in one of these has nothing left to do.</summary>
    public static readonly string[] ChunkFinished = [Done, Failed, Cancelled];
}
