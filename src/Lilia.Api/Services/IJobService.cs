using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IJobService
{
    Task<List<JobListDto>> GetJobsAsync(string userId, string? status = null, string? jobType = null, int limit = 50, int offset = 0);
    Task<JobDto?> GetJobAsync(Guid jobId, string userId);
    Task<JobDto> CreateExportJobAsync(string userId, CreateExportJobDto dto);

    /// <summary>
    /// Perform a queued export. Idempotent — refuses to start from a terminal
    /// job status, so a redelivery cannot double-write the result.
    /// </summary>
    Task RunExportJobAsync(Guid jobId, CancellationToken ct = default);
    Task<JobDto> CreateImportJobAsync(string userId, CreateImportJobDto dto, Stream fileStream);
    Task<ImportResultDto> CreateImportJobFromBase64Async(string userId, ImportRequestDto request);
    Task<JobDto?> RetryJobAsync(Guid jobId, string userId);
    Task<bool> CancelJobAsync(Guid jobId, string userId);
    Task<ExportResultDto?> GetExportResultAsync(Guid jobId, string userId);

    /// <summary>
    /// A chunk of a PDF already read as a LaTeX project (bulk import, 21 Nov 2026: Mathpix's tex.zip for a few pages),
    /// imported as a PDF's tex.zip is (its own import job, the LaTeX project import, the cleanup of Mathpix's reading),
    /// then put into <paramref name="documentId"/> in place of <paramref name="placeholderBlockId"/>. The plan and charges
    /// are the caller's.
    /// </summary>
    Task<(Guid SessionId, IReadOnlyList<Guid> BlockIds)> ImportPdfChunkIntoDocumentAsync(
        string userId, string fileName, Guid documentId, Guid? placeholderBlockId, bool applyPreamble,
        string latexSource, byte[] projectZip, string? sourcePdfKey, CancellationToken ct = default);

    /// <summary>Pages Mathpix read, charged as a PDF import's are: the plan's monthly PDF pages, the site's, and credits.</summary>
    Task ChargePdfPagesAsync(string userId, int pages);
}
