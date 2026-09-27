using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class TemplateService : ITemplateService
{
    private readonly LiliaDbContext _context;
    private readonly IDocumentService _documentService;

    public TemplateService(LiliaDbContext context, IDocumentService documentService)
    {
        _context = context;
        _documentService = documentService;
    }

    public async Task<List<TemplateListDto>> GetTemplatesAsync(string userId, string? category = null)
    {
        var query = _context.Documents
            .AsNoTracking()
            .Include(d => d.Owner)
            .Where(d => d.IsTemplate)
            .Where(d => d.IsPublicTemplate || d.OwnerId == userId);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(d => d.TemplateCategory == category);
        }

        var docs = await query
            .OrderByDescending(d => d.OwnerId == "system")
            .ThenByDescending(d => d.TemplateUsageCount)
            .ThenBy(d => d.TemplateName)
            .ToListAsync();

        return docs.Select(d => new TemplateListDto(
            d.Id,
            d.TemplateName ?? d.Title,
            d.TemplateDescription,
            d.TemplateCategory,
            d.TemplateThumbnail,
            d.IsPublicTemplate,
            d.OwnerId == "system",
            d.TemplateUsageCount,
            d.OwnerId,
            d.Owner?.Name,
            d.CreatedAt
        )).ToList();
    }

    public async Task<TemplateDto?> GetTemplateAsync(Guid templateId, string userId)
    {
        var doc = await _context.Documents
            .AsNoTracking()
            .Include(d => d.Owner)
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .FirstOrDefaultAsync(d => d.Id == templateId && d.IsTemplate);

        if (doc == null || !IsVisibleTo(doc, userId)) return null;

        // Build content from blocks (for backward compat with frontend)
        var blocksJson = doc.Blocks.Select(b => new
        {
            type = b.Type,
            content = b.Content.RootElement,
            sortOrder = b.SortOrder,
            depth = b.Depth
        });

        var content = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            language = doc.Language,
            paperSize = doc.PaperSize,
            fontFamily = doc.FontFamily,
            fontSize = doc.FontSize,
            columns = doc.Columns,
            blocks = blocksJson
        }));

        return new TemplateDto(
            doc.Id,
            doc.TemplateName ?? doc.Title,
            doc.TemplateDescription,
            doc.TemplateCategory,
            doc.TemplateThumbnail,
            content.RootElement,
            doc.IsPublicTemplate,
            doc.OwnerId == "system",
            doc.TemplateUsageCount,
            doc.OwnerId,
            doc.Owner?.Name,
            doc.CreatedAt,
            doc.UpdatedAt
        );
    }

    public async Task<TemplateDto?> CreateTemplateAsync(string userId, CreateTemplateDto dto)
    {
        // A template is a copy of the document, so it takes what Duplicate
        // takes: permission to read it. The owner and every collaborator —
        // viewers included — may; anyone else finds no such document.
        if (!await _documentService.HasAccessAsync(dto.DocumentId, userId, Permissions.Read))
            return null;

        var source = await _context.Documents
            .Include(d => d.Blocks)
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == dto.DocumentId);

        if (source == null) return null;

        // The same rule as Duplicate: content, class, columns, margins and
        // preamble come along; collaborators, team and sharing never do
        // (Olivia, templates handoff 27 Sep §2). It belongs to its maker.
        var templateDoc = DocumentCopy.Of(_context, source, userId, dto.Name);
        templateDoc.IsTemplate = true;
        templateDoc.TemplateName = dto.Name;
        templateDoc.TemplateDescription = dto.Description;
        templateDoc.TemplateCategory = dto.Category;
        templateDoc.IsPublicTemplate = dto.IsPublic;
        templateDoc.TemplateUsageCount = 0;

        _context.Documents.Add(templateDoc);
        await _context.SaveChangesAsync();

        return (await GetTemplateAsync(templateDoc.Id, userId))!;
    }

    public async Task<TemplateDto?> UpdateTemplateAsync(Guid templateId, string userId, UpdateTemplateDto dto)
    {
        var doc = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == templateId && d.IsTemplate && d.OwnerId == userId);

        if (doc == null) return null;

        if (dto.Name != null) { doc.TemplateName = dto.Name; doc.Title = dto.Name; }
        if (dto.Description != null) doc.TemplateDescription = dto.Description;
        if (dto.Category != null) doc.TemplateCategory = dto.Category;
        if (dto.IsPublic.HasValue) doc.IsPublicTemplate = dto.IsPublic.Value;

        doc.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return await GetTemplateAsync(templateId, userId);
    }

    public async Task<bool> DeleteTemplateAsync(Guid templateId, string userId)
    {
        var doc = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == templateId && d.IsTemplate && d.OwnerId == userId && d.OwnerId != "system");

        if (doc == null) return false;

        // Delete blocks first
        var blocks = await _context.Blocks.Where(b => b.DocumentId == templateId).ToListAsync();
        _context.Blocks.RemoveRange(blocks);
        _context.Documents.Remove(doc);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<DocumentDto?> UseTemplateAsync(Guid templateId, string userId, UseTemplateDto dto)
    {
        var template = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == templateId && d.IsTemplate);

        // Using a template copies its content, so it is visible on the same
        // terms as reading it: someone else's private template is not found.
        if (template == null || !IsVisibleTo(template, userId)) return null;

        // A new document, on the same rule as Duplicate: the template's
        // settings and content, owned by whoever used it.
        var newDoc = DocumentCopy.Of(_context, template, userId, dto.Title ?? template.TemplateName ?? template.Title);
        _context.Documents.Add(newDoc);

        // Increment usage count
        template.TemplateUsageCount++;
        await _context.SaveChangesAsync();

        return (await _documentService.GetDocumentAsync(newDoc.Id, userId))!;
    }

    /// <summary>
    /// Who may see a template — and so read it or use it: its owner, anyone
    /// when it is public, and everyone for a system template.
    /// </summary>
    private static bool IsVisibleTo(Document template, string userId) =>
        template.IsPublicTemplate || template.OwnerId == userId || template.OwnerId == "system";

    public async Task<List<TemplateCategoryDto>> GetCategoriesAsync()
    {
        var categories = await _context.Documents
            .Where(d => d.IsTemplate && d.TemplateCategory != null)
            .GroupBy(d => d.TemplateCategory)
            .Select(g => new TemplateCategoryDto(g.Key!, g.Count()))
            .ToListAsync();

        return categories;
    }
}
