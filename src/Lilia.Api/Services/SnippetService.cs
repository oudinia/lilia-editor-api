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
        search = search with
        {
            Page = Math.Max(1, search.Page),
            PageSize = Math.Clamp(search.PageSize, 1, 100)
        };

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

        var rows = WithFavorite(query, userId);

        // Favorites filter
        if (search.FavoritesOnly == true)
        {
            rows = rows.Where(r => r.IsFavorite);
        }

        var totalCount = await rows.CountAsync();

        var items = await rows
            .OrderByDescending(r => r.IsFavorite)
            .ThenByDescending(r => r.Snippet.UsageCount)
            .ThenBy(r => r.Snippet.Name)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync();

        return new SnippetPageDto(
            items.Select(r => MapToDto(r.Snippet, r.IsFavorite)).ToList(),
            totalCount,
            search.Page,
            search.PageSize
        );
    }

    public async Task<SnippetDto?> GetSnippetAsync(Guid id, string userId)
    {
        var row = await WithFavorite(
                _context.Snippets.Where(s => s.Id == id && (s.UserId == userId || (s.IsSystem && s.UserId == null))),
                userId)
            .FirstOrDefaultAsync();

        return row == null ? null : MapToDto(row.Snippet, row.IsFavorite);
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

        if (!snippet.IsSystem)
        {
            // The user's own snippet: the row is theirs alone, so the flag is too.
            snippet.IsFavorite = !snippet.IsFavorite;
            snippet.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return MapToDto(snippet, snippet.IsFavorite);
        }

        // A system snippet is one row shared by every user; the favourite is
        // this user's own, kept beside it. The shared row is not touched.
        var favorite = await _context.SnippetFavorites
            .FirstOrDefaultAsync(f => f.SnippetId == id && f.UserId == userId);
        if (favorite == null)
            _context.SnippetFavorites.Add(new SnippetFavorite { UserId = userId, SnippetId = id, CreatedAt = DateTime.UtcNow });
        else
            _context.SnippetFavorites.Remove(favorite);

        await _context.SaveChangesAsync();

        return MapToDto(snippet, isFavorite: favorite == null);
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

    private sealed class SnippetRow
    {
        public Snippet Snippet { get; init; } = null!;
        public bool IsFavorite { get; init; }
    }

    /// <summary>
    /// Pairs each snippet with whether it is this user's favourite: their own
    /// snippet's flag, or — for a system snippet, shared by everyone — a
    /// snippet_favorites row of theirs.
    /// </summary>
    private IQueryable<SnippetRow> WithFavorite(IQueryable<Snippet> snippets, string userId) =>
        snippets.Select(s => new SnippetRow
        {
            Snippet = s,
            IsFavorite = s.IsSystem
                ? _context.SnippetFavorites.Any(f => f.SnippetId == s.Id && f.UserId == userId)
                : s.IsFavorite,
        });

    private static SnippetDto MapToDto(Snippet s) => MapToDto(s, s.IsFavorite);

    private static SnippetDto MapToDto(Snippet s, bool isFavorite)
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
            isFavorite,
            s.IsSystem,
            s.UsageCount,
            s.UserId,
            s.CreatedAt,
            s.UpdatedAt
        );
    }
}
