using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public interface IRecentActivityService
{
    /// <summary>What <paramref name="userId"/> did recently, per tool, at most <paramref name="perTool"/> items each.</summary>
    Task<RecentActivityDto> GetRecentAsync(string userId, int perTool);
}

/// <summary>
/// The user's recent work, per tool, for Home and the switcher's Recent list.
/// </summary>
/// <remarks>
/// Only things the user acted on. New accounts are given starter documents
/// (<c>DocumentService.CloneStarterDocumentsAsync</c>); those are ours, not the
/// user's, so a document counts only once it has an audited act of the user's,
/// has been opened, or has been edited after it was made. Each tool is one
/// bounded, untracked query; documents add the audit scan and its jobs.
/// </remarks>
public class RecentActivityService : IRecentActivityService
{
    public const int DefaultPerTool = 3;
    public const int MaxPerTool = 10;

    // How far back the audit trail is read. Bounded so a prolific user costs
    // the same as a new one; the newest acts are the only ones that can rank.
    private const int AuditScan = 200;

    // A document edited within this long of being made was not edited by
    // anyone — creation and cloning write UpdatedAt a moment after CreatedAt.
    private static readonly TimeSpan EditSlack = TimeSpan.FromSeconds(5);

    private static readonly string[] DocumentActions =
    {
        "document.create", "document.import", "document.export", "document.restore", "document.public.copy",
    };
    private const string TemplateCreate = "template.create";

    private readonly LiliaDbContext _db;

    public RecentActivityService(LiliaDbContext db) => _db = db;

    public async Task<RecentActivityDto> GetRecentAsync(string userId, int perTool)
    {
        perTool = Math.Clamp(perTool, 1, MaxPerTool);

        var acts = await LoadDocumentActsAsync(userId);

        var tools = new List<RecentToolDto>();
        void Add(string tool, IEnumerable<RecentItemDto> items)
        {
            var list = items.OrderByDescending(i => i.At).ThenBy(i => i.Id).Take(perTool).ToList();
            if (list.Count > 0) tools.Add(new RecentToolDto(tool, list[0].At, list));
        }

        Add("documents", await DocumentsAsync(userId, perTool, acts.Documents));
        Add("tables", await TablesAsync(userId, perTool));
        Add("references", await ReferencesAsync(userId, perTool, acts.Documents.Keys.ToList()));
        Add("equations", await EquationsAsync(userId, perTool));
        Add("snippets", await SnippetsAsync(userId, perTool));
        Add("templates", await TemplatesAsync(userId, perTool, acts.Templates));
        Add("import", await JobsAsync(userId, perTool));

        return new RecentActivityDto(tools.OrderByDescending(t => t.LastUsedAt).ToList());
    }

    private sealed record Act(string Action, DateTime At, string? Format);

    private sealed record Acts(Dictionary<Guid, Act> Documents, Dictionary<Guid, Act> Templates);

    /// <summary>
    /// The user's latest whitelisted act per document (and per template), from
    /// the audit trail. Imports and exports are logged against the job, so the
    /// job says which document they were.
    /// </summary>
    private async Task<Acts> LoadDocumentActsAsync(string userId)
    {
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == userId && (DocumentActions.Contains(a.Action) || a.Action == TemplateCreate))
            .OrderByDescending(a => a.CreatedAt)
            .Take(AuditScan)
            .Select(a => new { a.Action, a.EntityType, a.EntityId, a.Details, a.CreatedAt })
            .ToListAsync();

        var jobIds = rows
            .Where(r => r.EntityType == "Job" && Guid.TryParse(r.EntityId, out _))
            .Select(r => Guid.Parse(r.EntityId!))
            .Distinct()
            .ToList();
        var jobs = jobIds.Count == 0
            ? new Dictionary<Guid, (Guid? DocumentId, DateTime? CompletedAt)>()
            : (await _db.Jobs.AsNoTracking()
                    .Where(j => j.UserId == userId && jobIds.Contains(j.Id))
                    .Select(j => new { j.Id, j.DocumentId, j.CompletedAt })
                    .ToListAsync())
                .ToDictionary(j => j.Id, j => (j.DocumentId, j.CompletedAt));

        var documents = new Dictionary<Guid, Act>();
        var templates = new Dictionary<Guid, Act>();

        foreach (var r in rows)
        {
            var format = DetailString(r.Details, "Format")?.ToLowerInvariant();

            if (r.Action == TemplateCreate)
            {
                if (Guid.TryParse(r.EntityId, out var templateId))
                    Keep(templates, templateId, new Act(r.Action, r.CreatedAt, null));
                continue;
            }

            Guid? documentId = null;
            var at = r.CreatedAt;
            if (r.EntityType == "Document")
            {
                if (Guid.TryParse(r.EntityId, out var id)) documentId = id;
            }
            else if (r.EntityType == "Job")
            {
                if (Guid.TryParse(r.EntityId, out var jobId) && jobs.TryGetValue(jobId, out var job))
                {
                    documentId = job.DocumentId;
                    if (r.Action == "document.import" && job.CompletedAt is { } completed) at = completed;
                }
                if (documentId is null && r.Action == "document.export"
                    && Guid.TryParse(DetailString(r.Details, "DocumentId"), out var exported))
                    documentId = exported;
            }

            if (documentId is { } docId) Keep(documents, docId, new Act(r.Action, at, format));
        }

        return new Acts(documents, templates);

        static void Keep(Dictionary<Guid, Act> map, Guid id, Act act)
        {
            if (!map.TryGetValue(id, out var existing) || act.At > existing.At) map[id] = act;
        }
    }

    /// <summary>A string (or GUID) property of the audit details, matched case-insensitively.</summary>
    private static string? DetailString(JsonDocument? details, string name)
    {
        if (details is null || details.RootElement.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in details.RootElement.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            return p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString())
                ? p.Value.GetString()
                : null;
        }
        return null;
    }

    /// <summary>The user's own documents, as the document list shows them.</summary>
    private IQueryable<Document> OwnedDocuments(string userId) =>
        _db.Documents.AsNoTracking()
            .Where(d => d.OwnerId == userId && !d.IsTemplate && !d.IsPlayground && !d.IsHelpContent
                && !_db.DocumentHides.Any(h => h.UserId == userId && h.DocumentId == d.Id));

    private async Task<IEnumerable<RecentItemDto>> DocumentsAsync(string userId, int perTool, Dictionary<Guid, Act> acts)
    {
        var actedIds = acts.Keys.ToList();

        // The ones with an audited act, whatever their timestamps say…
        var acted = actedIds.Count == 0
            ? new List<DocRow>()
            : await OwnedDocuments(userId)
                .Where(d => actedIds.Contains(d.Id))
                .Select(d => new DocRow(d.Id, d.Title, d.UpdatedAt, d.LastOpenedAt))
                .ToListAsync();

        // …and the most recent ones opened or edited since they were made.
        var slack = EditSlack.TotalSeconds;
        var touched = await OwnedDocuments(userId)
            .Where(d => d.LastOpenedAt != null || d.UpdatedAt > d.CreatedAt.AddSeconds(slack))
            .OrderByDescending(d => d.LastOpenedAt > d.UpdatedAt ? d.LastOpenedAt!.Value : d.UpdatedAt)
            .Take(perTool)
            .Select(d => new DocRow(d.Id, d.Title, d.UpdatedAt, d.LastOpenedAt))
            .ToListAsync();

        return acted.Concat(touched).DistinctBy(d => d.Id).Select(d =>
        {
            acts.TryGetValue(d.Id, out var act);
            var at = Max(Max(d.UpdatedAt, d.LastOpenedAt), act?.At);
            return new RecentItemDto(
                d.Id,
                string.IsNullOrWhiteSpace(d.Title) ? "Untitled document" : d.Title,
                at,
                UpdatedAt: d.UpdatedAt,
                Action: act?.Action,
                ActionAt: act?.At,
                ActionFormat: act?.Format);
        });
    }

    private sealed record DocRow(Guid Id, string Title, DateTime UpdatedAt, DateTime? LastOpenedAt);

    private static DateTime Max(DateTime a, DateTime? b) => b is { } v && v > a ? v : a;

    private async Task<IEnumerable<RecentItemDto>> TablesAsync(string userId, int perTool)
    {
        // Same count GET /api/tables shows: the papers using it.
        var rows = await _db.Tables.AsNoTracking()
            .Where(t => t.OwnerId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .Take(perTool)
            .Select(t => new { t.Id, t.Caption, t.Label, t.UpdatedAt, DocumentCount = t.Documents.Count() })
            .ToListAsync();

        return rows.Select(t => new RecentItemDto(
            t.Id,
            !string.IsNullOrWhiteSpace(t.Caption) ? t.Caption
                : !string.IsNullOrWhiteSpace(t.Label) ? t.Label
                : "Untitled table",
            t.UpdatedAt,
            UpdatedAt: t.UpdatedAt,
            DocumentCount: t.DocumentCount));
    }

    private async Task<IEnumerable<RecentItemDto>> ReferencesAsync(string userId, int perTool, List<Guid> actedDocumentIds)
    {
        // Starter documents come with references; those are not the user's
        // either. An entry counts when its document is one the user acted on,
        // or when the entry itself was added or edited after the document was made.
        var slack = EditSlack.TotalSeconds;
        var rows = await _db.BibliographyEntries.AsNoTracking()
            .Where(b => b.Document.OwnerId == userId && !b.Document.IsPlayground
                && (b.Document.LastOpenedAt != null
                    || b.Document.UpdatedAt > b.Document.CreatedAt.AddSeconds(slack)
                    || b.UpdatedAt > b.Document.CreatedAt.AddSeconds(slack)
                    || actedDocumentIds.Contains(b.DocumentId)))
            .OrderByDescending(b => b.UpdatedAt)
            .Take(perTool)
            .Select(b => new { b.Id, b.CiteKey, b.Data, b.UpdatedAt, b.DocumentId })
            .ToListAsync();

        return rows.Select(b => new RecentItemDto(
            b.Id,
            EntryTitle(b.Data) ?? (string.IsNullOrWhiteSpace(b.CiteKey) ? "Untitled reference" : b.CiteKey),
            b.UpdatedAt,
            UpdatedAt: b.UpdatedAt,
            DocumentId: b.DocumentId));
    }

    private static string? EntryTitle(JsonDocument data)
    {
        if (data.RootElement.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in data.RootElement.EnumerateObject())
        {
            if (!string.Equals(p.Name, "title", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind != JsonValueKind.String) return null;
            // BibTeX protects capitals with braces; they are not part of the title.
            var title = p.Value.GetString()!.Replace("{", "").Replace("}", "").Trim();
            return title.Length == 0 ? null : title;
        }
        return null;
    }

    private async Task<IEnumerable<RecentItemDto>> EquationsAsync(string userId, int perTool)
    {
        var rows = await _db.Formulas.AsNoTracking()
            .Where(f => f.UserId == userId && !f.IsSystem)
            .OrderByDescending(f => f.UpdatedAt)
            .Take(perTool)
            .Select(f => new { f.Id, f.Name, f.UpdatedAt })
            .ToListAsync();
        return rows.Select(f => new RecentItemDto(f.Id, NameOr(f.Name, "Untitled equation"), f.UpdatedAt, UpdatedAt: f.UpdatedAt));
    }

    private async Task<IEnumerable<RecentItemDto>> SnippetsAsync(string userId, int perTool)
    {
        var rows = await _db.Snippets.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsSystem)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(perTool)
            .Select(s => new { s.Id, s.Name, s.UpdatedAt })
            .ToListAsync();
        return rows.Select(s => new RecentItemDto(s.Id, NameOr(s.Name, "Untitled snippet"), s.UpdatedAt, UpdatedAt: s.UpdatedAt));
    }

    private async Task<IEnumerable<RecentItemDto>> TemplatesAsync(string userId, int perTool, Dictionary<Guid, Act> acts)
    {
        var rows = await _db.Documents.AsNoTracking()
            .Where(d => d.OwnerId == userId && d.IsTemplate)
            .OrderByDescending(d => d.UpdatedAt)
            .Take(perTool)
            .Select(d => new { d.Id, d.TemplateName, d.Title, d.UpdatedAt })
            .ToListAsync();

        return rows.Select(d =>
        {
            acts.TryGetValue(d.Id, out var act);
            return new RecentItemDto(
                d.Id,
                NameOr(d.TemplateName ?? d.Title, "Untitled template"),
                d.UpdatedAt,
                UpdatedAt: d.UpdatedAt,
                Action: act?.Action,
                ActionAt: act?.At);
        });
    }

    private async Task<IEnumerable<RecentItemDto>> JobsAsync(string userId, int perTool)
    {
        var rows = await _db.Jobs.AsNoTracking()
            .Where(j => j.UserId == userId
                && (j.JobType == JobTypes.Import || j.JobType == JobTypes.Export)
                && j.Status != JobStatus.Failed && j.Status != JobStatus.Cancelled)
            .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
            .Take(perTool)
            .Select(j => new
            {
                j.Id, j.JobType, j.SourceFormat, j.TargetFormat, j.SourceFileName, j.DocumentId,
                DocumentTitle = j.Document != null ? j.Document.Title : null,
                At = j.CompletedAt ?? j.CreatedAt,
            })
            .ToListAsync();

        return rows.Select(j =>
        {
            var isImport = j.JobType == JobTypes.Import;
            return new RecentItemDto(
                j.Id,
                NameOr(j.SourceFileName ?? j.DocumentTitle, "Untitled"),
                j.At,
                Action: isImport ? "document.import" : "document.export",
                ActionAt: j.At,
                ActionFormat: (isImport ? j.SourceFormat : j.TargetFormat)?.ToLowerInvariant(),
                DocumentId: j.DocumentId);
        });
    }

    private static string NameOr(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;
}
