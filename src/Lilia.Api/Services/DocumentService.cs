using System.Security.Cryptography;
using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class DocumentService : IDocumentService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(LiliaDbContext context, ILogger<DocumentService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PaginatedResult<DocumentListDto>> GetDocumentsPaginatedAsync(
        string userId,
        int page = 1,
        int pageSize = 20,
        string? search = null,
        Guid? labelId = null,
        string sortBy = "updatedAt",
        string sortDir = "desc")
    {
        // Ensure valid pagination params
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Documents
            .Include(d => d.Owner)
            .Include(d => d.Team)
            .Include(d => d.DocumentLabels)
                .ThenInclude(dl => dl.Label)
            .Where(d => d.DeletedAt == null)
            .Where(d => !d.IsTemplate)
            .Where(d => d.OwnerId == userId ||
                        d.Collaborators.Any(c => c.UserId == userId) ||
                        d.DocumentGroups.Any(dg => dg.Group.Members.Any(m => m.UserId == userId)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(d => d.Title.ToLower().Contains(search.ToLower()));
        }

        if (labelId.HasValue)
        {
            query = query.Where(d => d.DocumentLabels.Any(dl => dl.LabelId == labelId.Value));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = (sortBy.ToLower(), sortDir.ToLower()) switch
        {
            ("title", "asc") => query.OrderBy(d => d.Title),
            ("title", _) => query.OrderByDescending(d => d.Title),
            ("createdat", "asc") => query.OrderBy(d => d.CreatedAt),
            ("createdat", _) => query.OrderByDescending(d => d.CreatedAt),
            ("updatedat", "asc") => query.OrderBy(d => d.UpdatedAt),
            (_, "asc") => query.OrderBy(d => d.LastOpenedAt ?? d.UpdatedAt),
            _ => query.OrderByDescending(d => d.LastOpenedAt ?? d.UpdatedAt)
        };

        // Apply pagination
        var documents = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Get block counts for these documents
        var documentIds = documents.Select(d => d.Id).ToList();
        var blockCounts = await _context.Blocks
            .Where(b => documentIds.Contains(b.DocumentId))
            .GroupBy(b => b.DocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Count);

        // Get section counts and outlines (heading blocks)
        var headingBlocks = await _context.Blocks
            .Where(b => documentIds.Contains(b.DocumentId) && b.Type == BlockTypes.Heading)
            .OrderBy(b => b.SortOrder)
            .Select(b => new { b.DocumentId, b.Content })
            .ToListAsync();

        var sectionCounts = headingBlocks
            .GroupBy(b => b.DocumentId)
            .ToDictionary(g => g.Key, g => g.Count());

        var outlines = headingBlocks
            .GroupBy(b => b.DocumentId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(b => ExtractOutlineItem(b.Content)).Where(o => o != null).ToList()
            );

        // Determine user's role per document
        var collaboratorRoles = await _context.DocumentCollaborators
            .Include(dc => dc.Role)
            .Where(dc => documentIds.Contains(dc.DocumentId) && dc.UserId == userId)
            .ToDictionaryAsync(dc => dc.DocumentId, dc => dc.Role.Name);

        var items = documents.Select(d =>
        {
            string role;
            if (d.OwnerId == userId)
                role = "owner";
            else if (collaboratorRoles.TryGetValue(d.Id, out var roleName))
                role = roleName;
            else
                role = "viewer"; // group access fallback

            return new DocumentListDto(
                d.Id,
                d.Title,
                d.OwnerId,
                d.Owner?.Name,
                d.TeamId,
                d.Team?.Name,
                d.CreatedAt,
                d.UpdatedAt,
                d.LastOpenedAt,
                blockCounts.GetValueOrDefault(d.Id, 0),
                sectionCounts.GetValueOrDefault(d.Id, 0),
                outlines.GetValueOrDefault(d.Id, new List<OutlineItemDto?>())!.Where(o => o != null).Select(o => o!).ToList(),
                d.DocumentLabels.Select(dl => new LabelDto(
                    dl.Label.Id,
                    dl.Label.Name,
                    dl.Label.Color,
                    dl.Label.CreatedAt
                )).ToList(),
                role,
                d.ValidationErrorCount,
                d.ValidationWarningCount,
                d.ValidationCheckedAt
            );
        }).ToList();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new PaginatedResult<DocumentListDto>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<List<DocumentListDto>> GetDocumentsAsync(string userId, string? search = null, Guid? labelId = null)
    {
        // Legacy method - returns all documents without pagination
        var result = await GetDocumentsPaginatedAsync(userId, 1, 1000, search, labelId);
        return result.Items;
    }

    public async Task<DocumentDto?> GetDocumentAsync(Guid id, string userId)
    {
        var document = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .Include(d => d.DocumentLabels)
                .ThenInclude(dl => dl.Label)
            .FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt == null);

        if (document == null) return null;

        if (!await HasAccessAsync(id, userId, Permissions.Read))
            return null;

        // Update last opened
        document.LastOpenedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return MapToDto(document);
    }

    public async Task<DocumentDto?> GetSharedDocumentAsync(string shareLink)
    {
        var document = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .Include(d => d.DocumentLabels)
                .ThenInclude(dl => dl.Label)
            .FirstOrDefaultAsync(d => d.ShareLink == shareLink && d.IsPublic);

        if (document == null) return null;

        return MapToDto(document);
    }

    public async Task<DocumentDto> CreateDocumentAsync(string userId, CreateDocumentDto dto)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            TeamId = dto.TeamId,
            Title = dto.Title ?? "Untitled",
            Language = dto.Language ?? "en",
            PaperSize = dto.PaperSize ?? "a4",
            FontFamily = dto.FontFamily ?? "serif",
            FontSize = dto.FontSize ?? 12,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // If creating from template, copy blocks from template document
        if (dto.TemplateId.HasValue)
        {
            var templateDoc = await _context.Documents
                .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
                .FirstOrDefaultAsync(d => d.Id == dto.TemplateId.Value && d.IsTemplate);

            if (templateDoc != null)
            {
                // Copy document settings
                document.Language = templateDoc.Language;
                document.PaperSize = templateDoc.PaperSize;
                document.FontFamily = templateDoc.FontFamily;
                document.FontSize = templateDoc.FontSize;
                document.Columns = templateDoc.Columns;
                document.ColumnSeparator = templateDoc.ColumnSeparator;
                document.ColumnGap = templateDoc.ColumnGap;

                // Copy blocks
                foreach (var srcBlock in templateDoc.Blocks)
                {
                    document.Blocks.Add(new Block
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = document.Id,
                        Type = srcBlock.Type,
                        Content = JsonDocument.Parse(srcBlock.Content.RootElement.GetRawText()),
                        SortOrder = srcBlock.SortOrder,
                        Depth = srcBlock.Depth,
                        Path = srcBlock.Path,
                        Status = "draft",
                        Metadata = JsonDocument.Parse("{}"),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                templateDoc.TemplateUsageCount++;
            }
        }

        // Ensure every new document has at least one paragraph block
        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Block
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                Type = "paragraph",
                Content = JsonDocument.Parse("\"\""),
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        _context.Documents.Add(document);
        await _context.SaveChangesAsync();

        return (await GetDocumentAsync(document.Id, userId))!;
    }

    public async Task<DocumentDto?> UpdateDocumentAsync(Guid id, string userId, UpdateDocumentDto dto)
    {
        var document = await _context.Documents.FindAsync(id);
        if (document == null) return null;

        if (!await HasAccessAsync(id, userId, Permissions.Write))
            return null;

        if (dto.Title != null) document.Title = dto.Title;
        if (dto.Language != null) document.Language = dto.Language;
        if (dto.PaperSize != null) document.PaperSize = dto.PaperSize;
        if (dto.FontFamily != null) document.FontFamily = dto.FontFamily;
        if (dto.FontSize.HasValue) document.FontSize = dto.FontSize.Value;
        if (dto.Columns.HasValue) document.Columns = Math.Clamp(dto.Columns.Value, 1, 3);
        if (dto.ColumnSeparator != null) document.ColumnSeparator = dto.ColumnSeparator;
        if (dto.ColumnGap.HasValue) document.ColumnGap = dto.ColumnGap.Value;
        if (dto.MarginTop != null) document.MarginTop = dto.MarginTop;
        if (dto.MarginBottom != null) document.MarginBottom = dto.MarginBottom;
        if (dto.MarginLeft != null) document.MarginLeft = dto.MarginLeft;
        if (dto.MarginRight != null) document.MarginRight = dto.MarginRight;
        if (dto.HeaderText != null) document.HeaderText = dto.HeaderText;
        if (dto.FooterText != null) document.FooterText = dto.FooterText;
        if (dto.LineSpacing.HasValue) document.LineSpacing = dto.LineSpacing.Value;
        if (dto.ParagraphIndent != null) document.ParagraphIndent = dto.ParagraphIndent;
        if (dto.PageNumbering != null) document.PageNumbering = dto.PageNumbering;

        document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return await GetDocumentAsync(id, userId);
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, string userId)
    {
        var document = await _context.Documents.FindAsync(id);
        if (document == null) return false;

        if (!await HasAccessAsync(id, userId, Permissions.Delete))
            return false;

        document.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<DocumentDto?> DuplicateDocumentAsync(Guid id, string userId)
    {
        var original = await _context.Documents
            .Include(d => d.Blocks)
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (original == null) return null;

        if (!await HasAccessAsync(id, userId, Permissions.Read))
            return null;

        var newDoc = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            TeamId = original.TeamId,
            Title = $"{original.Title} (Copy)",
            Language = original.Language,
            PaperSize = original.PaperSize,
            FontFamily = original.FontFamily,
            FontSize = original.FontSize,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Copy blocks
        foreach (var block in original.Blocks.OrderBy(b => b.SortOrder))
        {
            newDoc.Blocks.Add(new Block
            {
                Id = Guid.NewGuid(),
                DocumentId = newDoc.Id,
                Type = block.Type,
                Content = JsonDocument.Parse(block.Content.RootElement.GetRawText()),
                SortOrder = block.SortOrder,
                Depth = block.Depth,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Copy bibliography
        foreach (var entry in original.BibliographyEntries)
        {
            newDoc.BibliographyEntries.Add(new BibliographyEntry
            {
                Id = Guid.NewGuid(),
                DocumentId = newDoc.Id,
                CiteKey = entry.CiteKey,
                EntryType = entry.EntryType,
                Data = JsonDocument.Parse(entry.Data.RootElement.GetRawText()),
                FormattedText = entry.FormattedText,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        _context.Documents.Add(newDoc);
        await _context.SaveChangesAsync();

        return await GetDocumentAsync(newDoc.Id, userId);
    }

    public async Task<DocumentShareResultDto?> ShareDocumentAsync(Guid id, string userId, bool isPublic)
    {
        var document = await _context.Documents.FindAsync(id);
        if (document == null) return null;

        if (!await HasAccessAsync(id, userId, Permissions.Manage))
            return null;

        document.IsPublic = isPublic;
        if (isPublic && string.IsNullOrEmpty(document.ShareLink))
        {
            document.ShareLink = GenerateShareLink();
            document.ShareSlug = GenerateSlug(document.Title);
        }

        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new DocumentShareResultDto(document.ShareLink ?? "", document.ShareSlug, document.IsPublic);
    }

    public async Task<bool> RevokeShareAsync(Guid id, string userId)
    {
        var document = await _context.Documents.FindAsync(id);
        if (document == null) return false;

        if (!await HasAccessAsync(id, userId, Permissions.Manage))
            return false;

        document.IsPublic = false;
        document.ShareLink = null;
        document.ShareSlug = null;
        document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> HasAccessAsync(Guid documentId, string userId, string requiredPermission)
    {
        var document = await _context.Documents
            .Include(d => d.Collaborators)
                .ThenInclude(c => c.Role)
            .Include(d => d.DocumentGroups)
                .ThenInclude(dg => dg.Role)
            .Include(d => d.DocumentGroups)
                .ThenInclude(dg => dg.Group)
                    .ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null) return false;

        // Owner always has full access
        if (document.OwnerId == userId) return true;

        // Check direct collaborator
        var collaborator = document.Collaborators.FirstOrDefault(c => c.UserId == userId);
        if (collaborator != null && collaborator.Role.Permissions.Contains(requiredPermission))
            return true;

        // Check group access
        foreach (var docGroup in document.DocumentGroups)
        {
            if (docGroup.Group.Members.Any(m => m.UserId == userId) &&
                docGroup.Role.Permissions.Contains(requiredPermission))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<PaginatedResult<TrashDocumentDto>> GetTrashDocumentsPaginatedAsync(string userId, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Documents.IgnoreQueryFilters()
            .Where(d => d.DeletedAt != null && d.OwnerId == userId)
            .OrderByDescending(d => d.DeletedAt);

        var totalCount = await query.CountAsync();

        var documents = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = documents.Select(d =>
        {
            var daysSinceDeletion = (int)(DateTime.UtcNow - d.DeletedAt!.Value).TotalDays;
            var daysUntilPurge = Math.Max(0, 30 - daysSinceDeletion);

            return new TrashDocumentDto(
                d.Id,
                d.Title,
                d.OwnerId,
                d.CreatedAt,
                d.UpdatedAt,
                d.DeletedAt.Value,
                daysUntilPurge
            );
        }).ToList();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new PaginatedResult<TrashDocumentDto>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<bool> RestoreDocumentAsync(Guid id, string userId)
    {
        var document = await _context.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt != null);
        if (document == null) return false;

        if (document.OwnerId != userId) return false;

        document.DeletedAt = null;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> PermanentDeleteDocumentAsync(Guid id, string userId)
    {
        var document = await _context.Documents.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt != null);
        if (document == null) return false;

        if (document.OwnerId != userId) return false;

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<int> PurgeExpiredDocumentsAsync(int retentionDays = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var expiredDocuments = await _context.Documents.IgnoreQueryFilters()
            .Where(d => d.DeletedAt != null && d.DeletedAt < cutoff)
            .ToListAsync();

        if (expiredDocuments.Count == 0) return 0;

        _context.Documents.RemoveRange(expiredDocuments);
        await _context.SaveChangesAsync();

        return expiredDocuments.Count;
    }

    public async Task<int> CloneStarterDocumentsAsync(string userId)
    {
        const string sampleUserId = "sample-content";

        var sampleDocs = await _context.Documents
            .Include(d => d.Blocks)
            .Include(d => d.BibliographyEntries)
            .Where(d => d.OwnerId == sampleUserId && d.DeletedAt == null)
            .AsNoTracking()
            .ToListAsync();

        if (sampleDocs.Count == 0) return 0;

        foreach (var original in sampleDocs)
        {
            var newDoc = new Document
            {
                Id = Guid.NewGuid(),
                OwnerId = userId,
                Title = original.Title,
                Language = original.Language,
                PaperSize = original.PaperSize,
                FontFamily = original.FontFamily,
                FontSize = original.FontSize,
                Columns = original.Columns,
                ColumnGap = original.ColumnGap,
                ColumnSeparator = original.ColumnSeparator,
                LineSpacing = original.LineSpacing,
                ParagraphIndent = original.ParagraphIndent,
                MarginTop = original.MarginTop,
                MarginBottom = original.MarginBottom,
                MarginLeft = original.MarginLeft,
                MarginRight = original.MarginRight,
                HeaderText = original.HeaderText,
                FooterText = original.FooterText,
                PageNumbering = original.PageNumbering,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            foreach (var block in original.Blocks.OrderBy(b => b.SortOrder))
            {
                newDoc.Blocks.Add(new Block
                {
                    Id = Guid.NewGuid(),
                    DocumentId = newDoc.Id,
                    Type = block.Type,
                    Content = JsonDocument.Parse(block.Content.RootElement.GetRawText()),
                    SortOrder = block.SortOrder,
                    Depth = block.Depth,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            foreach (var entry in original.BibliographyEntries)
            {
                newDoc.BibliographyEntries.Add(new BibliographyEntry
                {
                    Id = Guid.NewGuid(),
                    DocumentId = newDoc.Id,
                    CiteKey = entry.CiteKey,
                    EntryType = entry.EntryType,
                    Data = JsonDocument.Parse(entry.Data.RootElement.GetRawText()),
                    FormattedText = entry.FormattedText,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            _context.Documents.Add(newDoc);
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("[Onboarding] Cloned {Count} starter documents for user {UserId}",
            sampleDocs.Count, userId);

        return sampleDocs.Count;
    }

    private static string GenerateShareLink()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static string GenerateSlug(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title == "Untitled")
            return "document";

        // Normalize unicode, strip diacritics (é → e, ü → u)
        var normalized = title.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = char.GetUnicodeCategory(c);
            if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var slug = sb.ToString().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();

        // Replace non-alphanumeric with hyphens
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9]+", "-");

        // Trim hyphens and truncate
        slug = slug.Trim('-');
        if (slug.Length > 60)
            slug = slug[..60].TrimEnd('-');

        return string.IsNullOrEmpty(slug) ? "document" : slug;
    }

    private static DocumentDto MapToDto(Document d)
    {
        return new DocumentDto(
            d.Id,
            d.Title,
            d.OwnerId,
            d.TeamId,
            d.Language,
            d.PaperSize,
            d.FontFamily,
            d.FontSize,
            d.Columns,
            d.ColumnSeparator,
            d.ColumnGap,
            d.IsPublic,
            d.ShareLink,
            d.ShareSlug,
            d.CreatedAt,
            d.UpdatedAt,
            d.LastOpenedAt,
            d.MarginTop,
            d.MarginBottom,
            d.MarginLeft,
            d.MarginRight,
            d.HeaderText,
            d.FooterText,
            d.LineSpacing,
            d.ParagraphIndent,
            d.PageNumbering,
            d.Blocks.Select(b => new BlockDto(
                b.Id,
                b.DocumentId,
                b.Type,
                b.Content.RootElement,
                b.SortOrder,
                b.ParentId,
                b.Depth,
                b.CreatedAt,
                b.UpdatedAt
            )).ToList(),
            d.BibliographyEntries.Select(e => new BibliographyEntryDto(
                e.Id,
                e.DocumentId,
                e.CiteKey,
                e.EntryType,
                e.Data.RootElement,
                e.FormattedText,
                e.CreatedAt,
                e.UpdatedAt
            )).ToList(),
            d.DocumentLabels.Select(dl => new LabelDto(
                dl.Label.Id,
                dl.Label.Name,
                dl.Label.Color,
                dl.Label.CreatedAt
            )).ToList()
        );
    }

    private static OutlineItemDto? ExtractOutlineItem(JsonDocument content)
    {
        try
        {
            var root = content.RootElement;

            // Try to get text from heading block content
            // Format: { "text": "...", "level": 1 } or { "content": [{ "text": "..." }], "level": 1 }
            string? text = null;
            int level = 1;

            if (root.TryGetProperty("level", out var levelProp))
            {
                level = levelProp.GetInt32();
            }

            if (root.TryGetProperty("text", out var textProp))
            {
                text = textProp.GetString();
            }
            else if (root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.Array)
            {
                // ProseMirror-style content array
                var parts = new List<string>();
                foreach (var node in contentProp.EnumerateArray())
                {
                    if (node.TryGetProperty("text", out var nodeTxt))
                    {
                        parts.Add(nodeTxt.GetString() ?? "");
                    }
                }
                text = string.Join("", parts);
            }

            if (string.IsNullOrWhiteSpace(text))
                return null;

            return new OutlineItemDto(text.Trim(), level);
        }
        catch
        {
            return null;
        }
    }
}
