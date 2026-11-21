namespace Lilia.Api.Features.BulkImport.Services;

/// <summary>
/// One structured log line per stage of a bulk import (21 Nov 2026, the owner: "timed exactly, through logs or a
/// process"). Every line starts <c>[BulkImportTiming]</c> then the stage, and carries its numbers as Serilog properties
/// (<c>Stage</c>, <c>Ms</c>, <c>FileId</c>, <c>ChunkId</c>, …), so <c>grep "\[BulkImportTiming\] mathpix"</c> on the API log
/// gives every Mathpix reading. The same numbers are kept on the rows (<c>bulk_import_files</c>, <c>bulk_import_chunks</c>).
/// Stages: upload, split, queue-wait, cache-hit, mathpix, tex-zip, lines, place (a chunk staged, or placed), first-page
/// (the first chunk staged), promote (the staging made into the document), total, compile.
/// </summary>
public static class BulkImportTiming
{
    private const string Prefix = "[BulkImportTiming] {Stage} {Ms} ms";

    public static void Upload(ILogger logger, int ms, Guid fileId, long bytes, int pages) =>
        logger.LogInformation(Prefix + " · file {FileId} · {Bytes} bytes · {Pages} pages", "upload", ms, fileId, bytes, pages);

    public static void Split(ILogger logger, int ms, Guid fileId, int pages, int chunks) =>
        logger.LogInformation(Prefix + " · file {FileId} · {Pages} pages · {Chunks} chunks", "split", ms, fileId, pages, chunks);

    public static void QueueWait(ILogger logger, int ms, Guid fileId, Guid chunkId, int chunkIndex) =>
        logger.LogInformation(Prefix + " · file {FileId} · chunk {ChunkIndex} {ChunkId}", "queue-wait", ms, fileId, chunkIndex, chunkId);

    public static void CacheHit(ILogger logger, Guid fileId, Guid chunkId, int chunkIndex, int pages) =>
        logger.LogInformation(Prefix + " · file {FileId} · chunk {ChunkIndex} {ChunkId} · {Pages} pages · no Mathpix time",
            "cache-hit", 0, fileId, chunkIndex, chunkId, pages);

    public static void Mathpix(ILogger logger, int ms, Guid fileId, Guid chunkId, int chunkIndex, int pages) =>
        logger.LogInformation(Prefix + " · file {FileId} · chunk {ChunkIndex} {ChunkId} · {Pages} pages · {MsPerPage} ms a page",
            "mathpix", ms, fileId, chunkIndex, chunkId, pages, pages > 0 ? ms / pages : ms);

    public static void TexZip(ILogger logger, int ms, Guid fileId, Guid chunkId, int chunkIndex, int bytes) =>
        logger.LogInformation(Prefix + " · file {FileId} · chunk {ChunkIndex} {ChunkId} · {Bytes} bytes", "tex-zip", ms, fileId, chunkIndex, chunkId, bytes);

    public static void Lines(ILogger logger, int ms, Guid fileId, Guid chunkId, int chunkIndex, double? precision, int lowPages) =>
        logger.LogInformation(Prefix + " · file {FileId} · chunk {ChunkIndex} {ChunkId} · precision {Precision} · {LowPages} low pages",
            "lines", ms, fileId, chunkIndex, chunkId, precision, lowPages);

    public static void Place(ILogger logger, int ms, Guid fileId, Guid chunkId, int chunkIndex, int? parseMs, int? figuresMs, int? writeMs, int blocks) =>
        logger.LogInformation(Prefix + " · file {FileId} · chunk {ChunkIndex} {ChunkId} · parse {ParseMs} ms · figures {FiguresMs} ms · write {WriteMs} ms · {Blocks} blocks",
            "place", ms, fileId, chunkIndex, chunkId, parseMs, figuresMs, writeMs, blocks);

    public static void Promote(ILogger logger, int ms, Guid fileId, Guid documentId, int blocks, int assets) =>
        logger.LogInformation(Prefix + " · file {FileId} · document {DocumentId} · {Blocks} blocks · {Assets} assets",
            "promote", ms, fileId, documentId, blocks, assets);

    public static void FirstPage(ILogger logger, int ms, Guid fileId) =>
        logger.LogInformation(Prefix + " · file {FileId}", "first-page", ms, fileId);

    public static void Total(ILogger logger, int ms, Guid fileId, string status, int pagesPlaced, double? pagesPerMinute) =>
        logger.LogInformation(Prefix + " · file {FileId} · {Status} · {PagesPlaced} pages placed · {PagesPerMinute} pages a minute",
            "total", ms, fileId, status, pagesPlaced, pagesPerMinute);

    public static void Compile(ILogger logger, int ms, Guid fileId, Guid documentId, bool ok, string? engine, string? error) =>
        logger.LogInformation(Prefix + " · file {FileId} · document {DocumentId} · ok {Ok} · engine {Engine} · {CompileError}",
            "compile", ms, fileId, documentId, ok, engine ?? "-", error ?? "");

    /// <summary>Milliseconds since <paramref name="from"/> (UTC), 0 at least.</summary>
    public static int Since(DateTime from) => (int)Math.Clamp((DateTime.UtcNow - from).TotalMilliseconds, 0, int.MaxValue);

    /// <summary>Milliseconds since a <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>.</summary>
    public static int Since(long timestamp) =>
        (int)Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds, 0, int.MaxValue);
}
