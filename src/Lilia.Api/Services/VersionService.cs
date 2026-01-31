using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class VersionService : IVersionService
{
    private readonly LiliaDbContext _context;
    private readonly IDocumentService _documentService;

    public VersionService(LiliaDbContext context, IDocumentService documentService)
    {
        _context = context;
        _documentService = documentService;
    }

    public async Task<List<VersionListDto>> GetVersionsAsync(Guid documentId)
    {
        var versions = await _context.DocumentVersions
            .Include(v => v.Creator)
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();

        return versions.Select(v => new VersionListDto(
            v.Id,
            v.VersionNumber,
            v.Name,
            v.CreatedBy,
            v.Creator?.Name,
            v.CreatedAt
        )).ToList();
    }

    public async Task<VersionDto?> GetVersionAsync(Guid documentId, Guid versionId)
    {
        var version = await _context.DocumentVersions
            .Include(v => v.Creator)
            .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.Id == versionId);

        if (version == null) return null;

        return new VersionDto(
            version.Id,
            version.DocumentId,
            version.VersionNumber,
            version.Name,
            version.Snapshot.RootElement,
            version.CreatedBy,
            version.Creator?.Name,
            version.CreatedAt
        );
    }

    public async Task<VersionDto> CreateVersionAsync(Guid documentId, string userId, CreateVersionDto dto)
    {
        var document = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null)
            throw new ArgumentException("Document not found");

        // Get next version number
        var maxVersion = await _context.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

        // Create snapshot
        var snapshot = new
        {
            title = document.Title,
            language = document.Language,
            paperSize = document.PaperSize,
            fontFamily = document.FontFamily,
            fontSize = document.FontSize,
            blocks = document.Blocks.Select(b => new
            {
                id = b.Id,
                type = b.Type,
                content = b.Content.RootElement,
                sortOrder = b.SortOrder,
                parentId = b.ParentId,
                depth = b.Depth
            }),
            bibliography = document.BibliographyEntries.Select(e => new
            {
                id = e.Id,
                citeKey = e.CiteKey,
                entryType = e.EntryType,
                data = e.Data.RootElement
            })
        };

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            VersionNumber = maxVersion + 1,
            Name = dto.Name ?? $"Version {maxVersion + 1}",
            Snapshot = JsonDocument.Parse(JsonSerializer.Serialize(snapshot)),
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentVersions.Add(version);
        await _context.SaveChangesAsync();

        var creator = await _context.Users.FindAsync(userId);

        return new VersionDto(
            version.Id,
            version.DocumentId,
            version.VersionNumber,
            version.Name,
            version.Snapshot.RootElement,
            version.CreatedBy,
            creator?.Name,
            version.CreatedAt
        );
    }

    public async Task<DocumentDto?> RestoreVersionAsync(Guid documentId, Guid versionId, string userId)
    {
        var version = await _context.DocumentVersions
            .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.Id == versionId);

        if (version == null) return null;

        var document = await _context.Documents
            .Include(d => d.Blocks)
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null) return null;

        var snapshot = version.Snapshot.RootElement;

        // Restore document properties
        if (snapshot.TryGetProperty("title", out var title)) document.Title = title.GetString() ?? document.Title;
        if (snapshot.TryGetProperty("language", out var lang)) document.Language = lang.GetString() ?? document.Language;
        if (snapshot.TryGetProperty("paperSize", out var paper)) document.PaperSize = paper.GetString() ?? document.PaperSize;
        if (snapshot.TryGetProperty("fontFamily", out var font)) document.FontFamily = font.GetString() ?? document.FontFamily;
        if (snapshot.TryGetProperty("fontSize", out var size)) document.FontSize = size.GetInt32();

        // Remove existing blocks
        _context.Blocks.RemoveRange(document.Blocks);

        // Restore blocks
        if (snapshot.TryGetProperty("blocks", out var blocks))
        {
            foreach (var blockElement in blocks.EnumerateArray())
            {
                var block = new Block
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    Type = blockElement.GetProperty("type").GetString() ?? "paragraph",
                    Content = JsonDocument.Parse(blockElement.GetProperty("content").GetRawText()),
                    SortOrder = blockElement.GetProperty("sortOrder").GetInt32(),
                    Depth = blockElement.TryGetProperty("depth", out var depth) ? depth.GetInt32() : 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Blocks.Add(block);
            }
        }

        // Remove existing bibliography entries
        _context.BibliographyEntries.RemoveRange(document.BibliographyEntries);

        // Restore bibliography
        if (snapshot.TryGetProperty("bibliography", out var bib))
        {
            foreach (var bibElement in bib.EnumerateArray())
            {
                var entry = new BibliographyEntry
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    CiteKey = bibElement.GetProperty("citeKey").GetString() ?? "",
                    EntryType = bibElement.GetProperty("entryType").GetString() ?? "misc",
                    Data = JsonDocument.Parse(bibElement.GetProperty("data").GetRawText()),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.BibliographyEntries.Add(entry);
            }
        }

        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return await _documentService.GetDocumentAsync(documentId, userId);
    }

    public async Task<bool> DeleteVersionAsync(Guid documentId, Guid versionId, string userId)
    {
        var version = await _context.DocumentVersions
            .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.Id == versionId);

        if (version == null) return false;

        _context.DocumentVersions.Remove(version);
        await _context.SaveChangesAsync();

        return true;
    }
}
