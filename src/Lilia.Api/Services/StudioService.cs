using System.Text.Json;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class StudioService : IStudioService
{
    private readonly LiliaDbContext _db;
    private readonly IRenderService _renderService;
    private readonly ILogger<StudioService> _logger;

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public StudioService(LiliaDbContext db, IRenderService renderService, ILogger<StudioService> logger)
    {
        _db = db;
        _renderService = renderService;
        _logger = logger;
    }

    /// <summary>
    /// Safely extract and clone the RootElement from a JsonDocument.
    /// Handles null documents, disposed documents, and invalid states
    /// that can occur with EF Core + Npgsql jsonb materialization.
    /// Clone() detaches the JsonElement from the parent JsonDocument lifecycle.
    /// </summary>
    private static JsonElement SafeRoot(JsonDocument? doc)
    {
        if (doc == null) return EmptyObject;
        try
        {
            return doc.RootElement.Clone();
        }
        catch (ObjectDisposedException)
        {
            return EmptyObject;
        }
        catch (InvalidOperationException)
        {
            return EmptyObject;
        }
    }

    private static StudioBlockNodeDto ToNodeDto(Block b) => new(
        b.Id, b.Type, b.Path, b.SortOrder, b.ParentId, b.Depth, b.Status, BuildNodeMeta(b),
        ExtractPreview(b)
    );

    // Build a merged metadata for the navigator: includes block metadata + content hints (e.g. heading level)
    private static JsonElement BuildNodeMeta(Block b)
    {
        var root = SafeRoot(b.Content);
        if (b.Type == "heading" && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("level", out var lvl))
        {
            // Merge level into metadata
            var dict = new Dictionary<string, object>();
            // Copy existing metadata
            var meta = SafeRoot(b.Metadata);
            if (meta.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in meta.EnumerateObject())
                    dict[prop.Name] = prop.Value;
            }
            dict["level"] = lvl.GetInt32();
            return JsonSerializer.SerializeToElement(dict);
        }
        return SafeRoot(b.Metadata);
    }

    private static string? ExtractPreview(Block b)
    {
        var root = SafeRoot(b.Content);
        if (root.ValueKind != JsonValueKind.Object) return null;

        // Try common text fields
        foreach (var field in new[] { "text", "title", "caption", "latex", "code", "name" })
        {
            if (root.TryGetProperty(field, out var val) && val.ValueKind == JsonValueKind.String)
            {
                var text = val.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Length > 120 ? text[..120] + "…" : text;
            }
        }

        // For headings, also include level
        if (b.Type == "heading" && root.TryGetProperty("level", out _))
        {
            return null; // level is in metadata via the node, text already checked above
        }

        // For code blocks, show language
        if (b.Type == "code" && root.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String)
        {
            return lang.GetString();
        }

        return null;
    }

    private static StudioBlockDetailDto ToDetailDto(Block b) => new(
        b.Id, b.Type, SafeRoot(b.Content), b.Path, b.SortOrder, b.ParentId, b.Depth,
        b.Status, SafeRoot(b.Metadata), b.CreatedAt, b.UpdatedAt
    );

    // --- Tree Operations ---

    public async Task<StudioTreeDto?> GetTreeAsync(Guid documentId)
    {
        var document = await _db.Documents
            .AsNoTracking()
            .Where(d => d.Id == documentId)
            .Select(d => new { d.Id, d.Title })
            .FirstOrDefaultAsync();

        if (document == null) return null;

        var blocks = await _db.Blocks
            .AsNoTracking()
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        var nodes = blocks.Select(ToNodeDto).ToList();

        return new StudioTreeDto(document.Id, document.Title ?? "", nodes);
    }

    public async Task<StudioBlockDetailDto?> GetBlockDetailAsync(Guid documentId, Guid blockId)
    {
        var block = await _db.Blocks
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.DocumentId == documentId && b.Id == blockId);

        if (block == null) return null;
        return ToDetailDto(block);
    }

    public async Task<StudioBlockNodeDto> CreateBlockAsync(Guid documentId, CreateBlockDto dto)
    {
        var maxSort = await _db.Blocks
            .Where(b => b.DocumentId == documentId && b.ParentId == dto.ParentId)
            .MaxAsync(b => (int?)b.SortOrder) ?? -1;

        var position = dto.SortOrder ?? maxSort + 1;

        string? path;
        if (dto.ParentId.HasValue)
        {
            var parentPath = await _db.Blocks
                .Where(b => b.Id == dto.ParentId.Value)
                .Select(b => b.Path)
                .FirstOrDefaultAsync();
            path = $"{parentPath}.{position:D4}";
        }
        else
        {
            path = $"{position:D4}";
        }

        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = dto.Type,
            Content = dto.Content.HasValue
                ? JsonDocument.Parse(dto.Content.Value.GetRawText())
                : JsonDocument.Parse("{}"),
            SortOrder = position,
            ParentId = dto.ParentId,
            Depth = dto.Depth ?? 0,
            Path = path,
            Status = "draft",
            Metadata = JsonDocument.Parse("{}"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Blocks.Add(block);
        await _db.SaveChangesAsync();

        return ToNodeDto(block);
    }

    public async Task<StudioBlockDetailDto?> UpdateBlockContentAsync(Guid documentId, Guid blockId, UpdateBlockDto dto)
    {
        var block = await _db.Blocks
            .FirstOrDefaultAsync(b => b.DocumentId == documentId && b.Id == blockId);

        if (block == null) return null;

        if (dto.Content.HasValue)
            block.Content = JsonDocument.Parse(dto.Content.Value.GetRawText());
        if (dto.Type != null)
            block.Type = dto.Type;

        block.UpdatedAt = DateTime.UtcNow;

        // Update document timestamp so dashboard shows recent edit
        var document = await _db.Documents.FindAsync(documentId);
        if (document != null)
            document.UpdatedAt = DateTime.UtcNow;

        // Invalidate cached preview
        var previews = await _db.BlockPreviews
            .Where(bp => bp.BlockId == blockId)
            .ToListAsync();
        _db.BlockPreviews.RemoveRange(previews);

        await _db.SaveChangesAsync();

        return ToDetailDto(block);
    }

    public async Task<bool> DeleteBlockAsync(Guid documentId, Guid blockId)
    {
        var block = await _db.Blocks
            .FirstOrDefaultAsync(b => b.DocumentId == documentId && b.Id == blockId);

        if (block == null) return false;

        if (block.Path != null)
        {
            var childPrefix = block.Path + ".";
            var children = await _db.Blocks
                .Where(b => b.DocumentId == documentId && b.Path != null && b.Path.StartsWith(childPrefix))
                .ToListAsync();
            _db.Blocks.RemoveRange(children);
        }

        _db.Blocks.Remove(block);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MoveBlockAsync(Guid documentId, Guid blockId, MoveBlockDto dto)
    {
        var block = await _db.Blocks
            .FirstOrDefaultAsync(b => b.DocumentId == documentId && b.Id == blockId);

        if (block == null) return false;

        block.ParentId = dto.NewParentId;
        block.SortOrder = dto.NewPosition;
        block.UpdatedAt = DateTime.UtcNow;

        if (dto.NewParentId.HasValue)
        {
            var parentPath = await _db.Blocks
                .Where(b => b.Id == dto.NewParentId.Value)
                .Select(b => b.Path)
                .FirstOrDefaultAsync();
            block.Path = $"{parentPath}.{dto.NewPosition:D4}";
        }
        else
        {
            block.Path = $"{dto.NewPosition:D4}";
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateBlockMetadataAsync(Guid documentId, Guid blockId, UpdateBlockMetadataDto dto)
    {
        var block = await _db.Blocks
            .FirstOrDefaultAsync(b => b.DocumentId == documentId && b.Id == blockId);

        if (block == null) return false;

        if (dto.Status != null)
            block.Status = dto.Status;
        if (dto.Metadata.HasValue)
            block.Metadata = JsonDocument.Parse(dto.Metadata.Value.GetRawText());

        block.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    // --- Preview ---

    public async Task<BlockPreviewDto?> GetBlockPreviewAsync(Guid blockId, string format)
    {
        var preview = await _db.BlockPreviews
            .AsNoTracking()
            .FirstOrDefaultAsync(bp => bp.BlockId == blockId && bp.Format == format);

        if (preview == null) return null;

        return new BlockPreviewDto(preview.BlockId, preview.Format, preview.Data, preview.RenderedAt);
    }

    /// <summary>
    /// Batch read — all cached previews for a document in a single SQL
    /// round-trip. Response is keyed by blockId (string) so the client
    /// can drop it straight into a map keyed by block.id. Blocks that
    /// haven't been rendered yet are simply absent from the response —
    /// the caller falls back to individual on-demand rendering.
    /// </summary>
    public async Task<Dictionary<string, string>> GetBlockPreviewsForDocumentAsync(
        Guid documentId, string format)
    {
        // Join blocks → block_previews so we get one query scoped by
        // document without pulling every preview across every doc.
        // AsNoTracking — we're returning content, not mutating.
        var rows = await _db.BlockPreviews
            .AsNoTracking()
            .Where(bp => bp.Format == format
                      && _db.Blocks.Any(b => b.Id == bp.BlockId && b.DocumentId == documentId))
            .Select(bp => new { bp.BlockId, bp.Data })
            .ToListAsync();

        var result = new Dictionary<string, string>(rows.Count);
        foreach (var r in rows)
        {
            if (r.Data == null) continue;
            result[r.BlockId.ToString()] = System.Text.Encoding.UTF8.GetString(r.Data);
        }
        return result;
    }

    public async Task<BlockPreviewDto> RenderBlockPreviewAsync(Guid documentId, Guid blockId, string format)
    {
        var block = await _db.Blocks
            .FirstOrDefaultAsync(b => b.DocumentId == documentId && b.Id == blockId);

        if (block == null)
            throw new KeyNotFoundException($"Block {blockId} not found");

        byte[]? rendered = format switch
        {
            "html" => System.Text.Encoding.UTF8.GetBytes(_renderService.RenderBlockToHtml(block)),
            "latex" => System.Text.Encoding.UTF8.GetBytes(_renderService.RenderBlockToLatex(block)),
            _ => throw new ArgumentException($"Unsupported format: {format}")
        };

        var existing = await _db.BlockPreviews
            .FirstOrDefaultAsync(bp => bp.BlockId == blockId && bp.Format == format);

        if (existing != null)
        {
            existing.Data = rendered;
            existing.RenderedAt = DateTime.UtcNow;
        }
        else
        {
            _db.BlockPreviews.Add(new BlockPreview
            {
                Id = Guid.NewGuid(),
                BlockId = blockId,
                Format = format,
                Data = rendered,
                RenderedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        return new BlockPreviewDto(blockId, format, rendered, DateTime.UtcNow);
    }

    // --- Session ---

    public async Task<StudioSessionDto?> GetSessionAsync(string userId, Guid documentId)
    {
        var session = await _db.StudioSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.DocumentId == documentId);

        if (session == null) return null;

        return new StudioSessionDto(
            session.DocumentId, session.FocusedBlockId,
            SafeRoot(session.Layout),
            session.CollapsedIds, session.PinnedIds,
            session.ViewMode, session.LastAccessed
        );
    }

    public async Task<StudioSessionDto> SaveSessionAsync(string userId, Guid documentId, SaveStudioSessionDto dto)
    {
        var session = await _db.StudioSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.DocumentId == documentId);

        if (session == null)
        {
            session = new StudioSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DocumentId = documentId
            };
            _db.StudioSessions.Add(session);
        }

        if (dto.FocusedBlockId.HasValue)
            session.FocusedBlockId = dto.FocusedBlockId;
        if (dto.Layout.HasValue)
            session.Layout = JsonDocument.Parse(dto.Layout.Value.GetRawText());
        if (dto.CollapsedIds != null)
            session.CollapsedIds = dto.CollapsedIds;
        if (dto.PinnedIds != null)
            session.PinnedIds = dto.PinnedIds;
        if (dto.ViewMode != null)
            session.ViewMode = dto.ViewMode;

        session.LastAccessed = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return new StudioSessionDto(
            session.DocumentId, session.FocusedBlockId,
            SafeRoot(session.Layout),
            session.CollapsedIds, session.PinnedIds,
            session.ViewMode, session.LastAccessed
        );
    }
}
