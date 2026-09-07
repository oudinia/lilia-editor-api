using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

using Lilia.Api.Services.Versioning;

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
            v.IsAutoSave,
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
            version.IsAutoSave,
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

        var snapshot = VersionSnapshot.Build(
            document, document.Blocks, document.BibliographyEntries);

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
            version.IsAutoSave,
            version.Snapshot.RootElement,
            version.CreatedBy,
            creator?.Name,
            version.CreatedAt
        );
    }

    /// <summary>
    /// Make the document's content equal to a stored version.
    ///
    /// <para>Restoring is no longer destructive. The previous implementation
    /// deleted every block and wrote the snapshot's over the top, so restoring
    /// v3 while sitting on v7 destroyed v7's content unless an auto-version had
    /// happened to catch it in the previous five minutes. It also minted fresh
    /// Guids for every block, breaking <c>\label{blk-&lt;id&gt;}</c> cross-references,
    /// orphaning comments and dropping the whole validation cache — and it
    /// ignored <c>parentId</c>, so nested blocks came back flattened.</para>
    ///
    /// <para>The invariant now is: <b>the newest version always equals the
    /// document.</b> Restoring preserves the current state as a version first
    /// when it is not already stored, then applies the chosen snapshot, then
    /// records the result as a new version. Nothing is lost, the ordering stays
    /// monotonic, and "newest" is a truthful answer to "what am I looking at" —
    /// which is what the version list needs in order to mark one as current.</para>
    /// </summary>
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

        // Keep what is about to be overwritten, unless the newest version
        // already holds it. Without this, restore is a one-way door.
        var newest = await _context.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync();

        var current = VersionSnapshot.Serialise(
            document, document.Blocks, document.BibliographyEntries);

        // Already there. Restoring a version the document currently equals is a
        // no-op, not a reason to record that nothing happened — otherwise
        // clicking the same entry twice stacks identical "Restored from"
        // versions and the history fills with noise.
        if (SnapshotsMatch(snapshot, current.RootElement))
        {
            return await _documentService.GetDocumentAsync(documentId, userId);
        }

        if (newest == null || !SnapshotsMatch(newest.Snapshot.RootElement, current.RootElement))
        {
            _context.DocumentVersions.Add(new DocumentVersion
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                VersionNumber = await NextVersionNumberAsync(documentId),
                Name = "Before restore",
                Snapshot = current,
                IsAutoSave = true,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();
        }

        VersionSnapshot.ApplySettings(snapshot, document);

        _context.Blocks.RemoveRange(document.Blocks);
        foreach (var b in VersionSnapshot.ReadBlocks(snapshot))
        {
            _context.Blocks.Add(new Block
            {
                // The snapshot's id, not a new one — see the summary above.
                Id = b.Id,
                DocumentId = documentId,
                Type = b.Type,
                Content = JsonDocument.Parse(b.Content.GetRawText()),
                SortOrder = b.SortOrder,
                ParentId = b.ParentId,
                Depth = b.Depth,
                Path = b.Path,
                Status = b.Status,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        _context.BibliographyEntries.RemoveRange(document.BibliographyEntries);
        foreach (var e in VersionSnapshot.ReadBibliography(snapshot))
        {
            _context.BibliographyEntries.Add(new BibliographyEntry
            {
                Id = e.Id,
                DocumentId = documentId,
                CiteKey = e.CiteKey,
                EntryType = e.EntryType,
                Data = JsonDocument.Parse(e.Data.GetRawText()),
                FormattedText = e.FormattedText,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        document.UpdatedAt = DateTime.UtcNow;

        // Record where the document now is, so the newest version is the one it
        // actually contains and the list can mark it without guessing.
        _context.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            VersionNumber = await NextVersionNumberAsync(documentId),
            Name = $"Restored from version {version.VersionNumber}",
            Snapshot = JsonDocument.Parse(snapshot.GetRawText()),
            IsAutoSave = false,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
        });

        await _context.SaveChangesAsync();

        return await _documentService.GetDocumentAsync(documentId, userId);
    }

    private async Task<int> NextVersionNumberAsync(Guid documentId) =>
        (await _context.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0) + 1;

    /// <summary>
    /// Whether two snapshots carry the same document. Compared on content rather
    /// than on the raw text so a re-serialisation with different key ordering or
    /// whitespace does not read as a change and pile up "Before restore"
    /// versions on every restore.
    /// </summary>
    internal static bool SnapshotsMatch(JsonElement a, JsonElement b) =>
        VersionSnapshot.Fingerprint(a) == VersionSnapshot.Fingerprint(b);


    private const int AutoVersionThrottleMinutes = 5;
    private const int MaxAutoVersionsPerDocument = 50;

    public async Task CreateAutoVersionAsync(Guid documentId, string userId)
    {
        // Throttle: skip if last auto-version is within 5 minutes
        var lastAutoVersion = await _context.DocumentVersions
            .Where(v => v.DocumentId == documentId && v.IsAutoSave)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        if (lastAutoVersion != null &&
            (DateTime.UtcNow - lastAutoVersion.CreatedAt).TotalMinutes < AutoVersionThrottleMinutes)
        {
            return; // Too soon, skip
        }

        var document = await _context.Documents
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document == null) return;

        var maxVersion = await _context.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

        var snapshot = VersionSnapshot.Build(
            document, document.Blocks, document.BibliographyEntries);

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            VersionNumber = maxVersion + 1,
            Name = $"Auto-save",
            IsAutoSave = true,
            Snapshot = JsonDocument.Parse(JsonSerializer.Serialize(snapshot)),
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentVersions.Add(version);

        // Prune old auto-versions beyond limit
        var autoVersions = await _context.DocumentVersions
            .Where(v => v.DocumentId == documentId && v.IsAutoSave)
            .OrderByDescending(v => v.CreatedAt)
            .Skip(MaxAutoVersionsPerDocument)
            .ToListAsync();

        if (autoVersions.Count > 0)
        {
            _context.DocumentVersions.RemoveRange(autoVersions);
        }

        await _context.SaveChangesAsync();
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
