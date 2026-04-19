using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Lilia.Api.Services;

/// <summary>
/// Database-first structural-findings engine.
///
/// Compute (SessionId | DocumentId):
///   1. Clear any pending findings for the owner (dismissed + applied ones
///      stay for history).
///   2. Read blocks in one projection.
///   3. Run the rule pipeline in memory (one pass, no per-rule queries).
///   4. Bulk-insert new findings via Npgsql COPY — no EF change tracker.
///
/// List: pure SELECT.
/// Apply / Dismiss: single ExecuteUpdateAsync row write.
///
/// See lilia-docs/technical/import-export-db-first.md — this is the canonical
/// shape for import/export pipelines.
/// </summary>
public interface IImportHintService
{
    Task<int> ComputeForSessionAsync(Guid sessionId, string userId, CancellationToken ct = default);
    Task<int> ComputeForDocumentAsync(Guid documentId, string userId, CancellationToken ct = default);

    Task<List<ImportStructuralFindingDto>> ListForSessionAsync(Guid sessionId, string userId, CancellationToken ct = default);
    Task<List<ImportStructuralFindingDto>> ListForDocumentAsync(Guid documentId, string userId, CancellationToken ct = default);

    Task<bool> ApplyAsync(Guid findingId, string userId, CancellationToken ct = default);
    Task<bool> DismissAsync(Guid findingId, string userId, CancellationToken ct = default);
}

public class ImportHintService : IImportHintService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<ImportHintService> _logger;

    public ImportHintService(LiliaDbContext context, ILogger<ImportHintService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ─── Compute pass (bulk insert) ──────────────────────────────────────

    public async Task<int> ComputeForSessionAsync(Guid sessionId, string userId, CancellationToken ct = default)
    {
        if (!await CanAccessSessionAsync(sessionId, userId, ct)) return 0;

        var category = await _context.ImportReviewSessions
            .Where(s => s.Id == sessionId)
            .Select(s => s.DocumentCategory)
            .FirstOrDefaultAsync(ct);

        var reviews = await _context.ImportBlockReviews
            .AsNoTracking()
            .Where(b => b.SessionId == sessionId)
            .OrderBy(b => b.SortOrder)
            .Select(b => new BlockView(
                b.BlockId,
                b.BlockIndex,
                b.CurrentType ?? b.OriginalType,
                b.CurrentContent ?? b.OriginalContent))
            .ToListAsync(ct);

        await ClearPendingForSessionAsync(sessionId, ct);
        var findings = AnalyseBlocks(reviews, sessionId: sessionId, documentId: null, category: category);
        await BulkInsertAsync(findings, ct);
        _logger.LogInformation("[Findings] Staged {Count} findings for session {Session}", findings.Count, sessionId);
        return findings.Count;
    }

    public async Task<int> ComputeForDocumentAsync(Guid documentId, string userId, CancellationToken ct = default)
    {
        if (!await CanAccessDocumentAsync(documentId, userId, ct)) return 0;

        var category = await _context.Documents
            .IgnoreQueryFilters()
            .Where(d => d.Id == documentId)
            .Select(d => d.DocumentCategory)
            .FirstOrDefaultAsync(ct);

        var blocks = await _context.Blocks
            .AsNoTracking()
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .Select(b => new BlockView(b.Id.ToString(), b.SortOrder, b.Type, b.Content))
            .ToListAsync(ct);

        await ClearPendingForDocumentAsync(documentId, ct);
        var findings = AnalyseBlocks(blocks, sessionId: null, documentId: documentId, category: category);
        await BulkInsertAsync(findings, ct);
        _logger.LogInformation("[Findings] Staged {Count} findings for document {Document}", findings.Count, documentId);
        return findings.Count;
    }

    // ─── List (pure SELECT) ──────────────────────────────────────────────

    public async Task<List<ImportStructuralFindingDto>> ListForSessionAsync(Guid sessionId, string userId, CancellationToken ct = default)
    {
        if (!await CanAccessSessionAsync(sessionId, userId, ct)) return new();
        return await ProjectFindings(_context.ImportStructuralFindings.AsNoTracking()
            .Where(f => f.SessionId == sessionId), ct);
    }

    public async Task<List<ImportStructuralFindingDto>> ListForDocumentAsync(Guid documentId, string userId, CancellationToken ct = default)
    {
        if (!await CanAccessDocumentAsync(documentId, userId, ct)) return new();
        return await ProjectFindings(_context.ImportStructuralFindings.AsNoTracking()
            .Where(f => f.DocumentId == documentId), ct);
    }

    // ─── Apply + Dismiss (ExecuteUpdateAsync) ────────────────────────────

    public async Task<bool> ApplyAsync(Guid findingId, string userId, CancellationToken ct = default)
    {
        // Reads the finding once to determine target (session vs document)
        // and permission. Mutation is a single UPDATE. We don't execute the
        // ActionKind mutation server-side yet — the frontend performs the
        // block-level change through the existing block-update API and then
        // calls Apply to mark the finding resolved. A future iteration can
        // fold the mutation in-band.
        var f = await _context.ImportStructuralFindings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == findingId, ct);
        if (f is null) return false;
        if (f.SessionId.HasValue && !await CanAccessSessionAsync(f.SessionId.Value, userId, ct)) return false;
        if (f.DocumentId.HasValue && !await CanAccessDocumentAsync(f.DocumentId.Value, userId, ct)) return false;

        var now = DateTime.UtcNow;
        var affected = await _context.ImportStructuralFindings
            .Where(x => x.Id == findingId && x.Status == "pending")
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, "applied")
                .SetProperty(x => x.ResolvedBy, userId)
                .SetProperty(x => x.ResolvedAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);
        return affected > 0;
    }

    public async Task<bool> DismissAsync(Guid findingId, string userId, CancellationToken ct = default)
    {
        var f = await _context.ImportStructuralFindings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == findingId, ct);
        if (f is null) return false;
        if (f.SessionId.HasValue && !await CanAccessSessionAsync(f.SessionId.Value, userId, ct)) return false;
        if (f.DocumentId.HasValue && !await CanAccessDocumentAsync(f.DocumentId.Value, userId, ct)) return false;

        var now = DateTime.UtcNow;
        var affected = await _context.ImportStructuralFindings
            .Where(x => x.Id == findingId && x.Status == "pending")
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, "dismissed")
                .SetProperty(x => x.ResolvedBy, userId)
                .SetProperty(x => x.ResolvedAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);
        return affected > 0;
    }

    // ─── Permission gates ────────────────────────────────────────────────

    private async Task<bool> CanAccessSessionAsync(Guid sessionId, string userId, CancellationToken ct)
    {
        var s = await _context.ImportReviewSessions
            .AsNoTracking()
            .Include(x => x.Collaborators)
            .FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null) return false;
        return s.OwnerId == userId || s.Collaborators.Any(c => c.UserId == userId);
    }

    private async Task<bool> CanAccessDocumentAsync(Guid documentId, string userId, CancellationToken ct)
    {
        var d = await _context.Documents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId, ct);
        if (d is null) return false;
        if (d.OwnerId == userId) return true;
        return await _context.DocumentCollaborators
            .AnyAsync(c => c.DocumentId == documentId && c.UserId == userId, ct);
    }

    // ─── Bulk-insert via COPY ────────────────────────────────────────────

    private async Task ClearPendingForSessionAsync(Guid sessionId, CancellationToken ct)
    {
        await _context.ImportStructuralFindings
            .Where(f => f.SessionId == sessionId && f.Status == "pending")
            .ExecuteDeleteAsync(ct);
    }

    private async Task ClearPendingForDocumentAsync(Guid documentId, CancellationToken ct)
    {
        await _context.ImportStructuralFindings
            .Where(f => f.DocumentId == documentId && f.Status == "pending")
            .ExecuteDeleteAsync(ct);
    }

    private async Task BulkInsertAsync(List<ImportStructuralFinding> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        var conn = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        const string copy = @"COPY import_structural_findings
            (id, session_id, document_id, block_id, kind, severity,
             title, detail, suggested_action, action_kind, action_payload,
             status, resolved_by, resolved_at, created_at, updated_at)
            FROM STDIN BINARY";

        await using var writer = await conn.BeginBinaryImportAsync(copy, ct);
        var now = DateTime.UtcNow;
        foreach (var f in rows)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(f.Id == Guid.Empty ? Guid.NewGuid() : f.Id, NpgsqlDbType.Uuid, ct);
            await WriteNullableGuid(writer, f.SessionId, ct);
            await WriteNullableGuid(writer, f.DocumentId, ct);
            await WriteNullableString(writer, f.BlockId, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(f.Kind, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(f.Severity, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(f.Title, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(f.Detail, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(f.SuggestedAction, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(f.ActionKind, NpgsqlDbType.Varchar, ct);
            if (f.ActionPayload is null) await writer.WriteNullAsync(ct);
            else await writer.WriteAsync(f.ActionPayload.RootElement.GetRawText(), NpgsqlDbType.Jsonb, ct);
            await writer.WriteAsync(f.Status, NpgsqlDbType.Varchar, ct);
            await WriteNullableString(writer, f.ResolvedBy, NpgsqlDbType.Varchar, ct);
            await WriteNullableTs(writer, f.ResolvedAt, ct);
            await writer.WriteAsync(f.CreatedAt == default ? now : f.CreatedAt, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz, ct);
        }
        await writer.CompleteAsync(ct);
    }

    private static async Task WriteNullableGuid(NpgsqlBinaryImporter w, Guid? v, CancellationToken ct)
    {
        if (v is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(v.Value, NpgsqlDbType.Uuid, ct);
    }
    private static async Task WriteNullableString(NpgsqlBinaryImporter w, string? v, NpgsqlDbType t, CancellationToken ct)
    {
        if (v is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(v, t, ct);
    }
    private static async Task WriteNullableTs(NpgsqlBinaryImporter w, DateTime? v, CancellationToken ct)
    {
        if (v is null) await w.WriteNullAsync(ct);
        else await w.WriteAsync(v.Value, NpgsqlDbType.TimestampTz, ct);
    }

    private async Task<List<ImportStructuralFindingDto>> ProjectFindings(IQueryable<ImportStructuralFinding> q, CancellationToken ct)
    {
        // Materialise first — JsonDocument → JsonElement conversion can't be
        // translated to SQL by Npgsql. Then project in-memory.
        var rows = await q.ToListAsync(ct);
        // Sort in-memory — EF trips translating `OrderBy(f => f.Status == "pending" ? 0 : ...)`
        // when the row type has a `JsonDocument?` column (jsonb). Result sets are tiny
        // (<50 findings/doc), so LINQ-to-objects cost is negligible.
        return rows
            .OrderBy(f => f.Status == "pending" ? 0 : f.Status == "applied" ? 1 : 2)
            .ThenBy(f => f.CreatedAt)
            .Select(f => new ImportStructuralFindingDto(
                Id: f.Id, SessionId: f.SessionId, DocumentId: f.DocumentId, BlockId: f.BlockId,
                Kind: f.Kind, Severity: f.Severity, Title: f.Title, Detail: f.Detail,
                SuggestedAction: f.SuggestedAction, ActionKind: f.ActionKind,
                ActionPayload: f.ActionPayload == null
                    ? (JsonElement?)null
                    : JsonSerializer.Deserialize<JsonElement>(f.ActionPayload.RootElement.GetRawText()),
                Status: f.Status, ResolvedBy: f.ResolvedBy, ResolvedAt: f.ResolvedAt,
                CreatedAt: f.CreatedAt, UpdatedAt: f.UpdatedAt,
                Source: f.Source
            )).ToList();
    }

    // ─── Rule pipeline ───────────────────────────────────────────────────
    // Pure function from a block view + category to a list of findings.
    // No DB calls inside — all decisions on in-memory data. Easy to test,
    // easy to extend with category-specialised rules.

    private record BlockView(string BlockId, int Index, string Type, JsonDocument? Content);

    private static List<ImportStructuralFinding> AnalyseBlocks(
        List<BlockView> blocks,
        Guid? sessionId,
        Guid? documentId,
        string? category)
    {
        var findings = new List<ImportStructuralFinding>();
        var cvSectionCount = 0;
        var headingPromotions = 0;
        var isCvMode = category == "cv";

        void Add(ImportStructuralFinding f)
        {
            f.Id = Guid.NewGuid();
            f.SessionId = sessionId;
            f.DocumentId = documentId;
            f.Status = "pending";
            findings.Add(f);
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var next = i + 1 < blocks.Count ? blocks[i + 1] : null;
            var txt = GetStringField(b.Content, "text");

            // 1. CV section keyword match.
            if (!string.IsNullOrWhiteSpace(txt))
            {
                var cat = MatchCvSection(txt!);
                if (cat is not null)
                {
                    cvSectionCount++;
                    if (b.Type == "heading")
                    {
                        Add(new ImportStructuralFinding
                        {
                            Kind = "cv_section",
                            BlockId = b.BlockId,
                            Title = $"Looks like the '{cat}' section of a CV",
                            Detail = $"'{txt}' matches a CV section keyword. Converting to a cvSection block emits \\cvsection{{…}} on moderncv / altacv classes.",
                            SuggestedAction = "Convert to cvSection block",
                            ActionKind = "convert_block_type",
                            ActionPayload = JsonDocument.Parse(JsonSerializer.Serialize(new { targetType = "cvSection", category = cat })),
                        });
                    }
                    else if (b.Type == "paragraph")
                    {
                        Add(new ImportStructuralFinding
                        {
                            Kind = "paragraph_is_cv_section",
                            BlockId = b.BlockId,
                            Title = $"'{txt}' looks like a CV section that lost its heading style",
                            Detail = "This paragraph matches a CV section keyword but wasn't parsed as a heading — likely the Word source used visual formatting instead of Heading 1. Promoting it restores structure.",
                            SuggestedAction = "Promote to heading + mark as CV section",
                            ActionKind = "convert_block_type",
                            ActionPayload = JsonDocument.Parse(JsonSerializer.Serialize(new { targetType = "heading", alsoSuggest = "cvSection", category = cat })),
                        });
                    }
                }
            }

            // 2. Contact info in an early paragraph.
            if (b.Type == "paragraph" && i < 3 && !string.IsNullOrWhiteSpace(txt))
            {
                var contacts = ExtractContacts(txt!);
                if (contacts.Count >= 2)
                {
                    Add(new ImportStructuralFinding
                    {
                        Kind = "personal_info",
                        BlockId = b.BlockId,
                        Title = "Contact details detected",
                        Detail = $"Found {string.Join(", ", contacts.Select(c => c.Kind))} here. A personalInfo block emits \\name/\\email/\\phone/\\social for CV classes.",
                        SuggestedAction = "Convert to personalInfo block",
                        ActionKind = "convert_block_type",
                        ActionPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
                        {
                            targetType = "personalInfo",
                            extracted = contacts.ToDictionary(c => c.Kind, c => c.Value),
                        })),
                    });
                }
            }

            // 3. Header-table unpack — first table block at doc start contains contact info.
            if (i <= 1 && b.Type == "table")
            {
                var flattened = FlattenTableCells(b.Content);
                var contacts = ExtractContacts(flattened);
                if (contacts.Count >= 1)
                {
                    Add(new ImportStructuralFinding
                    {
                        Kind = "header_table_unpack",
                        BlockId = b.BlockId,
                        Title = "Header in a Word table — should be split",
                        Detail = "This top-of-document table holds contact info. Word authors often use a 1-row table as a visual header; LaTeX prefers a personalInfo block + intro paragraph.",
                        SuggestedAction = "Split into personalInfo + intro paragraph",
                        ActionKind = "split_header_table",
                        ActionPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
                        {
                            contacts = contacts.ToDictionary(c => c.Kind, c => c.Value),
                        })),
                    });
                }
            }

            // 4. Visual-heading promotion — short, unpunctuated paragraph before a list.
            if (b.Type == "paragraph"
                && !string.IsNullOrWhiteSpace(txt)
                && LooksLikeHeading(txt!)
                && MatchCvSection(txt!) is null
                && next is not null
                && (next.Type == "list" || (next.Type == "paragraph" && !LooksLikeHeading(GetStringField(next.Content, "text") ?? "")))
                && headingPromotions < 25)
            {
                headingPromotions++;
                Add(new ImportStructuralFinding
                {
                    Kind = "paragraph_as_heading",
                    BlockId = b.BlockId,
                    Title = $"'{txt}' looks like a heading",
                    Detail = "Short, no trailing punctuation, followed by content. Word likely formatted it with bold / size rather than the Heading style, so the parser missed it.",
                    SuggestedAction = "Promote to heading",
                    ActionKind = "convert_block_type",
                    ActionPayload = JsonDocument.Parse(JsonSerializer.Serialize(new { targetType = "heading" })),
                });
            }

            // 5. Spurious tableOfContents block (Word TOC field with no entries).
            if (b.Type == "tableOfContents" && IsContentEmpty(b.Content))
            {
                Add(new ImportStructuralFinding
                {
                    Kind = "spurious_toc",
                    BlockId = b.BlockId,
                    Title = "Empty table-of-contents block",
                    Detail = "The importer emitted a tableOfContents from a Word TOC field with no populated entries. Usually sits at the top of the document and clutters the structure.",
                    SuggestedAction = "Delete this block",
                    ActionKind = "delete_block",
                });
            }
        }

        // 6. Session/document-level CV class suggestion.
        if ((isCvMode || cvSectionCount + headingPromotions >= 2) && cvSectionCount >= 1)
        {
            findings.Insert(0, new ImportStructuralFinding
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                DocumentId = documentId,
                Status = "pending",
                Kind = "cv_class_suggestion",
                BlockId = null,
                Title = $"This document looks like a CV ({cvSectionCount} CV sections + {headingPromotions} heading-like paragraphs)",
                Detail = "A CV-aware document class (moderncv, altacv) gives better LaTeX output than article — per-entry layout, contact macros, typography tuned for CVs.",
                SuggestedAction = "Use moderncv class on finalize",
                ActionKind = "set_document_class",
                ActionPayload = JsonDocument.Parse(JsonSerializer.Serialize(new { documentClass = "moderncv" })),
            });
        }

        return findings;
    }

    // ─── CV keyword tables + matching ────────────────────────────────────

    private static readonly Dictionary<string, string[]> CvSectionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["experience"] = new[]
        {
            "Experience", "Work Experience", "Professional Experience", "Employment",
            "Employment History", "Career History", "Work History",
            "Experience Professionnelle", "Expérience", "Expérience Professionnelle",
            "Parcours Professionnel",
            "Resume Experiences", "Résumé Experiences", "Résume Experiences",
            "Berufserfahrung", "Berufliche Erfahrung",
            "Experiencia", "Experiencia Profesional", "Experiência", "Esperienza",
        },
        ["education"] = new[]
        {
            "Education", "Academic Background", "Academic Education", "Qualifications",
            "Formation", "Études", "Etudes", "Formation Académique",
            "Parcours Universitaire",
            "Ausbildung", "Bildung", "Studium",
            "Educación", "Formación Académica", "Educação", "Formazione",
        },
        ["skills"] = new[]
        {
            "Skills", "Technical Skills", "Core Competencies", "Key Skills",
            "Expertise", "Areas of Expertise",
            "Compétences", "Compétences Techniques", "Competences Techniques",
            "Savoir-faire",
            "Fähigkeiten", "Kenntnisse", "Kompetenzen",
            "Habilidades", "Competencias", "Competências", "Competenze",
        },
        ["languages"] = new[]
        {
            "Languages", "Language Skills", "Langues", "Sprachen", "Idiomas", "Lingue",
        },
        ["projects"] = new[]
        {
            "Projects", "Personal Projects", "Side Projects",
            "Projets", "Projekte", "Proyectos", "Projetos", "Progetti",
        },
        ["publications"] = new[]
        {
            "Publications", "Papers", "Articles",
            "Veröffentlichungen",
            "Publicaciones", "Publicações", "Pubblicazioni",
        },
        ["certifications"] = new[]
        {
            "Certifications", "Licenses", "Certificates",
            "Zertifikate", "Zertifizierungen",
            "Certificaciones", "Certificações", "Certificazioni",
        },
        ["awards"] = new[]
        {
            "Awards", "Honors", "Achievements", "Distinctions",
            "Auszeichnungen", "Premios", "Prêmios", "Premi",
        },
        ["volunteering"] = new[]
        {
            "Volunteering", "Volunteer Experience", "Bénévolat", "Ehrenamt",
            "Voluntariado", "Volontariato",
        },
        ["references"] = new[]
        {
            "References", "Références", "Referenzen", "Referencias", "Referenze",
        },
        ["summary"] = new[]
        {
            "Summary", "Profile", "About Me", "Resume", "Résumé",
            "Profil", "Zusammenfassung", "Perfil", "Perfil Profesional",
        },
    };

    private static readonly Dictionary<string, string> CvNormalisedLookup = BuildLookup();

    private static Dictionary<string, string> BuildLookup()
    {
        var d = new Dictionary<string, string>();
        foreach (var (cat, variants) in CvSectionKeywords)
            foreach (var v in variants)
                d.TryAdd(Normalise(v), cat);
        return d;
    }

    private static string Normalise(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var formD = input.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", " ");
    }

    private static string? MatchCvSection(string text)
    {
        var n = Normalise(text);
        if (string.IsNullOrEmpty(n)) return null;
        return CvNormalisedLookup.TryGetValue(n, out var c) ? c : null;
    }

    // ─── Contact-info extraction ─────────────────────────────────────────

    private static readonly Regex EmailRegex = new(@"\b[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"(?:\+?\d{1,3}[\s\-.]?)?(?:\(\d{1,4}\)|\d{1,4})[\s\-.]?\d{2,4}[\s\-.]?\d{2,4}[\s\-.]?\d{0,4}", RegexOptions.Compiled);
    private static readonly Regex LinkedInRegex = new(@"(?:https?://)?(?:www\.)?linkedin\.com/in/[\w\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GithubRegex = new(@"(?:https?://)?(?:www\.)?github\.com/[\w\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UrlRegex = new(@"https?://[^\s]+", RegexOptions.Compiled);

    private record struct Contact(string Kind, string Value);

    private static List<Contact> ExtractContacts(string text)
    {
        var found = new List<Contact>();
        if (EmailRegex.Match(text) is { Success: true } em) found.Add(new("email", em.Value));
        if (LinkedInRegex.Match(text) is { Success: true } li) found.Add(new("linkedin", li.Value));
        if (GithubRegex.Match(text) is { Success: true } gh) found.Add(new("github", gh.Value));
        if (found.Count > 0 && PhoneRegex.Match(text) is { Success: true } ph && ph.Value.Length >= 7)
            found.Add(new("phone", ph.Value));
        var url = UrlRegex.Match(text);
        if (url.Success && !url.Value.Contains("linkedin.com") && !url.Value.Contains("github.com"))
            found.Add(new("website", url.Value));
        return found;
    }

    // ─── Heading heuristic ───────────────────────────────────────────────

    private const int MaxHeadingLikeLength = 60;
    private static readonly Regex TrailingPunctuation = new(@"[\.\!\?…:]$", RegexOptions.Compiled);

    private static bool LooksLikeHeading(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length is > MaxHeadingLikeLength or < 2) return false;
        if (TrailingPunctuation.IsMatch(t)) return false;
        return true;
    }

    // ─── JSON helpers ────────────────────────────────────────────────────

    private static string FlattenTableCells(JsonDocument? content)
    {
        if (content is null) return "";
        var sb = new StringBuilder();
        if (content.RootElement.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                foreach (var cell in row.EnumerateArray())
                    if (cell.ValueKind == JsonValueKind.String)
                        sb.Append(cell.GetString()).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static bool IsContentEmpty(JsonDocument? content)
    {
        if (content is null) return true;
        var txt = content.RootElement.GetRawText();
        return txt == "{}" || string.IsNullOrWhiteSpace(txt);
    }

    private static string? GetStringField(JsonDocument? content, string field)
    {
        if (content is null) return null;
        if (!content.RootElement.TryGetProperty(field, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }
}
