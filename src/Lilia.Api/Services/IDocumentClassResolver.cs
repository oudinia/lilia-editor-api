using System.Text.Json;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// LILIA-121 D1 — scoped helper that resolves a document's LaTeX class +
/// allowed-sections list in a single round-trip. Pulled out of
/// <see cref="BlockTypeService"/> so the catalog service can stay a
/// stateless singleton.
/// </summary>
public interface IDocumentClassResolver
{
    Task<DocumentClassInfo?> ResolveAsync(Guid documentId);
}

public class DocumentClassInfo
{
    public string? DocumentClass { get; init; }
    public List<string> AllowedSections { get; init; } = [];
}

public class DocumentClassResolver : IDocumentClassResolver
{
    private readonly LiliaDbContext _context;

    public DocumentClassResolver(LiliaDbContext context)
    {
        _context = context;
    }

    public async Task<DocumentClassInfo?> ResolveAsync(Guid documentId)
    {
        var lookup = await _context.Documents
            .AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new
            {
                d.LatexDocumentClass,
                AllowedJson = _context.LatexDocumentClasses
                    .Where(c => c.Slug == d.LatexDocumentClass)
                    .Select(c => c.AllowedBlocks)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (lookup is null) return null;

        return new DocumentClassInfo
        {
            DocumentClass = lookup.LatexDocumentClass,
            AllowedSections = ParseAllowedBlocks(lookup.AllowedJson),
        };
    }

    /// <summary>
    /// Parse the <c>allowed_blocks</c> jsonb cell into a flat list of
    /// sectioning slugs. Tolerant of nulls / malformed payloads — the
    /// migration writes well-formed arrays today, but we don't want a stray
    /// admin edit to crash the slash menu.
    /// </summary>
    private static List<string> ParseAllowedBlocks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var slugs = new List<string>(doc.RootElement.GetArrayLength());
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) slugs.Add(s);
                }
            }
            return slugs;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
