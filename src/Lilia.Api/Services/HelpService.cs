using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public interface IHelpService
{
    Task<List<HelpArticleListDto>> GetAllAsync(string? category = null);
    Task<HelpArticleDetailDto?> GetByIdAsync(Guid id);
    Task<HelpArticleDetailDto?> GetBySlugAsync(string slug);
    Task<List<HelpCategoryDto>> GetCategoriesAsync();
    Task<List<HelpArticleListDto>> SearchAsync(string query);
}

public class HelpService : IHelpService
{
    private readonly LiliaDbContext _context;

    public HelpService(LiliaDbContext context)
    {
        _context = context;
    }

    public async Task<List<HelpArticleListDto>> GetAllAsync(string? category = null)
    {
        var query = _context.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.IsHelpContent);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(d => d.HelpCategory == category);

        return await query
            .OrderBy(d => d.HelpCategory)
            .ThenBy(d => d.HelpOrder)
            .Select(d => new HelpArticleListDto(
                d.Id,
                d.Title,
                d.HelpCategory,
                d.HelpOrder,
                d.HelpSlug
            ))
            .ToListAsync();
    }

    public async Task<HelpArticleDetailDto?> GetByIdAsync(Guid id)
    {
        var doc = await _context.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.IsHelpContent && d.Id == id)
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.HelpCategory,
                d.HelpOrder,
                d.HelpSlug,
            })
            .FirstOrDefaultAsync();

        if (doc == null) return null;

        var rawBlocks = await _context.Blocks
            .AsNoTracking()
            .Where(b => b.DocumentId == id)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        var blocks = rawBlocks.Select(b => new BlockDto(
            b.Id, b.DocumentId, b.Type,
            b.Content.RootElement,
            b.SortOrder, b.ParentId, b.Depth,
            b.CreatedAt, b.UpdatedAt
        )).ToList();

        return new HelpArticleDetailDto(
            doc.Id, doc.Title, doc.HelpCategory,
            doc.HelpOrder, doc.HelpSlug, blocks
        );
    }

    public async Task<HelpArticleDetailDto?> GetBySlugAsync(string slug)
    {
        var doc = await _context.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.IsHelpContent && d.HelpSlug == slug)
            .Select(d => new { d.Id })
            .FirstOrDefaultAsync();

        if (doc == null) return null;
        return await GetByIdAsync(doc.Id);
    }

    public async Task<List<HelpCategoryDto>> GetCategoriesAsync()
    {
        var categories = await _context.Documents
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.IsHelpContent && d.HelpCategory != null)
            .GroupBy(d => d.HelpCategory!)
            .Select(g => new HelpCategoryDto(
                g.Key,
                g.Key, // Label same as category for now
                g.Count()
            ))
            .ToListAsync();

        // Map category keys to human labels
        var labelMap = new Dictionary<string, string>
        {
            ["getting-started"] = "Getting Started",
            ["syntax"] = "Syntax",
            ["tutorials"] = "Tutorials",
            ["reference"] = "Reference",
        };

        return categories
            .Select(c => c with { Label = labelMap.GetValueOrDefault(c.Category, c.Category) })
            .OrderBy(c => c.Category switch
            {
                "getting-started" => 0,
                "syntax" => 1,
                "tutorials" => 2,
                "reference" => 3,
                _ => 99
            })
            .ToList();
    }

    public async Task<List<HelpArticleListDto>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await GetAllAsync();

        // Full-text search using PostgreSQL tsvector/tsquery with ranking
        var results = await _context.Documents
            .FromSqlRaw(@"
                SELECT * FROM documents
                WHERE is_help_content = true
                  AND to_tsvector('english', COALESCE(search_text, ''))
                      @@ plainto_tsquery('english', {0})
                ORDER BY ts_rank(
                    to_tsvector('english', COALESCE(search_text, '')),
                    plainto_tsquery('english', {0})
                ) DESC
            ", query)
            .AsNoTracking()
            .IgnoreQueryFilters()
            .ToListAsync();

        // Fallback to ILIKE if full-text returns nothing (handles partial matches)
        if (results.Count == 0)
        {
            var lowerQuery = query.ToLower();
            results = await _context.Documents
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(d => d.IsHelpContent &&
                    (d.Title.ToLower().Contains(lowerQuery) ||
                     (d.SearchText != null && d.SearchText.ToLower().Contains(lowerQuery))))
                .OrderBy(d => d.HelpCategory)
                .ThenBy(d => d.HelpOrder)
                .ToListAsync();
        }

        return results.Select(d => new HelpArticleListDto(
            d.Id, d.Title, d.HelpCategory, d.HelpOrder, d.HelpSlug
        )).ToList();
    }
}
