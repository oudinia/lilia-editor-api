using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class SnippetService : ISnippetService
{
    private readonly LiliaDbContext _context;

    public SnippetService(LiliaDbContext context)
    {
        _context = context;
    }

    public async Task<SnippetPageDto> GetSnippetsAsync(string userId, SnippetSearchDto search)
    {
        var query = _context.Snippets.AsQueryable();

        // Show user's own snippets + system snippets (if requested)
        if (search.IncludeSystem)
        {
            query = query.Where(s => s.UserId == userId || (s.IsSystem && s.UserId == null));
        }
        else
        {
            query = query.Where(s => s.UserId == userId);
        }

        // Category filter
        if (!string.IsNullOrEmpty(search.Category))
        {
            query = query.Where(s => s.Category == search.Category);
        }

        // Favorites filter
        if (search.FavoritesOnly == true)
        {
            query = query.Where(s => s.IsFavorite);
        }

        // Text search (ILIKE on name, description, latexContent)
        if (!string.IsNullOrEmpty(search.Query))
        {
            var searchTerm = search.Query.ToLower();
            query = query.Where(s =>
                EF.Functions.ILike(s.Name, $"%{searchTerm}%") ||
                (s.Description != null && EF.Functions.ILike(s.Description, $"%{searchTerm}%")) ||
                EF.Functions.ILike(s.LatexContent, $"%{searchTerm}%")
            );
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(s => s.IsFavorite)
            .ThenByDescending(s => s.UsageCount)
            .ThenBy(s => s.Name)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync();

        return new SnippetPageDto(
            items.Select(MapToDto).ToList(),
            totalCount,
            search.Page,
            search.PageSize
        );
    }

    public async Task<SnippetDto?> GetSnippetAsync(Guid id, string userId)
    {
        var snippet = await _context.Snippets
            .FirstOrDefaultAsync(s => s.Id == id && (s.UserId == userId || (s.IsSystem && s.UserId == null)));

        return snippet == null ? null : MapToDto(snippet);
    }

    public async Task<SnippetDto> CreateSnippetAsync(string userId, CreateSnippetDto dto)
    {
        var snippet = new Snippet
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Description = dto.Description,
            LatexContent = dto.LatexContent,
            BlockType = dto.BlockType,
            Category = dto.Category,
            RequiredPackages = dto.RequiredPackages ?? new List<string>(),
            Preamble = dto.Preamble,
            Tags = dto.Tags ?? new List<string>(),
            IsFavorite = false,
            IsSystem = false,
            UsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Snippets.Add(snippet);
        await _context.SaveChangesAsync();

        return MapToDto(snippet);
    }

    public async Task<SnippetDto?> UpdateSnippetAsync(Guid id, string userId, UpdateSnippetDto dto)
    {
        var snippet = await _context.Snippets
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && !s.IsSystem);

        if (snippet == null) return null;

        if (dto.Name != null) snippet.Name = dto.Name;
        if (dto.Description != null) snippet.Description = dto.Description;
        if (dto.LatexContent != null) snippet.LatexContent = dto.LatexContent;
        if (dto.BlockType != null) snippet.BlockType = dto.BlockType;
        if (dto.Category != null) snippet.Category = dto.Category;
        if (dto.RequiredPackages != null) snippet.RequiredPackages = dto.RequiredPackages;
        if (dto.Preamble != null) snippet.Preamble = dto.Preamble;
        if (dto.Tags != null) snippet.Tags = dto.Tags;

        snippet.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapToDto(snippet);
    }

    public async Task<bool> DeleteSnippetAsync(Guid id, string userId)
    {
        var snippet = await _context.Snippets
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && !s.IsSystem);

        if (snippet == null) return false;

        _context.Snippets.Remove(snippet);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<SnippetDto?> ToggleFavoriteAsync(Guid id, string userId)
    {
        var snippet = await _context.Snippets
            .FirstOrDefaultAsync(s => s.Id == id && (s.UserId == userId || (s.IsSystem && s.UserId == null)));

        if (snippet == null) return null;

        snippet.IsFavorite = !snippet.IsFavorite;
        snippet.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapToDto(snippet);
    }

    public async Task<bool> IncrementUsageAsync(Guid id, string userId)
    {
        var snippet = await _context.Snippets
            .FirstOrDefaultAsync(s => s.Id == id && (s.UserId == userId || (s.IsSystem && s.UserId == null)));

        if (snippet == null) return false;

        snippet.UsageCount++;
        snippet.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        return await _context.Snippets
            .Select(s => s.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    private static SnippetDto MapToDto(Snippet s)
    {
        return new SnippetDto(
            s.Id,
            s.Name,
            s.Description,
            s.LatexContent,
            s.BlockType,
            s.Category,
            s.RequiredPackages,
            s.Preamble,
            s.Tags,
            s.IsFavorite,
            s.IsSystem,
            s.UsageCount,
            s.UserId,
            s.CreatedAt,
            s.UpdatedAt
        );
    }
}
