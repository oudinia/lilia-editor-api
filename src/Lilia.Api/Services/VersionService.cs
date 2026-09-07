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

        // Which row the document is actually on. Validated rather than trusted —
        // see ValidatedCurrentVersionAsync — so an edit through any path shows
        // up here as "on no version" without that path knowing about the marker.
        var current = await ValidatedCurrentVersionAsync(documentId);

        return versions.Select(v => new VersionListDto(
            v.Id,
            v.VersionNumber,
            v.Name,
            v.IsAutoSave,
            v.CreatedBy,
            v.Creator?.Name,
            v.CreatedAt,
            v.Id == current
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
    /// Move the document to a stored version, and move the marker with it.
    ///
    /// <para>This is a checkout, not a revert. An earlier version of this
    /// appended "Restored from version N" so the newest row would equal the
    /// document — honest, but it read backwards: you asked to go back in time
    /// and the history grew. Git makes the same distinction, and the useful half
    /// is <c>checkout</c>: move a pointer, create nothing.</para>
    ///
    /// <para>Immutability is untouched. Versions are still write-once, and the
    /// state about to be overwritten is preserved first when it is not already
    /// stored — quietly, because losing work is unacceptable but announcing the
    /// rescue in the timeline is the clutter this removes.</para>
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

        var current = VersionSnapshot.Serialise(
            document, document.Blocks, document.BibliographyEntries);

        // Already there. Restoring a version the document equals only needs the
        // marker set — there is nothing to overwrite and nothing to preserve.
        if (SnapshotsMatch(snapshot, current.RootElement))
        {
            document.CurrentVersionId = versionId;
            await _context.SaveChangesAsync();
            return await _documentService.GetDocumentAsync(documentId, userId);
        }

        // Keep what is about to be overwritten, unless a version already holds
        // it. Without this, restore is a one-way door.
        var newest = await _context.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync();

        if (newest == null || !SnapshotsMatch(newest.Snapshot.RootElement, current.RootElement))
        {
            _context.DocumentVersions.Add(new DocumentVersion
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                VersionNumber = await NextVersionNumberAsync(documentId),
                // Named for what it preserves, not for what triggered it. Two
                // rows called "Before restore" tell you nothing about which is
                // which; the version being left is the useful half.
                Name = $"Unsaved work before v{version.VersionNumber}",
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
                // The snapshot's id, not a new one: the emitter writes
                // \label{blk-<id>}, so a fresh Guid breaks every cross-reference.
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
        // The marker moves back. Nothing is appended.
        document.CurrentVersionId = versionId;

        await _context.SaveChangesAsync();

        return await _documentService.GetDocumentAsync(documentId, userId);
    }

    /// <summary>
    /// Whether the document still holds the version it claims to.
    ///
    /// The pointer is a hint. Two dozen code paths mutate blocks and none of
    /// them should have to remember to clear it, so it is checked against the
    /// document's real content on the way out instead — an edit through any path
    /// makes this false without that path knowing this feature exists.
    /// </summary>
    private async Task<Guid?> ValidatedCurrentVersionAsync(Guid documentId)
    {
        var document = await _context.Documents
            .Include(d => d.Blocks)
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == documentId);
        if (document?.CurrentVersionId is not { } pointer) return null;

        var claimed = await _context.DocumentVersions
            .FirstOrDefaultAsync(v => v.DocumentId == documentId && v.Id == pointer);
        if (claimed == null) return null;   // deleted out from under us

        var current = VersionSnapshot.Serialise(
            document, document.Blocks, document.BibliographyEntries);

        return SnapshotsMatch(claimed.Snapshot.RootElement, current.RootElement) ? pointer : null;
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
