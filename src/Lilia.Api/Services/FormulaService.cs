using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public partial class FormulaService : IFormulaService
{
    private readonly LiliaDbContext _context;

    public FormulaService(LiliaDbContext context)
    {
        _context = context;
    }

    public async Task<FormulaPageDto> GetFormulasAsync(string userId, FormulaSearchDto search)
    {
        var query = _context.Formulas.AsQueryable();

        // Show user's own formulas + system formulas (if requested)
        if (search.IncludeSystem)
        {
            query = query.Where(f => f.UserId == userId || (f.IsSystem && f.UserId == null));
        }
        else
        {
            query = query.Where(f => f.UserId == userId);
        }

        // Category filter
        if (!string.IsNullOrEmpty(search.Category))
        {
            query = query.Where(f => f.Category == search.Category);
        }

        // Subcategory filter
        if (!string.IsNullOrEmpty(search.Subcategory))
        {
            query = query.Where(f => f.Subcategory == search.Subcategory);
        }

        // Favorites filter
        if (search.FavoritesOnly == true)
        {
            query = query.Where(f => f.IsFavorite);
        }

        // Text search (ILIKE on name, description, tags)
        if (!string.IsNullOrEmpty(search.Query))
        {
            var searchTerm = search.Query.ToLower();
            query = query.Where(f =>
                EF.Functions.ILike(f.Name, $"%{searchTerm}%") ||
                (f.Description != null && EF.Functions.ILike(f.Description, $"%{searchTerm}%")) ||
                EF.Functions.ILike(f.LatexContent, $"%{searchTerm}%")
            );
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(f => f.IsFavorite)
            .ThenByDescending(f => f.UsageCount)
            .ThenBy(f => f.Name)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync();

        return new FormulaPageDto(
            items.Select(MapToDto).ToList(),
            totalCount,
            search.Page,
            search.PageSize
        );
    }

    public async Task<FormulaDto?> GetFormulaAsync(Guid id, string userId)
    {
        var formula = await _context.Formulas
            .FirstOrDefaultAsync(f => f.Id == id && (f.UserId == userId || (f.IsSystem && f.UserId == null)));

        return formula == null ? null : MapToDto(formula);
    }

    public async Task<FormulaDto> CreateFormulaAsync(string userId, CreateFormulaDto dto)
    {
        var formula = new Formula
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Description = dto.Description,
            LatexContent = dto.LatexContent,
            LmlContent = GenerateLml(dto.LatexContent, Slugify(dto.Name)),
            Category = dto.Category,
            Subcategory = dto.Subcategory,
            Tags = dto.Tags ?? new List<string>(),
            IsFavorite = false,
            IsSystem = false,
            UsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Formulas.Add(formula);
        await _context.SaveChangesAsync();

        return MapToDto(formula);
    }

    public async Task<FormulaDto?> UpdateFormulaAsync(Guid id, string userId, UpdateFormulaDto dto)
    {
        var formula = await _context.Formulas
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId && !f.IsSystem);

        if (formula == null) return null;

        if (dto.Name != null) formula.Name = dto.Name;
        if (dto.Description != null) formula.Description = dto.Description;
        if (dto.LatexContent != null)
        {
            formula.LatexContent = dto.LatexContent;
            formula.LmlContent = GenerateLml(dto.LatexContent, Slugify(dto.Name ?? formula.Name));
        }
        if (dto.Category != null) formula.Category = dto.Category;
        if (dto.Subcategory != null) formula.Subcategory = dto.Subcategory;
        if (dto.Tags != null) formula.Tags = dto.Tags;

        formula.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapToDto(formula);
    }

    public async Task<bool> DeleteFormulaAsync(Guid id, string userId)
    {
        var formula = await _context.Formulas
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId && !f.IsSystem);

        if (formula == null) return false;

        _context.Formulas.Remove(formula);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<FormulaDto?> ToggleFavoriteAsync(Guid id, string userId)
    {
        var formula = await _context.Formulas
            .FirstOrDefaultAsync(f => f.Id == id && (f.UserId == userId || (f.IsSystem && f.UserId == null)));

        if (formula == null) return null;

        formula.IsFavorite = !formula.IsFavorite;
        formula.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapToDto(formula);
    }

    public async Task<string?> IncrementUsageAsync(Guid id, string userId, string? label)
    {
        var formula = await _context.Formulas
            .FirstOrDefaultAsync(f => f.Id == id && (f.UserId == userId || (f.IsSystem && f.UserId == null)));

        if (formula == null) return null;

        formula.UsageCount++;
        formula.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return GenerateLml(formula.LatexContent, label ?? Slugify(formula.Name));
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        return await _context.Formulas
            .Select(f => f.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    public async Task<List<string>> GetSubcategoriesAsync(string category)
    {
        return await _context.Formulas
            .Where(f => f.Category == category && f.Subcategory != null)
            .Select(f => f.Subcategory!)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
    }

    public async Task<List<string>> GetUserTagsAsync(string userId)
    {
        var formulas = await _context.Formulas
            .Where(f => f.UserId == userId)
            .Select(f => f.Tags)
            .ToListAsync();

        return formulas
            .SelectMany(t => t)
            .Distinct()
            .OrderBy(t => t)
            .ToList();
    }

    private static string GenerateLml(string latexContent, string label)
    {
        return $"\n@equation(label: eq:{label}, mode: display)\n{latexContent}\n";
    }

    private static string Slugify(string name)
    {
        var slug = SlugRegex().Replace(name.ToLower(), "-");
        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? "formula" : slug;
    }

    private static FormulaDto MapToDto(Formula f)
    {
        return new FormulaDto(
            f.Id,
            f.Name,
            f.Description,
            f.LatexContent,
            f.LmlContent,
            f.Category,
            f.Subcategory,
            f.Tags,
            f.IsFavorite,
            f.IsSystem,
            f.UsageCount,
            f.UserId,
            f.CreatedAt,
            f.UpdatedAt
        );
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
