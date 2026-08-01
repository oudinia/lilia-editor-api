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
}
