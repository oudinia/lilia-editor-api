using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Api.Events.Common;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public record AccountExportDto(Guid Id, string Status, DateTime CreatedAt, DateTime? CompletedAt, DateTime? ExpiresAt, string? FileName, int Documents);

/// <summary>
/// "Export everything" (Settings → Your data, launch redesign 3f): one archive of every document the
/// author owns, as LaTeX and as Lilia's own data, with their profile. A background job, one at a time,
/// on every plan; the archive stays downloadable for 7 days. Archives are written outside the public
/// uploads folder and served only to their owner.
/// </summary>
public class AccountExportService
{
    public const string JobType = "ACCOUNT_EXPORT";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly LiliaDbContext _db;
    private readonly ILaTeXExportService _latex;
    private readonly Wolverine.IMessageBus _bus;
    private readonly IEmailService? _email;
    private readonly IConfiguration _config;
    private readonly ILogger<AccountExportService> _logger;

    public AccountExportService(LiliaDbContext db, ILaTeXExportService latex, Wolverine.IMessageBus bus,
        IConfiguration config, ILogger<AccountExportService> logger, IEmailService? email = null)
    {
        _db = db; _latex = latex; _bus = bus; _config = config; _logger = logger; _email = email;
    }

    /// <summary>Private: never under Storage:LocalPath, which is served at /uploads.</summary>
    public string Root => Path.GetFullPath(_config["AccountExport:Path"] ?? "./account-exports");

    public async Task<AccountExportDto> StartAsync(string userId, CancellationToken ct = default)
    {
        var running = await _db.Jobs.AsNoTracking()
            .Where(j => j.UserId == userId && j.JobType == JobType && (j.Status == JobStatus.Pending || j.Status == JobStatus.Processing))
            .OrderByDescending(j => j.CreatedAt).FirstOrDefaultAsync(ct);
        if (running is not null) return ToDto(running);

        var job = new Job
        {
            Id = Guid.NewGuid(), TenantId = userId, UserId = userId, JobType = JobType,
            Status = JobStatus.Pending, Direction = "OUTBOUND", TargetFormat = "zip",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(ct);
        await _bus.PublishAsync(new RunAccountExportEvent(job.Id));
        return ToDto(job);
    }

    public async Task<AccountExportDto?> LatestAsync(string userId, CancellationToken ct = default)
    {
        var job = await _db.Jobs.AsNoTracking().Where(j => j.UserId == userId && j.JobType == JobType)
            .OrderByDescending(j => j.CreatedAt).FirstOrDefaultAsync(ct);
        return job is null ? null : ToDto(job);
    }

    /// <summary>The archive, to its owner only, while it hasn't expired. Null otherwise.</summary>
    public async Task<(Stream Stream, string FileName)?> OpenAsync(string userId, Guid jobId, CancellationToken ct = default)
    {
        var job = await _db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId && j.JobType == JobType, ct);
        if (job is null || job.Status != JobStatus.Completed || job.ExpiresAt < DateTime.UtcNow || job.OutputFileKey is null) return null;
        var path = Path.GetFullPath(Path.Combine(Root, job.OutputFileKey));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path)) return null;
        return (File.OpenRead(path), job.ResultFileName ?? "lilia-export.zip");
    }

    public async Task RunAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.JobType == JobType, ct);
        if (job is null || job.Status is JobStatus.Completed) return;
        job.Status = JobStatus.Processing; job.StartedAt = DateTime.UtcNow; job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        try
        {
            var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == job.UserId, ct);
            var docs = await _db.Documents.AsNoTracking()
                .Where(d => d.OwnerId == job.UserId && !d.IsPlayground && !d.IsHelpContent)
                .Include(d => d.Blocks).Include(d => d.BibliographyEntries)
                .OrderBy(d => d.CreatedAt).ToListAsync(ct);

            var key = Path.Combine(job.UserId, $"{job.Id}.zip");
            var path = Path.Combine(Root, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var skipped = new List<string>();
            await using (var file = File.Create(path))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Write(zip, "account.json", JsonSerializer.Serialize(new { user.Id, user.Email, user.Name, user.CreatedAt, exportedAt = DateTime.UtcNow }, Json));
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in docs)
                {
                    var folder = $"documents/{FolderName(d, used)}";
                    Write(zip, $"{folder}/document.json", JsonSerializer.Serialize(new
                    {
                        d.Id, d.Title, d.Language, d.CreatedAt, d.UpdatedAt, d.LatexDocumentClass, d.LatexDocumentClassOptions,
                        d.LatexPackages, d.CustomPreamble, d.PaperSize, d.FontSize, d.FontFamily, d.IsTemplate,
                        blocks = d.Blocks.OrderBy(b => b.SortOrder).Select(b => new { b.Id, b.Type, b.ParentId, b.SortOrder, b.Depth, content = b.Content.RootElement }),
                        bibliography = d.BibliographyEntries.Select(e => new { e.CiteKey, e.EntryType, data = e.Data.RootElement }),
                    }, Json));
                    try
                    {
                        await using var project = await _latex.ExportToZipAsync(d.Id, new LaTeXExportOptions());
                        using var inner = new ZipArchive(project, ZipArchiveMode.Read);
                        foreach (var entry in inner.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
                        {
                            var copy = zip.CreateEntry($"{folder}/latex/{entry.FullName}", CompressionLevel.Optimal);
                            await using var from = entry.Open();
                            await using var to = copy.Open();
                            await from.CopyToAsync(to, ct);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "[AccountExport] LaTeX for {DocumentId} skipped", d.Id);
                        skipped.Add(d.Title);
                    }
                }
                Write(zip, "README.txt", Readme(user.Email, docs.Count, skipped));
            }

            job.Status = JobStatus.Completed; job.Progress = 100; job.CompletedAt = DateTime.UtcNow;
            job.ExpiresAt = job.CompletedAt + Lifetime; job.OutputFileKey = key;
            job.ResultFileName = $"lilia-export-{job.CompletedAt:yyyy-MM-dd}.zip";
            job.Metadata = JsonDocument.Parse(JsonSerializer.Serialize(new { documents = docs.Count, latexSkipped = skipped.Count }));
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await NotifyAsync(user.Email, user.Locale, job, docs.Count, new FileInfo(path).Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.Status = JobStatus.Failed; job.ErrorMessage = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task NotifyAsync(string email, string? locale, Job job, int documents, long bytes)
    {
        if (_email is null || string.IsNullOrEmpty(email)) return;
        try
        {
            await _email.SendExportReadyAsync(email, locale, job.CreatedAt, job.ExpiresAt ?? DateTime.UtcNow + Lifetime,
                job.ResultFileName ?? "lilia-export.zip", documents, bytes);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "[AccountExport] ready email not sent"); }
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    private static string FolderName(Document d, HashSet<string> used)
    {
        var slug = Regex.Replace((d.Title ?? "untitled").Normalize(NormalizationForm.FormD), @"[^\w\- ]+", "").Trim();
        slug = Regex.Replace(slug, @"\s+", "-");
        if (slug.Length == 0) slug = "untitled";
        if (slug.Length > 60) slug = slug[..60];
        var name = slug;
        if (!used.Add(name)) { name = $"{slug}-{d.Id.ToString()[..8]}"; used.Add(name); }
        return name;
    }

    private static string Readme(string email, int count, List<string> skipped) =>
        $"""
        Lilia export for {email}, made {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.

        account.json                     your profile
        documents/<title>/document.json  each document as Lilia stores it: blocks, settings, bibliography
        documents/<title>/latex/         the same document as a LaTeX project (main.tex, .bib, images)

        Documents: {count}.{(skipped.Count == 0 ? "" : $"\nNo LaTeX for: {string.Join(", ", skipped)} (their document.json is complete).")}
        """;

    private static AccountExportDto ToDto(Job j) => new(j.Id, j.Status, j.CreatedAt, j.CompletedAt, j.ExpiresAt, j.ResultFileName,
        j.Metadata?.RootElement.TryGetProperty("documents", out var n) == true ? n.GetInt32() : 0);
}
