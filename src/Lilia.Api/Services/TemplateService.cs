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

        if (doc == null) return null;
        if (!doc.IsPublicTemplate && doc.OwnerId != userId && doc.OwnerId != "system")
            return null;

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

    public async Task<TemplateDto> CreateTemplateAsync(string userId, CreateTemplateDto dto)
    {
        // Load the source document with blocks
        var source = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .FirstOrDefaultAsync(d => d.Id == dto.DocumentId);

        if (source == null)
            throw new ArgumentException("Document not found");

        // Create a new document as template (copy)
        var templateDoc = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            Title = dto.Name,
            Language = source.Language,
            PaperSize = source.PaperSize,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            Columns = source.Columns,
            ColumnSeparator = source.ColumnSeparator,
            ColumnGap = source.ColumnGap,
            IsTemplate = true,
            TemplateName = dto.Name,
            TemplateDescription = dto.Description,
            TemplateCategory = dto.Category,
            IsPublicTemplate = dto.IsPublic,
            TemplateUsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _context.Documents.Add(templateDoc);

        // Copy blocks
        foreach (var block in source.Blocks)
        {
            _context.Blocks.Add(new Block
            {
                Id = Guid.NewGuid(),
                DocumentId = templateDoc.Id,
                Type = block.Type,
                Content = JsonDocument.Parse(block.Content.RootElement.GetRawText()),
                SortOrder = block.SortOrder,
                Depth = block.Depth,
                Path = block.Path,
                Status = "draft",
                Metadata = JsonDocument.Parse("{}"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

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

    public async Task<DocumentDto> UseTemplateAsync(Guid templateId, string userId, UseTemplateDto dto)
    {
        var template = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == templateId && d.IsTemplate);

        if (template == null)
            throw new ArgumentException("Template not found");

        // Create new document from template
        var newDoc = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            Title = dto.Title ?? template.TemplateName ?? template.Title,
            Language = template.Language,
            PaperSize = template.PaperSize,
            FontFamily = template.FontFamily,
            FontSize = template.FontSize,
            Columns = template.Columns,
            ColumnSeparator = template.ColumnSeparator,
            ColumnGap = template.ColumnGap,
            IsTemplate = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _context.Documents.Add(newDoc);

        // Copy blocks
        foreach (var block in template.Blocks)
        {
            _context.Blocks.Add(new Block
            {
                Id = Guid.NewGuid(),
                DocumentId = newDoc.Id,
                Type = block.Type,
                Content = JsonDocument.Parse(block.Content.RootElement.GetRawText()),
                SortOrder = block.SortOrder,
                Depth = block.Depth,
                Path = block.Path,
                Status = "draft",
                Metadata = JsonDocument.Parse("{}"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        // Increment usage count
        template.TemplateUsageCount++;
        await _context.SaveChangesAsync();

        return (await _documentService.GetDocumentAsync(newDoc.Id, userId))!;
    }

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
