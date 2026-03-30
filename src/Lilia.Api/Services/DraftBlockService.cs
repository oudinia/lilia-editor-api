using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public interface IDraftBlockService
{
    Task<(List<DraftBlockDto> Items, int TotalCount)> ListAsync(string userId, string? type = null, string? category = null, bool? favoritesOnly = null, string? query = null, int page = 1, int pageSize = 50);
    Task<DraftBlockDto?> GetAsync(Guid id, string userId);
    Task<DraftBlockDto> CreateAsync(string userId, CreateDraftBlockDto dto);
    Task<DraftBlockDto?> UpdateAsync(Guid id, string userId, UpdateDraftBlockDto dto);
    Task<bool> DeleteAsync(Guid id, string userId);
    Task<bool> ToggleFavoriteAsync(Guid id, string userId);
    Task<DraftBlockDto?> CreateFromBlockAsync(string userId, CreateDraftFromBlockDto dto);
    Task<Guid?> CommitAsync(Guid id, string userId, CommitDraftBlockDto dto);
    Task<List<string>> GetCategoriesAsync(string userId);
}

public class DraftBlockService : IDraftBlockService
{
    private readonly LiliaDbContext _db;

    public DraftBlockService(LiliaDbContext db)
    {
        _db = db;
    }

    public async Task<(List<DraftBlockDto> Items, int TotalCount)> ListAsync(
        string userId, string? type, string? category, bool? favoritesOnly, string? query, int page, int pageSize)
    {
        var q = _db.DraftBlocks.Where(d => d.UserId == userId);

        if (!string.IsNullOrEmpty(type))
            q = q.Where(d => d.Type == type);
        if (!string.IsNullOrEmpty(category))
            q = q.Where(d => d.Category == category);
        if (favoritesOnly == true)
            q = q.Where(d => d.IsFavorite);
        if (!string.IsNullOrEmpty(query))
            q = q.Where(d => d.Name != null && d.Name.ToLower().Contains(query.ToLower())
                          || d.Type.ToLower().Contains(query.ToLower()));

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(d => d.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items.Select(ToDto).ToList(), total);
    }

    public async Task<DraftBlockDto?> GetAsync(Guid id, string userId)
    {
        var d = await _db.DraftBlocks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        return d == null ? null : ToDto(d);
    }

    public async Task<DraftBlockDto> CreateAsync(string userId, CreateDraftBlockDto dto)
    {
        var draft = new DraftBlock
        {
            UserId = userId,
            Name = dto.Name,
            Type = dto.Type,
            Content = dto.Content.HasValue ? JsonDocument.Parse(dto.Content.Value.GetRawText()) : JsonDocument.Parse("{}"),
            Metadata = dto.Metadata.HasValue ? JsonDocument.Parse(dto.Metadata.Value.GetRawText()) : JsonDocument.Parse("{}"),
            Category = dto.Category,
            Tags = dto.Tags ?? new List<string>(),
        };

        _db.DraftBlocks.Add(draft);
        await _db.SaveChangesAsync();
        return ToDto(draft);
    }

    public async Task<DraftBlockDto?> UpdateAsync(Guid id, string userId, UpdateDraftBlockDto dto)
    {
        var draft = await _db.DraftBlocks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (draft == null) return null;

        if (dto.Name != null) draft.Name = dto.Name;
        if (dto.Type != null) draft.Type = dto.Type;
        if (dto.Content.HasValue) draft.Content = JsonDocument.Parse(dto.Content.Value.GetRawText());
        if (dto.Metadata.HasValue) draft.Metadata = JsonDocument.Parse(dto.Metadata.Value.GetRawText());
        if (dto.Category != null) draft.Category = dto.Category;
        if (dto.Tags != null) draft.Tags = dto.Tags;
        draft.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ToDto(draft);
    }

    public async Task<bool> DeleteAsync(Guid id, string userId)
    {
        var draft = await _db.DraftBlocks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (draft == null) return false;
        _db.DraftBlocks.Remove(draft);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ToggleFavoriteAsync(Guid id, string userId)
    {
        var draft = await _db.DraftBlocks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (draft == null) return false;
        draft.IsFavorite = !draft.IsFavorite;
        draft.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<DraftBlockDto?> CreateFromBlockAsync(string userId, CreateDraftFromBlockDto dto)
    {
        var block = await _db.Blocks
            .Include(b => b.Document)
            .FirstOrDefaultAsync(b => b.Id == dto.BlockId && b.DocumentId == dto.DocumentId);

        if (block == null) return null;

        // Verify user has access to the document
        if (block.Document?.OwnerId != userId)
        {
            var isCollaborator = await _db.Set<DocumentCollaborator>()
                .AnyAsync(c => c.DocumentId == dto.DocumentId && c.UserId == userId);
            if (!isCollaborator) return null;
        }

        var draft = new DraftBlock
        {
            UserId = userId,
            Name = $"{block.Type} draft",
            Type = block.Type,
            Content = block.Content != null ? JsonDocument.Parse(block.Content.RootElement.GetRawText()) : JsonDocument.Parse("{}"),
            Metadata = block.Metadata != null ? JsonDocument.Parse(block.Metadata.RootElement.GetRawText()) : JsonDocument.Parse("{}"),
            Category = MapTypeToCategory(block.Type),
        };

        _db.DraftBlocks.Add(draft);
        await _db.SaveChangesAsync();
        return ToDto(draft);
    }

    public async Task<Guid?> CommitAsync(Guid id, string userId, CommitDraftBlockDto dto)
    {
        var draft = await _db.DraftBlocks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (draft == null) return null;

        // Verify user has write access to the document
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == dto.DocumentId);
        if (doc == null) return null;
        if (doc.OwnerId != userId)
        {
            var isCollaborator = await _db.Set<DocumentCollaborator>()
                .AnyAsync(c => c.DocumentId == dto.DocumentId && c.UserId == userId);
            if (!isCollaborator) return null;
        }

        // Compute sort order if not provided
        var sortOrder = dto.SortOrder ?? (await _db.Blocks
            .Where(b => b.DocumentId == dto.DocumentId)
            .MaxAsync(b => (int?)b.SortOrder) ?? 0) + 1;

        var block = new Block
        {
            DocumentId = dto.DocumentId,
            Type = draft.Type,
            Content = JsonDocument.Parse(draft.Content.RootElement.GetRawText()),
            Metadata = draft.Metadata != null ? JsonDocument.Parse(draft.Metadata.RootElement.GetRawText()) : null,
            SortOrder = sortOrder,
            ParentId = dto.ParentId,
            Depth = dto.Depth ?? 0,
            CreatedBy = userId,
        };

        _db.Blocks.Add(block);
        draft.UsageCount++;
        draft.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return block.Id;
    }

    public async Task<List<string>> GetCategoriesAsync(string userId)
    {
        return await _db.DraftBlocks
            .Where(d => d.UserId == userId && d.Category != null)
            .Select(d => d.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    private static string MapTypeToCategory(string type) => type switch
    {
        "equation" => DraftBlockCategories.Equations,
        "code" => DraftBlockCategories.Code,
        "table" => DraftBlockCategories.Tables,
        "figure" => DraftBlockCategories.Figures,
        _ => DraftBlockCategories.Notes,
    };

    private static DraftBlockDto ToDto(DraftBlock d) => new(
        d.Id, d.UserId, d.Name, d.Type,
        d.Content.RootElement, d.Metadata.RootElement,
        d.Category, d.Tags, d.IsFavorite, d.UsageCount,
        d.CreatedAt, d.UpdatedAt
    );
}
