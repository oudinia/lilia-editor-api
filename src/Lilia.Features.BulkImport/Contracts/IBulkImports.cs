using Lilia.Api.Features.BulkImport.Contracts;

namespace Lilia.Api.Features.BulkImport.Services;

/// <summary>Why a file was not added to a batch (the controller's and the connector's answer).</summary>
public enum AddFileRefusal
{
    None,
    BatchNotFound,
    BatchClosed,
    TooManyFiles,
    TooLarge,
    NotPdf,
    PagesUnknown,
}

public sealed record AddFileResult(AddFileRefusal Refusal, BulkImportFileDto? File = null, string? Message = null);

/// <summary>
/// Bulk import as the rest of the API uses it (22 Nov 2026: the connector's start_bulk_import and bulk_import_status):
/// the same batches and files as the web's /import/bulk, without reaching into the slice.
/// </summary>
public interface IBulkImports
{
    Task<Guid> CreateBatchAsync(string userId, CancellationToken ct = default);

    /// <summary>One PDF into the author's batch; throws a QuotaExceededException when the plan doesn't cover its pages.</summary>
    Task<AddFileResult> AddFileAsync(string userId, Guid batchId, Stream content, string fileName, CancellationToken ct = default);

    Task<IReadOnlyList<BulkImportBatchSummaryDto>> ListAsync(string userId, CancellationToken ct = default);

    Task<BulkImportBatchDto?> GetAsync(string userId, Guid batchId, CancellationToken ct = default);
}
