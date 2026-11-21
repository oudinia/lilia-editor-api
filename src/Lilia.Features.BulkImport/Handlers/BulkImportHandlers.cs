using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Features.BulkImport.Services;

namespace Lilia.Api.Features.BulkImport.Handlers;

// Wolverine handlers (discovered in this assembly, Program.cs). Thin: each step is BulkImportProcessor's, which tests
// run by hand. A step catches its own failures and records them on its row, so Wolverine never retries a step blindly.

public sealed class SplitBulkImportFileHandler
{
    public Task Handle(SplitBulkImportFile message, BulkImportProcessor processor, CancellationToken ct) =>
        processor.SplitAsync(message.FileId, ct);
}

public sealed class ReadBulkImportChunkHandler
{
    public Task Handle(ReadBulkImportChunk message, BulkImportProcessor processor, CancellationToken ct) =>
        processor.ReadAsync(message.ChunkId, ct);
}

public sealed class PlaceBulkImportChunkHandler
{
    public Task Handle(PlaceBulkImportChunk message, BulkImportProcessor processor, CancellationToken ct) =>
        processor.PlaceAsync(message.ChunkId, ct);
}

public sealed class CompileBulkImportFileHandler
{
    public Task Handle(CompileBulkImportFile message, BulkImportProcessor processor, CancellationToken ct) =>
        processor.CompileAsync(message.FileId, ct);
}
