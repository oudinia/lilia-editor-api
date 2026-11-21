namespace Lilia.Api.Features.BulkImport.Contracts;

// The slice's messages (Wolverine): to RabbitMQ when BulkImport:RabbitMq is set, else to durable local queues of the
// same names (BulkImportMessaging). Each names a row; the row says what to do, so a message delivered twice is harmless.

/// <summary>Split a file into chunks and make its document (queue <c>bulk-import.split</c>, 2 at once).</summary>
public sealed record SplitBulkImportFile(Guid FileId);

/// <summary>Read one chunk through Mathpix (queue <c>bulk-import.ocr</c>, <c>BulkImport:MaxParallelChunks</c> at once).</summary>
public sealed record ReadBulkImportChunk(Guid ChunkId);

/// <summary>Put a read chunk's blocks into its document, in place of its placeholder (queue <c>bulk-import.place</c>, one at a time).</summary>
public sealed record PlaceBulkImportChunk(Guid ChunkId);

/// <summary>
/// Compile a finished file's document once, to time it (queue <c>bulk-import.compile</c>, a durable local queue, one at a
/// time; 21 Nov 2026). Never holds the import up: the file is finished before it is sent.
/// </summary>
public sealed record CompileBulkImportFile(Guid FileId);
