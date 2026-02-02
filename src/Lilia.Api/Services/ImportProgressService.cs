using Lilia.Api.Hubs;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.SignalR;

namespace Lilia.Api.Services;

/// <summary>
/// Service for emitting import progress events via SignalR.
/// </summary>
public interface IImportProgressService
{
    /// <summary>
    /// Send progress update to all clients subscribed to this job.
    /// </summary>
    Task SendProgressAsync(ImportProgressDto progress);

    /// <summary>
    /// Send completion notification to all clients subscribed to this job.
    /// </summary>
    Task SendCompletedAsync(ImportCompletedDto completed);

    /// <summary>
    /// Create a progress tracker for a specific job.
    /// </summary>
    ImportProgressTracker CreateTracker(string jobId, long? fileSizeBytes = null);
}

public class ImportProgressService : IImportProgressService
{
    private readonly IHubContext<ImportHub> _hubContext;
    private readonly ILogger<ImportProgressService> _logger;

    public ImportProgressService(
        IHubContext<ImportHub> hubContext,
        ILogger<ImportProgressService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendProgressAsync(ImportProgressDto progress)
    {
        var groupName = $"import-{progress.JobId}";
        await _hubContext.Clients.Group(groupName).SendAsync("ImportProgress", progress);
        _logger.LogDebug("Sent progress for job {JobId}: {Phase} {Progress}%",
            progress.JobId, progress.Phase, progress.Progress);
    }

    public async Task SendCompletedAsync(ImportCompletedDto completed)
    {
        var groupName = $"import-{completed.JobId}";
        await _hubContext.Clients.Group(groupName).SendAsync("ImportCompleted", completed);
        _logger.LogInformation("Import {JobId} completed: Success={Success}, Blocks={Blocks}",
            completed.JobId, completed.Success, completed.TotalBlocks);
    }

    public ImportProgressTracker CreateTracker(string jobId, long? fileSizeBytes = null)
    {
        return new ImportProgressTracker(this, jobId, fileSizeBytes);
    }
}

/// <summary>
/// Helper class to track and emit progress during import.
/// </summary>
public class ImportProgressTracker
{
    private readonly IImportProgressService _progressService;
    private readonly string _jobId;
    private readonly long? _fileSizeBytes;
    private readonly DateTime _startTime;
    private readonly Dictionary<string, int> _blockCounts = new();
    private readonly List<string> _sections = new();
    private readonly List<ImportWarningDto> _warnings = new();

    private int _totalPages;
    private int _currentPage;
    private int _totalBlocks;
    private int _processedBlocks;
    private int _imageCount;
    private int _tableCount;
    private int _equationCount;
    private string? _currentSection;
    private ImportPhase _currentPhase = ImportPhase.Receiving;

    public ImportProgressTracker(
        IImportProgressService progressService,
        string jobId,
        long? fileSizeBytes)
    {
        _progressService = progressService;
        _jobId = jobId;
        _fileSizeBytes = fileSizeBytes;
        _startTime = DateTime.UtcNow;
    }

    public string JobId => _jobId;
    public TimeSpan Elapsed => DateTime.UtcNow - _startTime;

    /// <summary>
    /// Set total pages discovered in document.
    /// </summary>
    public void SetTotalPages(int pages)
    {
        _totalPages = pages;
    }

    /// <summary>
    /// Set total blocks to process.
    /// </summary>
    public void SetTotalBlocks(int blocks)
    {
        _totalBlocks = blocks;
    }

    /// <summary>
    /// Add a section/chapter found in the document.
    /// </summary>
    public void AddSection(string section)
    {
        _sections.Add(section);
    }

    /// <summary>
    /// Increment block type count.
    /// </summary>
    public void IncrementBlockCount(string blockType)
    {
        _blockCounts.TryGetValue(blockType, out var count);
        _blockCounts[blockType] = count + 1;

        // Track special counts
        switch (blockType.ToLowerInvariant())
        {
            case "figure":
            case "image":
                _imageCount++;
                break;
            case "table":
                _tableCount++;
                break;
            case "equation":
                _equationCount++;
                break;
        }
    }

    /// <summary>
    /// Add a warning encountered during processing.
    /// </summary>
    public void AddWarning(string code, string message, string? blockType = null, int? pageNumber = null)
    {
        _warnings.Add(new ImportWarningDto
        {
            Code = code,
            Message = message,
            BlockType = blockType,
            PageNumber = pageNumber
        });
    }

    /// <summary>
    /// Report progress for receiving phase.
    /// </summary>
    public Task ReportReceivingAsync(string message = "Receiving file...")
    {
        _currentPhase = ImportPhase.Receiving;
        return SendProgressAsync(5, message);
    }

    /// <summary>
    /// Report progress for parsing phase.
    /// </summary>
    public Task ReportParsingAsync(string message = "Parsing document structure...")
    {
        _currentPhase = ImportPhase.Parsing;
        return SendProgressAsync(10, message);
    }

    /// <summary>
    /// Report progress for page extraction.
    /// </summary>
    public Task ReportExtractingPagesAsync(int currentPage, string? activity = null)
    {
        _currentPhase = ImportPhase.ExtractingPages;
        _currentPage = currentPage;

        var progress = _totalPages > 0
            ? 15 + (int)(25 * ((double)currentPage / _totalPages))
            : 20;

        var message = _totalPages > 0
            ? $"Extracting page {currentPage} of {_totalPages}..."
            : $"Extracting page {currentPage}...";

        return SendProgressAsync(progress, message, activity);
    }

    /// <summary>
    /// Report progress for image processing.
    /// </summary>
    public Task ReportProcessingImagesAsync(int current, int total, string? imageName = null)
    {
        _currentPhase = ImportPhase.ProcessingImages;
        var progress = total > 0
            ? 40 + (int)(10 * ((double)current / total))
            : 45;

        var message = $"Processing image {current} of {total}...";
        return SendProgressAsync(progress, message, imageName);
    }

    /// <summary>
    /// Report progress for table processing.
    /// </summary>
    public Task ReportProcessingTablesAsync(int current, int total)
    {
        _currentPhase = ImportPhase.ProcessingTables;
        var progress = total > 0
            ? 50 + (int)(10 * ((double)current / total))
            : 55;

        var message = $"Processing table {current} of {total}...";
        return SendProgressAsync(progress, message);
    }

    /// <summary>
    /// Report progress for equation conversion.
    /// </summary>
    public Task ReportConvertingEquationsAsync(int current, int total)
    {
        _currentPhase = ImportPhase.ConvertingEquations;
        var progress = total > 0
            ? 60 + (int)(10 * ((double)current / total))
            : 65;

        var message = $"Converting equation {current} of {total}...";
        return SendProgressAsync(progress, message);
    }

    /// <summary>
    /// Report progress for block conversion.
    /// </summary>
    public Task ReportConvertingBlocksAsync(int current, int total, string? blockType = null)
    {
        _currentPhase = ImportPhase.ConvertingBlocks;
        _processedBlocks = current;

        var progress = total > 0
            ? 70 + (int)(15 * ((double)current / total))
            : 75;

        var message = $"Converting block {current} of {total}...";
        var activity = blockType != null ? $"Processing {blockType} block" : null;
        return SendProgressAsync(progress, message, activity);
    }

    /// <summary>
    /// Report entering a new section.
    /// </summary>
    public Task ReportSectionAsync(string sectionTitle)
    {
        _currentSection = sectionTitle;
        if (!_sections.Contains(sectionTitle))
        {
            _sections.Add(sectionTitle);
        }
        return SendProgressAsync(_currentPhase switch
        {
            ImportPhase.ExtractingPages => 30,
            ImportPhase.ConvertingBlocks => 75,
            _ => 50
        }, $"Processing: {sectionTitle}");
    }

    /// <summary>
    /// Report validation phase.
    /// </summary>
    public Task ReportValidatingAsync(string message = "Validating content...")
    {
        _currentPhase = ImportPhase.Validating;
        return SendProgressAsync(88, message);
    }

    /// <summary>
    /// Report saving phase.
    /// </summary>
    public Task ReportSavingAsync(int current, int total)
    {
        _currentPhase = ImportPhase.Saving;
        var progress = total > 0
            ? 90 + (int)(8 * ((double)current / total))
            : 95;

        var message = $"Saving block {current} of {total}...";
        return SendProgressAsync(progress, message);
    }

    /// <summary>
    /// Report successful completion.
    /// </summary>
    public async Task ReportCompletedAsync(string? documentId = null, string? documentTitle = null)
    {
        _currentPhase = ImportPhase.Completed;

        // Send final progress
        await SendProgressAsync(100, "Import completed successfully!");

        // Send completion event
        await _progressService.SendCompletedAsync(new ImportCompletedDto
        {
            JobId = _jobId,
            Success = true,
            DocumentId = documentId,
            DocumentTitle = documentTitle,
            TotalBlocks = _processedBlocks > 0 ? _processedBlocks : _totalBlocks,
            TotalPages = _totalPages,
            BlockCounts = _blockCounts.Count > 0 ? new Dictionary<string, int>(_blockCounts) : null,
            Sections = _sections.Count > 0 ? new List<string>(_sections) : null,
            TotalDuration = Elapsed,
            Warnings = _warnings.Count > 0 ? new List<ImportWarningDto>(_warnings) : null
        });
    }

    /// <summary>
    /// Report failure.
    /// </summary>
    public async Task ReportFailedAsync(string errorMessage)
    {
        _currentPhase = ImportPhase.Failed;

        // Send failure progress
        await SendProgressAsync(0, $"Import failed: {errorMessage}");

        // Send completion event with failure
        await _progressService.SendCompletedAsync(new ImportCompletedDto
        {
            JobId = _jobId,
            Success = false,
            ErrorMessage = errorMessage,
            TotalBlocks = _processedBlocks,
            TotalPages = _totalPages,
            TotalDuration = Elapsed,
            Warnings = _warnings.Count > 0 ? new List<ImportWarningDto>(_warnings) : null
        });
    }

    private Task SendProgressAsync(int progress, string message, string? activity = null)
    {
        return _progressService.SendProgressAsync(new ImportProgressDto
        {
            JobId = _jobId,
            Phase = _currentPhase,
            Progress = Math.Min(100, Math.Max(0, progress)),
            Message = message,
            CurrentActivity = activity,
            TotalPages = _totalPages > 0 ? _totalPages : null,
            CurrentPage = _currentPage > 0 ? _currentPage : null,
            TotalBlocks = _totalBlocks > 0 ? _totalBlocks : null,
            ProcessedBlocks = _processedBlocks > 0 ? _processedBlocks : null,
            BlockCounts = _blockCounts.Count > 0 ? new Dictionary<string, int>(_blockCounts) : null,
            Sections = _sections.Count > 0 ? new List<string>(_sections) : null,
            CurrentSection = _currentSection,
            Warnings = _warnings.Count > 0 ? new List<ImportWarningDto>(_warnings) : null,
            WarningCount = _warnings.Count,
            Elapsed = Elapsed,
            FileSizeBytes = _fileSizeBytes,
            ImageCount = _imageCount > 0 ? _imageCount : null,
            TableCount = _tableCount > 0 ? _tableCount : null,
            EquationCount = _equationCount > 0 ? _equationCount : null
        });
    }
}
