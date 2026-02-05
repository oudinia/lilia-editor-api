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

    public DocumentService(LiliaDbContext context)
    {
        _context = context;
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

        var items = documents.Select(d => new DocumentListDto(
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
            )).ToList()
        )).ToList();

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
            .FirstOrDefaultAsync(d => d.Id == id);

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

        // If creating from template, copy content
        if (dto.TemplateId.HasValue)
        {
            var template = await _context.Templates.FindAsync(dto.TemplateId.Value);
            if (template != null)
            {
                // Parse template content and create blocks
                var content = template.Content.RootElement;
                if (content.TryGetProperty("blocks", out var blocksElement) && blocksElement.ValueKind == JsonValueKind.Array)
                {
                    int sortOrder = 0;
                    foreach (var blockElement in blocksElement.EnumerateArray())
                    {
                        var block = new Block
                        {
                            Id = Guid.NewGuid(),
                            DocumentId = document.Id,
                            Type = blockElement.GetProperty("type").GetString() ?? "paragraph",
                            Content = JsonDocument.Parse(blockElement.GetProperty("content").GetRawText()),
                            SortOrder = sortOrder++,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        document.Blocks.Add(block);
                    }
                }

                template.UsageCount++;
            }
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
        }

        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new DocumentShareResultDto(document.ShareLink ?? "", document.IsPublic);
    }

    public async Task<bool> RevokeShareAsync(Guid id, string userId)
    {
        var document = await _context.Documents.FindAsync(id);
        if (document == null) return false;

        if (!await HasAccessAsync(id, userId, Permissions.Manage))
            return false;

        document.IsPublic = false;
        document.ShareLink = null;
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

    private static string GenerateShareLink()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
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
            d.IsPublic,
            d.ShareLink,
            d.CreatedAt,
            d.UpdatedAt,
            d.LastOpenedAt,
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
