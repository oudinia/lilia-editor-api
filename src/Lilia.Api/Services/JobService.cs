using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Import.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class JobService : IJobService
{
    private readonly LiliaDbContext _context;
    private readonly IDocumentService _documentService;
    private readonly IRenderService _renderService;
    private readonly IDocxImportService _docxImportService;
    private readonly IImportProgressService _progressService;
    private readonly IImportReviewService _reviewService;
    private readonly ILogger<JobService> _logger;

    public JobService(
        LiliaDbContext context,
        IDocumentService documentService,
        IRenderService renderService,
        IDocxImportService docxImportService,
        IImportProgressService progressService,
        IImportReviewService reviewService,
        ILogger<JobService> logger)
    {
        _context = context;
        _documentService = documentService;
        _renderService = renderService;
        _docxImportService = docxImportService;
        _progressService = progressService;
        _reviewService = reviewService;
        _logger = logger;
    }

    public async Task<List<JobListDto>> GetJobsAsync(string userId, string? status = null, string? jobType = null, int limit = 50, int offset = 0)
    {
        var query = _context.Jobs
            .Include(j => j.Document)
            .Where(j => j.UserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(j => j.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(jobType))
        {
            query = query.Where(j => j.JobType == jobType);
        }

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return jobs.Select(j => new JobListDto(
            j.Id,
            j.DocumentId,
            j.Document?.Title,
            j.JobType,
            j.Status,
            j.Progress,
            j.SourceFormat,
            j.TargetFormat,
            j.SourceFileName,
            j.ErrorMessage,
            j.CreatedAt,
            j.CompletedAt
        )).ToList();
    }

    public async Task<JobDto?> GetJobAsync(Guid jobId, string userId)
    {
        var job = await _context.Jobs
            .Include(j => j.Document)
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId);

        if (job == null) return null;

        return MapToDto(job);
    }

    public async Task<JobDto> CreateExportJobAsync(string userId, CreateExportJobDto dto)
    {
        // Verify document access
        var document = await _documentService.GetDocumentAsync(dto.DocumentId, userId);
        if (document == null)
        {
            throw new ArgumentException("Document not found");
        }

        var job = new Job
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DocumentId = dto.DocumentId,
            JobType = JobTypes.Export,
            Status = JobStatus.Processing,
            Progress = 0,
            SourceFormat = "lilia",
            TargetFormat = dto.Format.ToLowerInvariant(),
            SourceFileName = document.Title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        try
        {
            // Process export
            string content;
            string extension;

            switch (dto.Format.ToUpperInvariant())
            {
                case "LATEX":
                    content = await _renderService.RenderToLatexAsync(dto.DocumentId);
                    extension = "tex";
                    break;

                case "HTML":
                    content = await _renderService.RenderToHtmlAsync(dto.DocumentId);
                    extension = "html";
                    break;

                case "MARKDOWN":
                    content = await RenderToMarkdownAsync(dto.DocumentId);
                    extension = "md";
                    break;

                case "LML":
                    content = await RenderToLmlAsync(dto.DocumentId, userId);
                    extension = "lilia";
                    break;

                default:
                    throw new ArgumentException($"Unsupported format: {dto.Format}");
            }

            // Store result (for now, we'll store in metadata since we don't have file storage for exports)
            var safeTitle = SanitizeFilename(document.Title ?? "document");
            job.ResultFileName = $"{safeTitle}.{extension}";
            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.CompletedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;
            job.Metadata = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(new { content })
            );

            await _context.SaveChangesAsync();

            _logger.LogInformation("[Export] Job {JobId} completed for document {DocumentId}", job.Id, dto.DocumentId);
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogError(ex, "[Export] Job {JobId} failed for document {DocumentId}", job.Id, dto.DocumentId);
        }

        return MapToDto(job);
    }

    public async Task<JobDto> CreateImportJobAsync(string userId, CreateImportJobDto dto, Stream fileStream)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            JobType = JobTypes.Import,
            Status = JobStatus.Pending,
            Progress = 0,
            SourceFormat = dto.Format.ToLowerInvariant(),
            TargetFormat = "lilia",
            SourceFileName = dto.FileName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        // TODO: Implement actual import processing
        // For now, mark as pending - import would be processed by a background worker
        _logger.LogInformation("[Import] Job {JobId} created for file {FileName}", job.Id, dto.FileName);

        return MapToDto(job);
    }

    public async Task<ImportResultDto> CreateImportJobFromBase64Async(string userId, ImportRequestDto request)
    {
        // Calculate file size for progress tracking
        var fileSizeBytes = request.Content.Length * 3 / 4; // Approximate decoded size from base64

        var job = new Job
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            JobType = JobTypes.Import,
            Status = JobStatus.Processing,
            Progress = 10,
            SourceFormat = request.Format.ToLowerInvariant(),
            TargetFormat = "lilia",
            SourceFileName = request.Filename,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        // Create progress tracker for SignalR updates
        var tracker = _progressService.CreateTracker(job.Id.ToString(), fileSizeBytes);

        try
        {
            Lilia.Core.Entities.Document? createdDocument = null;

            Guid? reviewSessionId = null;

            if (request.Format.ToUpperInvariant() == "DOCX")
            {
                // Report receiving phase
                await tracker.ReportReceivingAsync($"Receiving {request.Filename}...");

                // Decode base64 and save to temp file
                var bytes = Convert.FromBase64String(request.Content);
                var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.docx");

                try
                {
                    await File.WriteAllBytesAsync(tempPath, bytes);

                    // Report parsing phase
                    await tracker.ReportParsingAsync("Parsing DOCX structure...");
                    job.Progress = 30;
                    await _context.SaveChangesAsync();

                    // Map DTO options to ImportOptions model
                    Lilia.Import.Models.ImportOptions? importOptions = null;
                    if (request.Options != null)
                    {
                        importOptions = new Lilia.Import.Models.ImportOptions
                        {
                            PreserveFormatting = request.Options.PreserveFormatting,
                            ExtractImages = request.Options.ImportImages,
                            ConvertEquationsToLatex = request.Options.AutoDetectEquations,
                            DetectHeadingsByFormatting = request.Options.SplitByHeadings,
                            AIOptions = new Lilia.Import.Models.AIImportOptions
                            {
                                BibliographyParsing = request.Options.ImportBibliography
                            }
                        };
                    }

                    // Use DOCX import service
                    var importResult = await _docxImportService.ImportAsync(tempPath, importOptions);

                    if (importResult.Document == null)
                    {
                        throw new InvalidOperationException("Import failed - no document created");
                    }

                    // Report extraction completed
                    var elements = importResult.IntermediateDocument?.Elements ?? [];
                    tracker.SetTotalBlocks(elements.Count);
                    await tracker.ReportExtractingPagesAsync(1, "Extraction complete");

                    job.Progress = 60;
                    await _context.SaveChangesAsync();

                    var title = !string.IsNullOrWhiteSpace(request.Title)
                        ? request.Title
                        : Path.GetFileNameWithoutExtension(request.Filename);

                    // Build review blocks from intermediate elements
                    var reviewBlocks = new List<CreateReviewBlockDto>();
                    var sortOrder = 0;
                    var currentListItems = new List<Lilia.Import.Models.ImportListItem>();
                    var currentListIsOrdered = false;
                    var currentAbstractTexts = new List<string>();
                    var processedCount = 0;
                    var totalElements = elements.Count;

                    foreach (var element in elements)
                    {
                        // Flush accumulated abstract paragraphs when we hit a non-abstract element
                        if (element is not Lilia.Import.Models.ImportAbstract && currentAbstractTexts.Any())
                        {
                            reviewBlocks.Add(CreateMergedAbstractBlock(currentAbstractTexts, sortOrder++));
                            tracker.IncrementBlockCount("abstract");
                            currentAbstractTexts.Clear();
                        }

                        // Flush accumulated list items when we hit a non-list element
                        if (element is not Lilia.Import.Models.ImportListItem && currentListItems.Any())
                        {
                            reviewBlocks.Add(CreateReviewListBlock(currentListItems, currentListIsOrdered, sortOrder++));
                            currentListItems.Clear();
                        }

                        CreateReviewBlockDto? reviewBlock = element switch
                        {
                            Lilia.Import.Models.ImportHeading h => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "heading",
                                Content: JsonSerializer.SerializeToElement(new { text = h.Text, level = h.Level }),
                                Confidence: 90,
                                Warnings: null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            Lilia.Import.Models.ImportParagraph p => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "paragraph",
                                Content: JsonSerializer.SerializeToElement(new { text = p.Text }),
                                Confidence: 95,
                                Warnings: null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            Lilia.Import.Models.ImportEquation eq => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "equation",
                                Content: JsonSerializer.SerializeToElement(new
                                {
                                    latex = eq.LatexContent ?? eq.OmmlXml,
                                    displayMode = !eq.IsInline
                                }),
                                Confidence: eq.LatexContent != null ? 80 : 50,
                                Warnings: eq.LatexContent == null
                                    ? JsonSerializer.SerializeToElement(new[]
                                    {
                                        new { id = Guid.NewGuid().ToString(), type = "EquationConversionFailed", message = "Equation could not be fully converted to LaTeX", severity = "warning" }
                                    })
                                    : null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            Lilia.Import.Models.ImportTable t => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "table",
                                Content: JsonSerializer.SerializeToElement(ConvertTableToEditorFormat(t)),
                                Confidence: 85,
                                Warnings: null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            Lilia.Import.Models.ImportImage img => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "figure",
                                Content: JsonSerializer.SerializeToElement(new
                                {
                                    src = img.Data.Length > 0 ? $"data:{img.MimeType};base64,{Convert.ToBase64String(img.Data)}" : "",
                                    caption = img.AltText ?? "",
                                    alt = img.AltText ?? ""
                                }),
                                Confidence: img.Data.Length > 0 ? 85 : 40,
                                Warnings: img.Data.Length == 0
                                    ? JsonSerializer.SerializeToElement(new[]
                                    {
                                        new { id = Guid.NewGuid().ToString(), type = "ImageExtractionFailed", message = "Image could not be extracted from document", severity = "warning" }
                                    })
                                    : null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            Lilia.Import.Models.ImportCodeBlock cb => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "code",
                                Content: JsonSerializer.SerializeToElement(new
                                {
                                    code = cb.Text,
                                    language = cb.Language ?? "plaintext"
                                }),
                                Confidence: 75,
                                Warnings: null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            Lilia.Import.Models.ImportAbstract => null, // Accumulated below
                            Lilia.Import.Models.ImportBlockquote bq => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "blockquote",
                                Content: JsonSerializer.SerializeToElement(new { text = bq.Text }),
                                Confidence: 80,
                                Warnings: null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            Lilia.Import.Models.ImportTheorem th => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "theorem",
                                Content: JsonSerializer.SerializeToElement(new
                                {
                                    text = th.Text,
                                    environmentType = th.EnvironmentType.ToString().ToLowerInvariant(),
                                    number = th.Number,
                                    title = th.Title
                                }),
                                Confidence: 80,
                                Warnings: null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            Lilia.Import.Models.ImportBibliographyEntry bib => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "bibliography",
                                Content: JsonSerializer.SerializeToElement(new
                                {
                                    text = bib.Text,
                                    referenceLabel = bib.ReferenceLabel
                                }),
                                Confidence: 75,
                                Warnings: null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            Lilia.Import.Models.ImportListItem => null, // Handle below
                            Lilia.Import.Models.ImportPageBreak => new CreateReviewBlockDto(
                                Id: Guid.NewGuid().ToString(),
                                Type: "pageBreak",
                                Content: JsonSerializer.SerializeToElement(new { }),
                                Confidence: 100,
                                Warnings: null,
                                SortOrder: sortOrder++,
                                Depth: 0
                            ),
                            _ => null
                        };

                        // Accumulate abstract paragraphs to create a single abstract block
                        if (element is Lilia.Import.Models.ImportAbstract abstractElement)
                        {
                            if (!string.IsNullOrWhiteSpace(abstractElement.Text))
                            {
                                currentAbstractTexts.Add(abstractElement.Text);
                            }
                        }
                        // Accumulate list items to create a single list block
                        else if (element is Lilia.Import.Models.ImportListItem listItem)
                        {
                            if (currentListItems.Any() && currentListIsOrdered != listItem.IsNumbered)
                            {
                                reviewBlocks.Add(CreateReviewListBlock(currentListItems, currentListIsOrdered, sortOrder++));
                                currentListItems.Clear();
                            }
                            currentListItems.Add(listItem);
                            currentListIsOrdered = listItem.IsNumbered;
                        }
                        else if (reviewBlock != null)
                        {
                            reviewBlocks.Add(reviewBlock);
                            var blockTypeName = element.GetType().Name.Replace("Import", "").ToLowerInvariant();
                            tracker.IncrementBlockCount(blockTypeName);
                        }

                        // Report progress every 10 elements
                        processedCount++;
                        if (processedCount % 10 == 0 || processedCount == totalElements)
                        {
                            var blockType = element.GetType().Name.Replace("Import", "");
                            await tracker.ReportConvertingBlocksAsync(processedCount, totalElements, blockType);
                        }
                    }

                    // Flush any remaining abstract paragraphs
                    if (currentAbstractTexts.Any())
                    {
                        reviewBlocks.Add(CreateMergedAbstractBlock(currentAbstractTexts, sortOrder++));
                        tracker.IncrementBlockCount("abstract");
                    }

                    // Flush any remaining list items
                    if (currentListItems.Any())
                    {
                        reviewBlocks.Add(CreateReviewListBlock(currentListItems, currentListIsOrdered, sortOrder++));
                        tracker.IncrementBlockCount("list");
                    }

                    // Report saving phase
                    await tracker.ReportSavingAsync(1, reviewBlocks.Count);

                    // Serialize paragraph traces from the intermediate document
                    JsonElement? tracesJson = null;
                    var traces = importResult.IntermediateDocument?.ParagraphTraces;
                    if (traces != null && traces.Count > 0)
                    {
                        tracesJson = JsonSerializer.SerializeToElement(traces);
                    }

                    // Move DOCX to persistent storage for re-testing
                    string? persistentPath = null;
                    try
                    {
                        var importsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uploads", "imports");
                        Directory.CreateDirectory(importsDir);
                        persistentPath = Path.Combine(importsDir, $"{job.Id}.docx");
                        File.Copy(tempPath, persistentPath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Import] Failed to save DOCX to persistent storage for job {JobId}", job.Id);
                    }

                    // Create review session instead of document + blocks
                    var sessionResult = await _reviewService.CreateSessionFromImportAsync(
                        userId, job.Id, title, reviewBlocks,
                        paragraphTraces: tracesJson,
                        sourceFilePath: persistentPath);

                    reviewSessionId = sessionResult.Session.Id;

                    await tracker.ReportSavingAsync(reviewBlocks.Count, reviewBlocks.Count);

                    _logger.LogInformation("[Import] Created review session {SessionId} with {BlockCount} blocks and {TraceCount} traces for job {JobId}",
                        reviewSessionId, reviewBlocks.Count, traces?.Count ?? 0, job.Id);

                    // Do NOT set job.DocumentId — that happens at finalize
                }
                finally
                {
                    // Clean up temp file (persistent copy already saved above)
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
            }
            else if (request.Format.ToUpperInvariant() == "LATEX" || request.Format.ToUpperInvariant() == "LML")
            {
                // For LaTeX and LML, content is plain text
                var title = !string.IsNullOrWhiteSpace(request.Title)
                    ? request.Title
                    : Path.GetFileNameWithoutExtension(request.Filename);

                createdDocument = new Lilia.Core.Entities.Document
                {
                    Id = Guid.NewGuid(),
                    OwnerId = userId,
                    Title = title,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Documents.Add(createdDocument);
                await _context.SaveChangesAsync();

                job.Progress = 50;
                job.DocumentId = createdDocument.Id;

                if (request.Format.ToUpperInvariant() == "LML")
                {
                    // Parse LML/Lilia format (JSON)
                    try
                    {
                        var lmlDoc = JsonSerializer.Deserialize<JsonElement>(request.Content);
                        if (lmlDoc.TryGetProperty("document", out var docElement) &&
                            docElement.TryGetProperty("blocks", out var blocksElement))
                        {
                            var sortOrder = 0;
                            foreach (var block in blocksElement.EnumerateArray())
                            {
                                var blockEntity = new Lilia.Core.Entities.Block
                                {
                                    Id = Guid.NewGuid(),
                                    DocumentId = createdDocument.Id,
                                    Type = block.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "paragraph" : "paragraph",
                                    Content = block.TryGetProperty("content", out var contentEl)
                                        ? JsonDocument.Parse(contentEl.GetRawText())
                                        : JsonDocument.Parse("{}"),
                                    SortOrder = sortOrder++,
                                    Depth = block.TryGetProperty("depth", out var depthEl) ? depthEl.GetInt32() : 0,
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow
                                };
                                _context.Blocks.Add(blockEntity);
                            }
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "[Import] Failed to parse LML content, creating as single block");
                        // Fall back to single paragraph block
                        _context.Blocks.Add(new Lilia.Core.Entities.Block
                        {
                            Id = Guid.NewGuid(),
                            DocumentId = createdDocument.Id,
                            Type = "paragraph",
                            Content = JsonDocument.Parse(JsonSerializer.Serialize(new { text = request.Content })),
                            SortOrder = 0,
                            Depth = 0,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                        await _context.SaveChangesAsync();
                    }
                }
                else
                {
                    // LaTeX: Create single code block with the content
                    _context.Blocks.Add(new Lilia.Core.Entities.Block
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = createdDocument.Id,
                        Type = "code",
                        Content = JsonDocument.Parse(JsonSerializer.Serialize(new { code = request.Content, language = "latex" })),
                        SortOrder = 0,
                        Depth = 0,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                throw new ArgumentException($"Unsupported import format: {request.Format}");
            }

            // Mark job as completed
            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.CompletedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Report completion via SignalR
            await tracker.ReportCompletedAsync(
                createdDocument?.Id.ToString(),
                createdDocument?.Title ?? request.Filename
            );

            _logger.LogInformation("[Import] Job {JobId} completed, created document {DocumentId}",
                job.Id, createdDocument?.Id);

            return new ImportResultDto(
                MapToDto(job),
                createdDocument != null
                    ? new ImportedDocumentInfoDto(createdDocument.Id, createdDocument.Title ?? "", createdDocument.CreatedAt)
                    : null,
                reviewSessionId
            );
        }
        catch (Exception ex)
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Report failure via SignalR
            await tracker.ReportFailedAsync(ex.Message);

            _logger.LogError(ex, "[Import] Job {JobId} failed", job.Id);
            throw;
        }
    }

    public async Task<JobDto?> RetryJobAsync(Guid jobId, string userId)
    {
        var job = await _context.Jobs.FindAsync(jobId);
        if (job == null || job.UserId != userId)
        {
            return null;
        }

        if (job.Status != JobStatus.Failed)
        {
            throw new InvalidOperationException("Only failed jobs can be retried");
        }

        // Reset job status
        job.Status = JobStatus.Pending;
        job.Progress = 0;
        job.ErrorMessage = null;
        job.UpdatedAt = DateTime.UtcNow;
        job.CompletedAt = null;

        await _context.SaveChangesAsync();

        // If it's an export job, process it immediately
        if (job.JobType == JobTypes.Export && job.DocumentId.HasValue)
        {
            return await CreateExportJobAsync(userId, new CreateExportJobDto(
                job.DocumentId.Value,
                job.TargetFormat ?? "latex"
            ));
        }

        return MapToDto(job);
    }

    public async Task<bool> CancelJobAsync(Guid jobId, string userId)
    {
        var job = await _context.Jobs.FindAsync(jobId);
        if (job == null || job.UserId != userId)
        {
            return false;
        }

        if (job.Status == JobStatus.Completed || job.Status == JobStatus.Cancelled)
        {
            return false;
        }

        job.Status = JobStatus.Cancelled;
        job.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<ExportResultDto?> GetExportResultAsync(Guid jobId, string userId)
    {
        var job = await _context.Jobs.FindAsync(jobId);
        if (job == null || job.UserId != userId)
        {
            return null;
        }

        if (job.Status != JobStatus.Completed || job.Metadata == null)
        {
            return null;
        }

        var content = job.Metadata.RootElement.GetProperty("content").GetString() ?? "";

        return new ExportResultDto(
            job.Id,
            job.Status,
            content,
            job.ResultFileName ?? "export.txt"
        );
    }

    private async Task<string> RenderToMarkdownAsync(Guid documentId)
    {
        var html = await _renderService.RenderToHtmlAsync(documentId);

        var markdown = html
            .Replace("<h1>", "# ").Replace("</h1>", "\n\n")
            .Replace("<h2>", "## ").Replace("</h2>", "\n\n")
            .Replace("<h3>", "### ").Replace("</h3>", "\n\n")
            .Replace("<h4>", "#### ").Replace("</h4>", "\n\n")
            .Replace("<p>", "").Replace("</p>", "\n\n")
            .Replace("<strong>", "**").Replace("</strong>", "**")
            .Replace("<em>", "_").Replace("</em>", "_")
            .Replace("<code>", "`").Replace("</code>", "`")
            .Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n")
            .Replace("&nbsp;", " ")
            .Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&amp;", "&");

        markdown = System.Text.RegularExpressions.Regex.Replace(markdown, "<[^>]+>", "");

        return markdown.Trim();
    }

    private async Task<string> RenderToLmlAsync(Guid documentId, string userId)
    {
        var document = await _documentService.GetDocumentAsync(documentId, userId);

        if (document == null)
        {
            return "{}";
        }

        var lml = new
        {
            version = "1.0",
            document = new
            {
                id = document.Id,
                title = document.Title,
                createdAt = document.CreatedAt,
                updatedAt = document.UpdatedAt,
                blocks = document.Blocks?.Select(b => new
                {
                    id = b.Id,
                    type = b.Type,
                    content = b.Content,
                    sortOrder = b.SortOrder,
                    depth = b.Depth
                })
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(lml, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }

    private static string SanitizeFilename(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("", filename.Where(c => !invalid.Contains(c)));

        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized.Trim();
    }

    private static JobDto MapToDto(Job j)
    {
        return new JobDto(
            j.Id,
            j.UserId,
            j.DocumentId,
            j.Document?.Title,
            j.JobType,
            j.Status,
            j.Progress,
            j.SourceFormat,
            j.TargetFormat,
            j.SourceFileName,
            j.ResultFileName,
            j.ResultUrl,
            j.ErrorMessage,
            j.CreatedAt,
            j.UpdatedAt,
            j.CompletedAt
        );
    }

    /// <summary>
    /// Convert ImportTable to editor-expected format { headers: string[], rows: string[][] }
    /// </summary>
    private static object ConvertTableToEditorFormat(Lilia.Import.Models.ImportTable table)
    {
        if (table.Rows.Count == 0)
        {
            return new
            {
                headers = new[] { "Column 1", "Column 2", "Column 3" },
                rows = new[] { new[] { "", "", "" }, new[] { "", "", "" } }
            };
        }

        // Extract text from each cell
        var allRows = table.Rows.Select(row => row.Select(cell => cell.Text).ToArray()).ToArray();

        if (table.HasHeaderRow && allRows.Length > 0)
        {
            // First row is headers
            var headers = allRows[0];
            var dataRows = allRows.Skip(1).ToArray();

            // Ensure at least one data row
            if (dataRows.Length == 0)
            {
                dataRows = new[] { headers.Select(_ => "").ToArray() };
            }

            return new { headers, rows = dataRows };
        }
        else
        {
            // Generate column headers
            var colCount = allRows[0].Length;
            var headers = Enumerable.Range(1, colCount).Select(i => $"Column {i}").ToArray();
            return new { headers, rows = allRows };
        }
    }

    /// <summary>
    /// Create a review list block DTO from accumulated list items
    /// </summary>
    private static CreateReviewBlockDto CreateReviewListBlock(
        List<Lilia.Import.Models.ImportListItem> items,
        bool ordered,
        int sortOrder)
    {
        var itemTexts = items.Select(li => li.Text).ToArray();
        return new CreateReviewBlockDto(
            Id: Guid.NewGuid().ToString(),
            Type: "list",
            Content: JsonSerializer.SerializeToElement(new { items = itemTexts, ordered }),
            Confidence: 90,
            Warnings: null,
            SortOrder: sortOrder,
            Depth: 0
        );
    }

    /// <summary>
    /// Create a review abstract block DTO from accumulated abstract paragraphs.
    /// Merges consecutive abstract paragraphs into a single block with combined text.
    /// </summary>
    private static CreateReviewBlockDto CreateMergedAbstractBlock(
        List<string> texts,
        int sortOrder)
    {
        var combinedText = string.Join("\n\n", texts);
        return new CreateReviewBlockDto(
            Id: Guid.NewGuid().ToString(),
            Type: "abstract",
            Content: JsonSerializer.SerializeToElement(new { text = combinedText }),
            Confidence: 85,
            Warnings: null,
            SortOrder: sortOrder,
            Depth: 0
        );
    }

    /// <summary>
    /// Create a list block from accumulated list items
    /// </summary>
    private static Lilia.Core.Entities.Block CreateListBlock(
        Guid documentId,
        List<Lilia.Import.Models.ImportListItem> items,
        bool ordered,
        int sortOrder)
    {
        var itemTexts = items.Select(li => li.Text).ToArray();

        return new Lilia.Core.Entities.Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "list",
            Content = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                items = itemTexts,
                ordered = ordered
            })),
            SortOrder = sortOrder,
            Depth = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
