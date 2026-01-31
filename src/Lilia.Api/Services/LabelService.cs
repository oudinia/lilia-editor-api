using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class LabelService : ILabelService
{
    private readonly LiliaDbContext _context;

    public LabelService(LiliaDbContext context)
    {
        _context = context;
    }

    public async Task<List<LabelDto>> GetLabelsAsync(string userId)
    {
        var labels = await _context.Labels
            .Where(l => l.UserId == userId)
            .OrderBy(l => l.Name)
            .ToListAsync();

        return labels.Select(MapToDto).ToList();
    }

    public async Task<LabelDto?> GetLabelAsync(string userId, Guid labelId)
    {
        var label = await _context.Labels
            .FirstOrDefaultAsync(l => l.Id == labelId && l.UserId == userId);

        return label == null ? null : MapToDto(label);
    }

    public async Task<LabelDto> CreateLabelAsync(string userId, CreateLabelDto dto)
    {
        var label = new Label
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Color = dto.Color,
            CreatedAt = DateTime.UtcNow
        };

        _context.Labels.Add(label);
        await _context.SaveChangesAsync();

        return MapToDto(label);
    }

    public async Task<LabelDto?> UpdateLabelAsync(string userId, Guid labelId, UpdateLabelDto dto)
    {
        var label = await _context.Labels
            .FirstOrDefaultAsync(l => l.Id == labelId && l.UserId == userId);

        if (label == null) return null;

        if (dto.Name != null) label.Name = dto.Name;
        if (dto.Color != null) label.Color = dto.Color;

        await _context.SaveChangesAsync();

        return MapToDto(label);
    }

    public async Task<bool> DeleteLabelAsync(string userId, Guid labelId)
    {
        var label = await _context.Labels
            .FirstOrDefaultAsync(l => l.Id == labelId && l.UserId == userId);

        if (label == null) return false;

        _context.Labels.Remove(label);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> AddLabelToDocumentAsync(Guid documentId, Guid labelId, string userId)
    {
        // Verify user owns the label
        var label = await _context.Labels
            .FirstOrDefaultAsync(l => l.Id == labelId && l.UserId == userId);
        if (label == null) return false;

        // Check if already exists
        var exists = await _context.DocumentLabels
            .AnyAsync(dl => dl.DocumentId == documentId && dl.LabelId == labelId);
        if (exists) return true;

        var documentLabel = new DocumentLabel
        {
            DocumentId = documentId,
            LabelId = labelId
        };

        _context.DocumentLabels.Add(documentLabel);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> RemoveLabelFromDocumentAsync(Guid documentId, Guid labelId, string userId)
    {
        // Verify user owns the label
        var label = await _context.Labels
            .FirstOrDefaultAsync(l => l.Id == labelId && l.UserId == userId);
        if (label == null) return false;

        var documentLabel = await _context.DocumentLabels
            .FirstOrDefaultAsync(dl => dl.DocumentId == documentId && dl.LabelId == labelId);

        if (documentLabel == null) return false;

        _context.DocumentLabels.Remove(documentLabel);
        await _context.SaveChangesAsync();

        return true;
    }

    private static LabelDto MapToDto(Label l)
    {
        return new LabelDto(l.Id, l.Name, l.Color, l.CreatedAt);
    }
}
