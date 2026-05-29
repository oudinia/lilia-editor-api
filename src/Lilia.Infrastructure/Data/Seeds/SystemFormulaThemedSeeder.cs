using System.Text.Json;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lilia.Infrastructure.Data.Seeds;

/// <summary>
/// Seeds the 64 Lilia Math reference formulas as system formulas
/// (UserId == null, IsSystem == true). Mirrors
/// <c>lilia-docs/reference/math/data/formulas.json</c> — that file
/// is the single source of truth for both this seeder and the static
/// HTML reference pages.
///
/// Each seeded formula carries the <see cref="Formula.Theme"/> column
/// so the editor's Load-from-library modal can filter by the active
/// Common-panel theme. The stable <see cref="Formula.Slug"/> column
/// makes re-seeding idempotent — re-running this seeder updates
/// existing rows in place rather than appending duplicates.
///
/// Read by <see cref="Lilia.Infrastructure.Data.LiliaDbContext"/> at
/// application start; safe to invoke repeatedly.
/// </summary>
public static class SystemFormulaThemedSeeder
{
    /// <summary>
    /// Path to the canonical JSON catalog, relative to the API process
    /// CWD. Resolves <c>../lilia-docs/reference/math/data/formulas.json</c>
    /// when the API runs from <c>lilia-editor-api/src/Lilia.Api</c>.
    /// </summary>
    private static readonly string[] CatalogProbe =
    {
        "../../../lilia-docs/reference/math/data/formulas.json",
        "../../lilia-docs/reference/math/data/formulas.json",
        "../lilia-docs/reference/math/data/formulas.json",
        "lilia-docs/reference/math/data/formulas.json",
    };

    public static async Task SeedAsync(LiliaDbContext context, ILogger? logger = null)
    {
        var catalog = TryLoadCatalog(logger);
        if (catalog is null) return;

        // Idempotent upsert keyed off Slug. We only touch system rows
        // (UserId == null) so a user-saved formula is never clobbered.
        var existingBySlug = await context.Formulas
            .Where(f => f.IsSystem && f.UserId == null && f.Slug != null)
            .ToDictionaryAsync(f => f.Slug!, f => f);

        var now = DateTime.UtcNow;
        var upserts = 0;
        foreach (var entry in catalog.Formulas)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) ||
                string.IsNullOrWhiteSpace(entry.Title) ||
                string.IsNullOrWhiteSpace(entry.Latex) ||
                !FormulaThemes.IsValid(entry.Theme))
            {
                logger?.LogWarning("Skipping malformed reference entry: {Id}", entry.Id);
                continue;
            }

            var category = MapThemeToCategory(entry.Theme!);
            var subcategory = MapThemeToSubcategory(entry.Theme!);

            if (existingBySlug.TryGetValue(entry.Id, out var existing))
            {
                existing.Name = entry.Title!;
                existing.Description = entry.Intent;
                existing.LatexContent = entry.Latex!;
                existing.LmlContent = $"\n@equation(label: eq:{entry.Id}, mode: display)\n{entry.Latex}\n";
                existing.Category = category;
                existing.Subcategory = subcategory;
                existing.Theme = entry.Theme;
                existing.Tags = BuildTags(entry);
                existing.UpdatedAt = now;
            }
            else
            {
                context.Formulas.Add(new Formula
                {
                    Id = Guid.NewGuid(),
                    UserId = null,
                    Name = entry.Title!,
                    Description = entry.Intent,
                    LatexContent = entry.Latex!,
                    LmlContent = $"\n@equation(label: eq:{entry.Id}, mode: display)\n{entry.Latex}\n",
                    Category = category,
                    Subcategory = subcategory,
                    Tags = BuildTags(entry),
                    Theme = entry.Theme,
                    Slug = entry.Id,
                    IsFavorite = false,
                    IsSystem = true,
                    UsageCount = 0,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            upserts++;
        }

        await context.SaveChangesAsync();
        logger?.LogInformation("SystemFormulaThemedSeeder: upserted {Count} reference formulas across {Themes} themes",
            upserts, catalog.Themes.Count);
    }

    private static ReferenceCatalog? TryLoadCatalog(ILogger? logger)
    {
        var cwd = Directory.GetCurrentDirectory();
        foreach (var rel in CatalogProbe)
        {
            var full = Path.GetFullPath(Path.Combine(cwd, rel));
            if (File.Exists(full))
            {
                try
                {
                    var text = File.ReadAllText(full);
                    var parsed = JsonSerializer.Deserialize<ReferenceCatalog>(text, JsonOptions);
                    if (parsed?.Formulas?.Count > 0) return parsed;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to parse formula catalog at {Path}", full);
                }
            }
        }
        logger?.LogWarning("Formula catalog not found near {Cwd}; themed seed skipped", cwd);
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static List<string> BuildTags(ReferenceFormula f)
    {
        // Tags drive both search and the Library page filters. Always
        // include the theme + a stable "lilia-math-reference" marker
        // so the UI can distinguish curated reference formulas from
        // the older legacy system seed.
        var tags = new List<string> { "lilia-math-reference", f.Theme ?? "general" };
        return tags;
    }

    /// <summary>
    /// Maps our 8 Lilia Math themes onto the existing
    /// <see cref="FormulaCategories"/> taxonomy so legacy callers
    /// that filter by Category keep working.
    /// </summary>
    private static string MapThemeToCategory(string theme) => theme switch
    {
        FormulaThemes.Physics => FormulaCategories.Physics,
        FormulaThemes.Stats   => FormulaCategories.Statistics,
        FormulaThemes.Cs      => FormulaCategories.ComputerScience,
        _ => FormulaCategories.Math,
    };

    private static string? MapThemeToSubcategory(string theme) => theme switch
    {
        FormulaThemes.Calculus => FormulaSubcategories.Calculus,
        FormulaThemes.LinAlg   => FormulaSubcategories.LinearAlgebra,
        FormulaThemes.Sets     => FormulaSubcategories.SetTheory,
        FormulaThemes.Stats    => FormulaSubcategories.Probability,
        _ => null,
    };

    // --- JSON DTOs (matching formulas.json) ----------------------------------

    private sealed class ReferenceCatalog
    {
        public string Version { get; set; } = "";
        public List<ReferenceTheme> Themes { get; set; } = new();
        public List<ReferenceFormula> Formulas { get; set; } = new();
    }

    private sealed class ReferenceTheme
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private sealed class ReferenceFormula
    {
        public string Id { get; set; } = "";
        public string Theme { get; set; } = "";
        public string Title { get; set; } = "";
        public string Latex { get; set; } = "";
        public string? Intent { get; set; }
        public List<string>? Recipe { get; set; }
    }
}
