namespace Lilia.Core.Entities;

/// <summary>
/// Bulk import (21 Nov 2026, the owner: mass conversion of scanned maths notebooks, the Forkosh case of ~41 PDFs of
/// ~150 pages). An author drops many PDFs at once; each is a <see cref="BulkImportFile"/>, split into
/// <see cref="BulkImportChunk"/>s of a few pages that Mathpix reads in parallel. Since 21 Nov 2026 (the owner: "a staging
/// table for the whole import until it ends… so we don't clash with the final tables") each chunk's blocks are staged
/// (<see cref="BulkImportStagedBlock"/>, <see cref="BulkImportStagedAsset"/>) and the file's document is made in one go
/// when every chunk is finished (promotion). The batch is what one drop made.
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
    /// <summary>
    /// The document: set when the file is promoted (every chunk finished, its staged blocks made into the document in one
    /// go); null before, and null again if the document is erased. A file split before staging (<see cref="UsesStaging"/>
    /// false) got its document at the split.
    /// </summary>
    public Guid? DocumentId { get; set; }
    /// <summary>Whether a chunk's preamble is on the document yet: the first chunk placed sets it whole, later ones add missing macros.</summary>
    public bool PreambleApplied { get; set; }
    /// <summary>
    /// True for a file split since staging (21 Nov 2026): its chunks are staged, and its document is made at promotion.
    /// False for a file split before (its document made at the split, its chunks placed into it as they were read): it
    /// finishes the old way, so nothing in flight breaks at the deploy.
    /// </summary>
    public bool UsesStaging { get; set; }
    /// <summary>When the staged blocks became the document (promotion); null until then. After it, a retried chunk goes into the document.</summary>
    public DateTime? PromotedAt { get; set; }
    /// <summary>
    /// The staged preamble: the LaTeX before <c>\begin{document}</c> of the first chunk staged (class, packages, macros,
    /// page setup), applied to the document at promotion as a single import's.
    /// </summary>
    public string? StagedPreamble { get; set; }
    /// <summary>The macros later chunks define that the staged preamble lacks, added to the document's preamble at promotion.</summary>
    public string? StagedMacros { get; set; }
    public string? Error { get; set; }
    /// <summary>The import's warnings (each chunk's LaTeX import diagnostics, and its pages the reader was unsure of), summed.</summary>
    public int Warnings { get; set; }

    // ── measurement (21 Nov 2026, the owner: "how long it takes to actually compile, import and convert… timed
    // exactly"). Milliseconds, null until the stage has run. "From the upload" counts from the request's start, so
    // FirstPageMs and TotalMs include UploadMs. ─────────────────────────────────────────────────────────────────

    /// <summary>The PDF received → kept in storage, its row written (hash, page count, plan check included).</summary>
    public int? UploadMs { get; set; }
    /// <summary>The PDF's size in bytes.</summary>
    public long? SizeBytes { get; set; }
    /// <summary>Split into chunks (and, for a file split before staging, its document made).</summary>
    public int? SplitMs { get; set; }
    /// <summary>From the upload to the first chunk staged (the author sees a first page in the preview); placed, for a file split before staging.</summary>
    public int? FirstPageMs { get; set; }
    /// <summary>From the upload to the file finished (done, partial or failed), the first time it finished.</summary>
    public int? TotalMs { get; set; }
    /// <summary>The staged blocks and assets made into the document, in one transaction (promotion, 21 Nov 2026).</summary>
    public int? PromoteMs { get; set; }
    /// <summary>The document compiled once in the background when the file finished (the normal compile path).</summary>
    public int? CompileMs { get; set; }
    /// <summary>Whether that compile made a PDF; null until it ran.</summary>
    public bool? CompileOk { get; set; }
    /// <summary>The engine the compile used (typst, pdflatex, lualatex, xelatex).</summary>
    public string? CompileEngine { get; set; }
    /// <summary>The compile error's first line, when it failed.</summary>
    public string? CompileError { get; set; }

    /// <summary>
    /// How sure the reader (Mathpix) was of the file's pages, 0–1: the mean of its pages' precision (each page counts
    /// once), from each chunk's <see cref="BulkImportChunk.PrecisionPages"/>. Null when no page has a score.
    /// </summary>
    public double? Precision { get; set; }
    /// <summary>The file's pages under <c>BulkImport:LowConfidence</c> (0.80): each is a warning to check against the scan.</summary>
    public int LowPrecisionPages { get; set; }

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
    /// queued (waiting its turn), sent (on the ocr queue), reading (at Mathpix), read (waiting to be placed), done (staged,
    /// or placed in the document), failed, cancelled (<see cref="BulkImportStatuses"/>).
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
    /// <summary>
    /// The placeholder block on the document that the chunk's blocks replace: each chunk's from the split for a file split
    /// before staging; a failed chunk's from the promotion for a staged file (a retry replaces it in the document).
    /// </summary>
    public Guid? PlaceholderBlockId { get; set; }
    /// <summary>The chunk's import review session (its LaTeX import's record and diagnostics); linked to the document at promotion.</summary>
    public Guid? SessionId { get; set; }
    /// <summary>When the chunk was last claimed for reading: a read stuck past the stale limit is sent again.</summary>
    public DateTime? StartedAt { get; set; }

    // ── measurement (21 Nov 2026), milliseconds, null until the stage has run ──────────────────────────────────

    /// <summary>Made (at the split) → its reading started: the queue and the author's fairness hold.</summary>
    public int? QueueWaitMs { get; set; }
    /// <summary>Sent to Mathpix → Mathpix said <c>completed</c>; null for a cache hit (no call).</summary>
    public int? MathpixMs { get; set; }
    /// <summary>Mathpix's tex.zip conversion awaited and downloaded.</summary>
    public int? TexZipMs { get; set; }
    /// <summary>Put into the document, whole: the LaTeX import, its cleanup, its figures and the insert.</summary>
    public int? PlaceMs { get; set; }
    /// <summary>Of <see cref="PlaceMs"/>: the LaTeX read and staged for review (parse and cleanup).</summary>
    public int? PlaceParseMs { get; set; }
    /// <summary>Of <see cref="PlaceMs"/>: the figures stored as the document's assets.</summary>
    public int? PlaceFiguresMs { get; set; }
    /// <summary>Of <see cref="PlaceMs"/>: the blocks written into the document (figures not included).</summary>
    public int? PlaceWriteMs { get; set; }

    /// <summary>The chunk's pages' mean precision, 0–1 (see <see cref="PrecisionPages"/>); null when none has a score.</summary>
    public double? Precision { get; set; }
    /// <summary>
    /// Per page, as JSON: <c>[{"page":37,"score":0.62,"min":0.31,"lines":40,"chars":1830,"low":true}]</c>, the file's page
    /// numbers. From Mathpix's lines.json; null when it couldn't be read (never a reason to fail the chunk).
    /// </summary>
    public string? PrecisionPages { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int PageCount => LastPage - FirstPage + 1;

    public virtual BulkImportFile? File { get; set; }
}

/// <summary>
/// One block of a chunk's reading, staged until its file is promoted (21 Nov 2026): the import writes here, never into
/// the author's document while it runs. Its id becomes the block's id at promotion; its content is a block's, a figure's
/// <c>src</c> pointing at its <see cref="BulkImportStagedAsset"/> until then.
/// </summary>
public class BulkImportStagedBlock
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }
    /// <summary>The chunk it came from (<see cref="BulkImportChunk.Index"/>): blocks are promoted in page order.</summary>
    public int ChunkIndex { get; set; }
    /// <summary>Its order within the chunk, from 0.</summary>
    public int Order { get; set; }
    public string Type { get; set; } = string.Empty;
    /// <summary>The block's content, as a block's (jsonb).</summary>
    public System.Text.Json.JsonDocument Content { get; set; } = System.Text.Json.JsonDocument.Parse("{}");
    public int Depth { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual BulkImportFile? File { get; set; }
}

/// <summary>
/// A file of a chunk's reading (a figure's image), stored under <c>bulk-staging/{fileId}/</c> until promotion, when it
/// is moved to the document's assets (its id becomes the asset's). Served to the author for the preview by
/// <c>GET /api/bulk-imports/{batchId}/files/{fileId}/assets/{name}</c>.
/// </summary>
public class BulkImportStagedAsset
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }
    public int ChunkIndex { get; set; }
    /// <summary>Its name in the staging URLs: the id and the extension (<c>{id:N}.png</c>).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Its path in the reading's zip (<c>images/fig1.png</c>).</summary>
    public string OriginalPath { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

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
    /// <summary>The reading's precision per page (<see cref="BulkImportChunk.PrecisionPages"/>), so a cache hit has it too.</summary>
    public string? PrecisionPages { get; set; }
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
