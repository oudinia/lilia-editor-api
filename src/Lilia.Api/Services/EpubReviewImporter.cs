using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// An .epub on its way into Lilia: parsed by <see cref="IEpubService"/>
/// (the Clean Up parser), staged as an import job and an import review
/// session, so the author sees what Clean Up kept, merged and dropped before
/// any of it becomes a document.
///
/// <para>One import model (design, 27 Sep 2026): ePub goes through the job and
/// the review step like .tex and .docx. The document is created server-side,
/// by the review's finalize — the same INSERT…SELECT every import uses.</para>
///
/// <para>The parse runs in the request, as the DOCX path does: the book is
/// already in memory and the parser is a single pass over its XHTML. The job
/// row records the import (history, <c>GET /jobs/{id}</c>, the document it
/// made) the way it does for every other format.</para>
/// </summary>
public interface IEpubReviewImporter
{
    /// <summary>
    /// Stage <paramref name="bytes"/> for review. Throws
    /// <see cref="NotAnEpubException"/> when the file is not a book, before
    /// anything is written.
    /// </summary>
    Task<LatexImportUploadResponseDto> StageAsync(
        string userId, string fileName, byte[] bytes, bool autoFinalize, CancellationToken ct = default);
}

/// <summary>The upload is not an ePub the parser can read — a 400, not a failed job.</summary>
public class NotAnEpubException(string message) : Exception(message);

public class EpubReviewImporter : IEpubReviewImporter
{
    private readonly LiliaDbContext _context;
    private readonly IEpubService _epub;
    private readonly IImportReviewService _review;
    private readonly IStorageService _storage;
    private readonly ILogger<EpubReviewImporter> _logger;

    public EpubReviewImporter(
        LiliaDbContext context,
        IEpubService epub,
        IImportReviewService review,
        IStorageService storage,
        ILogger<EpubReviewImporter> logger)
    {
        _context = context;
        _epub = epub;
        _review = review;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>Zip local-file-header magic: every .epub is a zip.</summary>
    public static bool LooksLikeZip(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;

    public async Task<LatexImportUploadResponseDto> StageAsync(
        string userId, string fileName, byte[] bytes, bool autoFinalize, CancellationToken ct = default)
    {
        if (!LooksLikeZip(bytes))
            throw new NotAnEpubException($"\"{fileName}\" isn't an ePub (not a zip archive), so it can't be imported as one.");

        // Parse first: a file that is not a book never becomes a job.
        Core.Models.Epub.EpubMetadata metadata;
        List<Block> blocks;
        List<string> warnings;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            (metadata, blocks, warnings) = await _epub.ImportAsync(stream);
        }
        catch (Lilia.Core.Security.UnsafeZipException ex)
        {
            _logger.LogWarning("ePub {FileName} refused by zip limits: {Detail}", fileName, ex.Message);
            throw new NotAnEpubException(Lilia.Core.Security.UnsafeZipException.BookMessage);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            throw new NotAnEpubException($"\"{fileName}\" isn't a readable ePub: {ex.Message}");
        }

        // No package document: the parser returns its reason as the only warning.
        if (blocks.Count == 0 && warnings.Any(w => w.Contains("OPF", StringComparison.Ordinal)))
            throw new NotAnEpubException($"\"{fileName}\" isn't an ePub: {warnings[0]}");
        if (blocks.Count == 0)
            throw new NotAnEpubException($"\"{fileName}\" has no content to import.");

        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        var title = !string.IsNullOrWhiteSpace(metadata.Title) && metadata.Title != "Unknown"
            ? metadata.Title.Trim()
            : Path.GetFileNameWithoutExtension(fileName);

        _context.Jobs.Add(new Job
        {
            Id = jobId,
            TenantId = userId,
            UserId = userId,
            JobType = JobTypes.Import,
            Status = JobStatus.Processing,
            Progress = 50,
            SourceFormat = "epub",
            TargetFormat = "lilia",
            SourceFileName = fileName,
            InputFileSize = bytes.LongLength,
            Direction = "INBOUND",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _context.SaveChangesAsync(ct);

        // Keep the original book next to the other imports' sources — an
        // opaque locator, as for DOCX and PDF. A storage hiccup is not a
        // reason to lose the import.
        string? sourceKey = $"imports/{jobId}.epub";
        try
        {
            using var upload = new MemoryStream(bytes, writable: false);
            await _storage.UploadAsync(sourceKey, upload, "application/epub+zip");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EpubImport] Could not keep the source .epub for job {JobId}", jobId);
            sourceKey = null;
        }

        var reviewBlocks = ToReviewBlocks(blocks);
        var created = await _review.CreateSessionFromImportAsync(
            userId, jobId, title, reviewBlocks,
            warnings: warnings.Cast<object>().ToList(),
            sourceFilePath: sourceKey);
        var sessionId = created.Session.Id;

        await _context.ImportReviewSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SourceFormat, "epub")
                .SetProperty(x => x.AutoFinalizeEnabled, autoFinalize), ct);

        // What Clean Up did, where the review shows it: the Diagnostics tab.
        _context.ImportDiagnostics.AddRange(Diagnose(sessionId, warnings, reviewBlocks, now));
        await _context.SaveChangesAsync(ct);

        await _context.Jobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(j => j
                .SetProperty(x => x.Status, JobStatus.Completed)
                .SetProperty(x => x.Progress, 100)
                .SetProperty(x => x.CompletedAt, DateTime.UtcNow)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        // "Review off": the finalize the review page would run, run now. It
        // links the job to the document, which is how the page finds it.
        if (autoFinalize)
        {
            await _review.FinalizeSessionAsync(sessionId, userId, new FinalizeSessionDto(title, Force: true));
        }

        _logger.LogInformation(
            "[EpubImport] {File}: job {JobId}, review session {SessionId}, {Blocks} blocks, {Warnings} warnings",
            fileName, jobId, sessionId, reviewBlocks.Count, warnings.Count);

        return new LatexImportUploadResponseDto(sessionId, jobId);
    }

    /// <summary>The parser's blocks as review rows, in reading order.</summary>
    internal static List<CreateReviewBlockDto> ToReviewBlocks(IReadOnlyList<Block> blocks) =>
        blocks
            .OrderBy(b => b.SortOrder)
            .Select((b, i) => new CreateReviewBlockDto(
                Id: $"blk_{Guid.NewGuid():N}",
                Type: b.Type,
                Content: JsonSerializer.SerializeToElement(b.Content.RootElement),
                // Clean Up is heuristic: front/back matter and unknown
                // elements are folded into one block, so they get a lower
                // confidence than a heading or a plain paragraph.
                Confidence: b.Type is BlockTypes.FrontMatter or BlockTypes.BackMatter ? 60 : 90,
                Warnings: null,
                SortOrder: i,
                Depth: 0))
            .ToList();

    private static readonly Regex InlineStyle = new(@"^Detected inline style on <(\w+)>$", RegexOptions.Compiled);

    /// <summary>
    /// The parser's warnings, and what it folded together, as diagnostic rows.
    /// Inline-style notices come one per element, so they are counted into a
    /// single row per tag rather than listed.
    /// </summary>
    internal static IEnumerable<ImportDiagnostic> Diagnose(
        Guid sessionId, IEnumerable<string> warnings, IEnumerable<CreateReviewBlockDto> blocks, DateTime now)
    {
        var styled = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in warnings)
        {
            var m = InlineStyle.Match(w);
            if (m.Success)
            {
                styled[m.Groups[1].Value] = styled.GetValueOrDefault(m.Groups[1].Value) + 1;
                continue;
            }

            var (category, severity, code, action) = w switch
            {
                _ when w.StartsWith("Spine item not found", StringComparison.Ordinal) =>
                    ("missing_asset", "warning", "EPUB.MISSING_CHAPTER", "This chapter is listed in the book but not in the file; its text is not in the import."),
                _ when w.StartsWith("No <body> found", StringComparison.Ordinal) =>
                    ("parse_ambiguity", "warning", "EPUB.EMPTY_CHAPTER", "This chapter has no body; nothing was imported from it."),
                _ when w.StartsWith("Error parsing", StringComparison.Ordinal) =>
                    ("parse_ambiguity", "warning", "EPUB.UNREADABLE_CHAPTER", "This chapter could not be read; nothing was imported from it."),
                _ => ("parse_ambiguity", "info", "EPUB.NOTE", (string?)null),
            };
            yield return Row(sessionId, null, category, severity, code, w, action, now);
        }

        foreach (var (tag, count) in styled)
        {
            yield return Row(sessionId, null, "auto_shimmed", "info", "EPUB.INLINE_STYLE_DROPPED",
                $"{count} <{tag}> element{(count == 1 ? "" : "s")} had inline styles; Clean Up kept the text and dropped the styling.",
                null, now);
        }

        foreach (var b in blocks.Where(b => b.Type is BlockTypes.FrontMatter or BlockTypes.BackMatter))
        {
            var part = b.Type == BlockTypes.FrontMatter ? "front matter" : "back matter";
            yield return Row(sessionId, b.Id, "parse_ambiguity", "info", "EPUB.MATTER_MERGED",
                $"The book's {part} was merged into one block.",
                "Split it, or reject it if it's not part of your document.", now);
        }
    }

    private static ImportDiagnostic Row(
        Guid sessionId, string? blockId, string category, string severity, string code,
        string message, string? action, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        BlockId = blockId,
        Category = category,
        Severity = severity,
        Code = code,
        Message = message,
        SuggestedAction = action,
        CreatedAt = now,
    };
}
