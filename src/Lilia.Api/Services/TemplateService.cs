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
        var query = _context.Templates
            .Include(t => t.User)
            .Where(t => t.IsSystem || t.IsPublic || t.UserId == userId);

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(t => t.Category == category);
        }

        var templates = await query
            .OrderByDescending(t => t.IsSystem)
            .ThenByDescending(t => t.UsageCount)
            .ThenBy(t => t.Name)
            .ToListAsync();

        return templates.Select(t => new TemplateListDto(
            t.Id,
            t.Name,
            t.Description,
            t.Category,
            t.Thumbnail,
            t.IsPublic,
            t.IsSystem,
            t.UsageCount,
            t.UserId,
            t.User?.Name,
            t.CreatedAt
        )).ToList();
    }

    public async Task<TemplateDto?> GetTemplateAsync(Guid templateId, string userId)
    {
        var template = await _context.Templates
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Id == templateId);

        if (template == null) return null;

        // Check access
        if (!template.IsSystem && !template.IsPublic && template.UserId != userId)
            return null;

        return MapToDto(template);
    }

    public async Task<TemplateDto> CreateTemplateAsync(string userId, CreateTemplateDto dto)
    {
        var document = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == dto.DocumentId);

        if (document == null)
            throw new ArgumentException("Document not found");

        // Create content from document
        var content = new
        {
            language = document.Language,
            paperSize = document.PaperSize,
            fontFamily = document.FontFamily,
            fontSize = document.FontSize,
            blocks = document.Blocks.Select(b => new
            {
                type = b.Type,
                content = b.Content.RootElement,
                sortOrder = b.SortOrder,
                depth = b.Depth
            }),
            bibliography = document.BibliographyEntries.Select(e => new
            {
                citeKey = e.CiteKey,
                entryType = e.EntryType,
                data = e.Data.RootElement
            })
        };

        var template = new Template
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Description = dto.Description,
            Category = dto.Category,
            Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
            IsPublic = dto.IsPublic,
            IsSystem = false,
            UsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Templates.Add(template);
        await _context.SaveChangesAsync();

        return MapToDto(template);
    }

    public async Task<TemplateDto?> UpdateTemplateAsync(Guid templateId, string userId, UpdateTemplateDto dto)
    {
        var template = await _context.Templates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.UserId == userId);

        if (template == null) return null;

        if (dto.Name != null) template.Name = dto.Name;
        if (dto.Description != null) template.Description = dto.Description;
        if (dto.Category != null) template.Category = dto.Category;
        if (dto.IsPublic.HasValue) template.IsPublic = dto.IsPublic.Value;

        template.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return MapToDto(template);
    }

    public async Task<bool> DeleteTemplateAsync(Guid templateId, string userId)
    {
        var template = await _context.Templates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.UserId == userId && !t.IsSystem);

        if (template == null) return false;

        _context.Templates.Remove(template);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<DocumentDto> UseTemplateAsync(Guid templateId, string userId, UseTemplateDto dto)
    {
        var template = await _context.Templates.FindAsync(templateId);
        if (template == null)
            throw new ArgumentException("Template not found");

        var createDto = new CreateDocumentDto(
            dto.Title ?? template.Name,
            null,
            null,
            null,
            null,
            null,
            templateId
        );

        return await _documentService.CreateDocumentAsync(userId, createDto);
    }

    public async Task<List<TemplateCategoryDto>> GetCategoriesAsync()
    {
        var categories = await _context.Templates
            .Where(t => t.Category != null)
            .GroupBy(t => t.Category)
            .Select(g => new TemplateCategoryDto(g.Key!, g.Count()))
            .ToListAsync();

        return categories;
    }

    private static TemplateDto MapToDto(Template t)
    {
        return new TemplateDto(
            t.Id,
            t.Name,
            t.Description,
            t.Category,
            t.Thumbnail,
            t.Content.RootElement,
            t.IsPublic,
            t.IsSystem,
            t.UsageCount,
            t.UserId,
            t.User?.Name,
            t.CreatedAt,
            t.UpdatedAt
        );
    }
}
