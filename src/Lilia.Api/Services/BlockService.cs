using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lilia.Api.Services;

public class BlockService : IBlockService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<BlockService> _logger;
    private readonly IPreviewCacheService _previewCacheService;
    private readonly IBlockTypeService _blockTypeService;

    public BlockService(LiliaDbContext context, ILogger<BlockService> logger, IPreviewCacheService previewCacheService, IBlockTypeService blockTypeService)
    {
        _context = context;
        _logger = logger;
        _previewCacheService = previewCacheService;
        _blockTypeService = blockTypeService;
    }

    public async Task<List<BlockDto>> GetBlocksAsync(Guid documentId)
    {
        var blocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        return blocks.Select(MapToDto).ToList();
    }

    public async Task<BlockDto?> GetBlockAsync(Guid documentId, Guid blockId)
    {
        var block = await _context.Blocks
            .FirstOrDefaultAsync(b => b.DocumentId == documentId && b.Id == blockId);

        return block == null ? null : MapToDto(block);
    }

    public async Task<BlockDto> CreateBlockAsync(Guid documentId, CreateBlockDto dto)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null)
            throw new ArgumentException("Document not found");

        // Get max sort order for the document
        var maxSortOrder = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .MaxAsync(b => (int?)b.SortOrder) ?? -1;

        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = dto.Type,
            Content = dto.Content.HasValue
                ? JsonDocument.Parse(dto.Content.Value.GetRawText())
                : JsonDocument.Parse("{}"),
            SortOrder = dto.SortOrder ?? (maxSortOrder + 1),
            ParentId = dto.ParentId,
            Depth = dto.Depth ?? 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Blocks.Add(block);

        // Update document timestamp
        document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Invalidate preview cache
        await _previewCacheService.InvalidateCacheAsync(documentId);

        return MapToDto(block);
    }

    public async Task<BlockDto?> UpdateBlockAsync(Guid documentId, Guid blockId, UpdateBlockDto dto)
    {
        var block = await _context.Blocks
            .FirstOrDefaultAsync(b => b.DocumentId == documentId && b.Id == blockId);

        if (block == null) return null;

        if (dto.Type != null) block.Type = dto.Type;
        if (dto.Content.HasValue) block.Content = JsonDocument.Parse(dto.Content.Value.GetRawText());
        if (dto.SortOrder.HasValue) block.SortOrder = dto.SortOrder.Value;
        if (dto.ParentId.HasValue) block.ParentId = dto.ParentId.Value;
        if (dto.Depth.HasValue) block.Depth = dto.Depth.Value;

        block.UpdatedAt = DateTime.UtcNow;

        // Update document timestamp
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null) document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Invalidate preview cache
        await _previewCacheService.InvalidateCacheAsync(documentId);

        return MapToDto(block);
    }

    public async Task<bool> DeleteBlockAsync(Guid documentId, Guid blockId)
    {
        var block = await _context.Blocks
            .FirstOrDefaultAsync(b => b.DocumentId == documentId && b.Id == blockId);

        if (block == null) return false;

        _context.Blocks.Remove(block);

        // Update document timestamp
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null) document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Invalidate preview cache
        await _previewCacheService.InvalidateCacheAsync(documentId);

        return true;
    }

    public async Task<List<BlockDto>> BatchUpdateBlocksAsync(Guid documentId, List<BatchUpdateBlockDto> blocks)
    {
        _logger.LogInformation("BatchUpdateBlocksAsync: Starting for document {DocumentId} with {Count} blocks", documentId, blocks.Count);

        var incomingIds = blocks.Select(b => b.Id).ToHashSet();
        _logger.LogDebug("BatchUpdateBlocksAsync: Block IDs to process: {BlockIds}", string.Join(", ", incomingIds));

        // Fetch ALL existing blocks for this document so we can detect deletions
        var allExistingBlocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId)
            .ToListAsync();

        var existingDict = allExistingBlocks.ToDictionary(b => b.Id);

        _logger.LogInformation("BatchUpdateBlocksAsync: Found {ExistingCount} existing blocks in database", allExistingBlocks.Count);

        var resultBlocks = new List<Block>();
        var createdCount = 0;
        var updatedCount = 0;

        foreach (var update in blocks)
        {
            if (existingDict.TryGetValue(update.Id, out var block))
            {
                // Update existing block
                _logger.LogDebug("BatchUpdateBlocksAsync: Updating existing block {BlockId}", update.Id);
                if (update.Type != null) block.Type = update.Type;
                if (update.Content.HasValue) block.Content = JsonDocument.Parse(update.Content.Value.GetRawText());
                if (update.SortOrder.HasValue) block.SortOrder = update.SortOrder.Value;
                if (update.ParentId.HasValue) block.ParentId = update.ParentId.Value;
                if (update.Depth.HasValue) block.Depth = update.Depth.Value;
                block.UpdatedAt = DateTime.UtcNow;
                resultBlocks.Add(block);
                updatedCount++;
            }
            else
            {
                // Create new block
                _logger.LogInformation("BatchUpdateBlocksAsync: Creating NEW block {BlockId} of type {Type}", update.Id, update.Type ?? "paragraph");
                var newBlock = new Block
                {
                    Id = update.Id,
                    DocumentId = documentId,
                    Type = update.Type ?? "paragraph",
                    Content = update.Content.HasValue
                        ? JsonDocument.Parse(update.Content.Value.GetRawText())
                        : JsonDocument.Parse("{}"),
                    SortOrder = update.SortOrder ?? 0,
                    ParentId = update.ParentId ?? null,
                    Depth = update.Depth ?? 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Blocks.Add(newBlock);
                resultBlocks.Add(newBlock);
                createdCount++;
            }
        }

        // Delete blocks that exist in DB but are NOT in the client's payload (user deleted them)
        var blocksToDelete = allExistingBlocks
            .Where(b => !incomingIds.Contains(b.Id))
            .ToList();

        if (blocksToDelete.Count > 0)
        {
            _logger.LogInformation("BatchUpdateBlocksAsync: Deleting {DeleteCount} orphaned blocks", blocksToDelete.Count);
            _context.Blocks.RemoveRange(blocksToDelete);
        }

        // Update document timestamp
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null) document.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation("BatchUpdateBlocksAsync: About to SaveChanges - Created: {Created}, Updated: {Updated}, Deleted: {Deleted}",
            createdCount, updatedCount, blocksToDelete.Count);

        await _context.SaveChangesAsync();

        _logger.LogInformation("BatchUpdateBlocksAsync: SaveChanges completed successfully for document {DocumentId}", documentId);

        // Invalidate preview cache
        await _previewCacheService.InvalidateCacheAsync(documentId);

        return resultBlocks.OrderBy(b => b.SortOrder).Select(MapToDto).ToList();
    }

    public async Task<List<BlockDto>> ReorderBlocksAsync(Guid documentId, List<Guid> blockIds)
    {
        var blocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId && blockIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id);

        for (int i = 0; i < blockIds.Count; i++)
        {
            if (blocks.TryGetValue(blockIds[i], out var block))
            {
                block.SortOrder = i;
                block.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Update document timestamp
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null) document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Invalidate preview cache
        await _previewCacheService.InvalidateCacheAsync(documentId);

        return blocks.Values.OrderBy(b => b.SortOrder).Select(MapToDto).ToList();
    }

    // DB-driven per-block type convert. Extracts text from the old content
    // inline in SQL (handles paragraph/heading/equation/code/list items/
    // table cells/theorem/abstract/blockquote shapes) and rebuilds the new
    // content for the target type, preserving the text. Prevents the old
    // behaviour of replacing a table with a blank paragraph — a 1x2
    // "Name: John" table now becomes a paragraph with "Name — John".
    public async Task<BlockDto?> ConvertBlockAsync(Guid documentId, Guid blockId, string newType)
    {
        // Allow-list — matches REASSIGNABLE_TYPES in the frontend. Anything
        // else falls through to the default-content reset (no text carry).
        var textCarrierTypes = new HashSet<string> { "paragraph", "heading", "list", "code", "blockquote", "abstract", "theorem", "equation" };
        var resetsContent = !textCarrierTypes.Contains(newType);

        if (resetsContent)
        {
            var defaultContent = _blockTypeService.GetDefaultContent(newType);
            await _context.Blocks
                .Where(b => b.DocumentId == documentId && b.Id == blockId)
                .ExecuteUpdateAsync(b => b
                    .SetProperty(x => x.Type, newType)
                    .SetProperty(x => x.Content, defaultContent)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
        }
        else
        {
            // One UPDATE: reads old content inline via jsonb operators,
            // flattens to a text scalar, rebuilds the new content shape
            // via jsonb_build_object based on the target type. Zero rows
            // transit .NET memory.
            const string sql = @"
WITH src AS (
  SELECT
    COALESCE(
      NULLIF(content->>'text', ''),
      NULLIF(content->>'title', ''),
      NULLIF(content->>'caption', ''),
      NULLIF(content->>'code', ''),
      NULLIF(content->>'latex', ''),
      NULLIF(content->>'name', ''),
      CASE WHEN jsonb_typeof(content->'items') = 'array'
        THEN (
          SELECT string_agg(item_text, E'\n')
          FROM jsonb_array_elements_text(content->'items') AS item_text
          WHERE item_text <> ''
        )
      END,
      CASE WHEN jsonb_typeof(content->'rows') = 'array'
        THEN (
          SELECT string_agg(cell_text, ' — ')
          FROM jsonb_array_elements(content->'rows') WITH ORDINALITY AS r(row_val, row_idx),
               jsonb_array_elements_text(r.row_val) AS cell_text
          WHERE cell_text <> ''
        )
      END,
      ''
    ) AS text
  FROM blocks
  WHERE document_id = @doc AND id = @id
)
UPDATE blocks b
SET type = @new_type,
    content = CASE @new_type
      WHEN 'paragraph'  THEN jsonb_build_object('text', (SELECT text FROM src))
      WHEN 'heading'    THEN jsonb_build_object('text', (SELECT text FROM src), 'level', 1)
      WHEN 'list'       THEN jsonb_build_object(
                                'items', CASE WHEN (SELECT text FROM src) = '' THEN '[]'::jsonb
                                              ELSE jsonb_build_array((SELECT text FROM src)) END,
                                'ordered', false)
      WHEN 'code'       THEN jsonb_build_object('code', (SELECT text FROM src), 'language', '')
      WHEN 'blockquote' THEN jsonb_build_object('text', (SELECT text FROM src))
      WHEN 'abstract'   THEN jsonb_build_object('text', (SELECT text FROM src))
      WHEN 'theorem'    THEN jsonb_build_object('text', (SELECT text FROM src), 'theoremType', 'theorem', 'title', '', 'label', '')
      WHEN 'equation'   THEN jsonb_build_object('latex', (SELECT text FROM src), 'equationMode', 'display')
      ELSE b.content
    END,
    updated_at = NOW()
WHERE b.document_id = @doc AND b.id = @id;";

            await _context.Database.ExecuteSqlRawAsync(sql,
                new Npgsql.NpgsqlParameter("doc", documentId),
                new Npgsql.NpgsqlParameter("id", blockId),
                new Npgsql.NpgsqlParameter("new_type", newType));
        }

        await TouchDocumentSqlAsync(documentId);
        await _previewCacheService.InvalidateCacheAsync(documentId);

        _logger.LogInformation("ConvertBlockAsync: Block {BlockId} converted to type {NewType} in document {DocumentId}",
            blockId, newType, documentId);

        var updated = await _context.Blocks
            .Where(b => b.DocumentId == documentId && b.Id == blockId)
            .FirstOrDefaultAsync();
        return updated == null ? null : MapToDto(updated);
    }

    // Tier 1 bulk-convert — DB-driven throughout. The block content (JSONB)
    // never transits .NET memory: we pull a tiny type/id projection to
    // decide the heuristic, then do one UPDATE (with a CTE that reads the
    // old content inline) and one bulk DELETE. Three round-trips, zero
    // entity materialisation — consistent with the import pipeline rule.
    public async Task<BatchConvertResultDto?> BatchConvertAsync(Guid documentId, BatchConvertBlocksDto dto)
    {
        if (dto.BlockIds.Count == 0) return null;

        // Tiny projection — types + sort order, no JSONB — to drive the
        // heading-as-label heuristic and existence check.
        var metas = await _context.Blocks
            .Where(b => b.DocumentId == documentId && dto.BlockIds.Contains(b.Id))
            .OrderBy(b => b.SortOrder)
            .Select(b => new { b.Id, b.Type })
            .ToListAsync();

        if (metas.Count != dto.BlockIds.Count) return null;

        switch (dto.Action)
        {
            case "to_list":
                return await ConvertToListSqlAsync(documentId, metas.Select(m => (m.Id, m.Type)).ToList(), ordered: false);
            case "to_ordered_list":
                return await ConvertToListSqlAsync(documentId, metas.Select(m => (m.Id, m.Type)).ToList(), ordered: true);
            case "merge_paragraph":
                return await MergeParagraphSqlAsync(documentId, metas.Select(m => m.Id).ToList());
            case "reheading":
                if (dto.HeadingLevel is null or < 1 or > 6) return null;
                return await ReheadingSqlAsync(documentId, metas.Where(m => m.Type == "heading").Select(m => m.Id).ToList(), dto.HeadingLevel.Value);
            default:
                return null;
        }
    }

    // Pure-SQL fold: CTE reads the old content text columns, aggregates
    // into a jsonb array, and UPDATEs the host block's content + type.
    // Heading-as-label heuristic: when metas[0] is heading and the rest
    // are non-heading, keep metas[0] untouched and fold the tail.
    private async Task<BatchConvertResultDto> ConvertToListSqlAsync(Guid documentId, List<(Guid Id, string Type)> metas, bool ordered)
    {
        var treatFirstAsLabel = metas.Count >= 2
            && metas[0].Type == "heading"
            && metas.Skip(1).All(m => m.Type != "heading");

        var foldIds = (treatFirstAsLabel ? metas.Skip(1) : metas).Select(m => m.Id).ToArray();
        var hostId = foldIds[0];
        var deleteIds = foldIds.Skip(1).ToArray();

        // CTE picks text from the first populated string field per row,
        // aggregates into a jsonb array in sort order, and the UPDATE sets
        // content = { items, ordered }. The fold-set rows are read in SQL
        // only — no EF materialization.
        // When a folded row is already a list, expand its items[] so the
        // merged list inherits every child item instead of flattening to
        // empty. Same pattern used on the import_block_reviews side.
        const string sql = @"
WITH fold AS (
  SELECT id,
         sort_order,
         CASE
           WHEN jsonb_typeof(content->'items') = 'array'
             THEN content->'items'
           ELSE jsonb_build_array(
             COALESCE(
               NULLIF(content->>'text', ''),
               NULLIF(content->>'title', ''),
               NULLIF(content->>'caption', ''),
               NULLIF(content->>'code', ''),
               NULLIF(content->>'latex', ''),
               NULLIF(content->>'name', ''),
               ''
             )
           )
         END AS items_arr
  FROM blocks
  WHERE document_id = @doc AND id = ANY(@fold_ids)
),
flat AS (
  SELECT item_text, f.sort_order, t.ord
  FROM fold f,
       LATERAL jsonb_array_elements_text(f.items_arr) WITH ORDINALITY AS t(item_text, ord)
  WHERE item_text <> ''
)
UPDATE blocks b
SET type = 'list',
    content = jsonb_build_object(
      'items',   COALESCE((SELECT jsonb_agg(to_jsonb(item_text) ORDER BY sort_order, ord) FROM flat), '[]'::jsonb),
      'ordered', @ordered::boolean),
    updated_at = NOW()
WHERE b.document_id = @doc AND b.id = @host;";

        await _context.Database.ExecuteSqlRawAsync(sql,
            new Npgsql.NpgsqlParameter("doc", documentId),
            new Npgsql.NpgsqlParameter("fold_ids", foldIds),
            new Npgsql.NpgsqlParameter("ordered", ordered),
            new Npgsql.NpgsqlParameter("host", hostId));

        if (deleteIds.Length > 0)
        {
            await _context.Blocks
                .Where(b => b.DocumentId == documentId && deleteIds.Contains(b.Id))
                .ExecuteDeleteAsync();
        }

        await TouchDocumentSqlAsync(documentId);
        await _previewCacheService.InvalidateCacheAsync(documentId);
        return await ProjectCreatedAsync(documentId, treatFirstAsLabel ? new[] { metas[0].Id, hostId } : new[] { hostId }, deleteIds);
    }

    private async Task<BatchConvertResultDto> MergeParagraphSqlAsync(Guid documentId, List<Guid> ids)
    {
        var hostId = ids[0];
        var deleteIds = ids.Skip(1).ToArray();

        const string sql = @"
WITH parts AS (
  SELECT sort_order,
         CASE
           WHEN jsonb_typeof(content->'items') = 'array'
             THEN (
               SELECT string_agg(item_text, E'\n')
               FROM jsonb_array_elements_text(content->'items') AS item_text
               WHERE item_text <> ''
             )
           ELSE COALESCE(
             NULLIF(content->>'text', ''),
             NULLIF(content->>'title', ''),
             NULLIF(content->>'caption', ''),
             NULLIF(content->>'code', ''),
             NULLIF(content->>'latex', ''),
             NULLIF(content->>'name', ''),
             ''
           )
         END AS text
  FROM blocks
  WHERE document_id = @doc AND id = ANY(@ids)
)
UPDATE blocks b
SET type = 'paragraph',
    content = jsonb_build_object(
      'text', COALESCE((SELECT string_agg(text, E'\n\n' ORDER BY sort_order) FROM parts WHERE text <> ''), '')),
    updated_at = NOW()
WHERE b.document_id = @doc AND b.id = @host;";

        await _context.Database.ExecuteSqlRawAsync(sql,
            new Npgsql.NpgsqlParameter("doc", documentId),
            new Npgsql.NpgsqlParameter("ids", ids.ToArray()),
            new Npgsql.NpgsqlParameter("host", hostId));

        if (deleteIds.Length > 0)
        {
            await _context.Blocks
                .Where(b => b.DocumentId == documentId && deleteIds.Contains(b.Id))
                .ExecuteDeleteAsync();
        }

        await TouchDocumentSqlAsync(documentId);
        await _previewCacheService.InvalidateCacheAsync(documentId);
        return await ProjectCreatedAsync(documentId, new[] { hostId }, deleteIds);
    }

    // Re-level: one UPDATE that rebuilds content.level for all heading rows
    // in the selection. Text is read inline from the existing content (no
    // .NET materialisation).
    private async Task<BatchConvertResultDto> ReheadingSqlAsync(Guid documentId, List<Guid> headingIds, int level)
    {
        if (headingIds.Count == 0) return new BatchConvertResultDto(Created: new List<BlockDto>(), DeletedIds: new List<Guid>());

        const string sql = @"
UPDATE blocks b
SET content = jsonb_build_object(
      'text', COALESCE(b.content->>'text', ''),
      'level', @level::int),
    updated_at = NOW()
WHERE b.document_id = @doc AND b.id = ANY(@ids) AND b.type = 'heading';";

        await _context.Database.ExecuteSqlRawAsync(sql,
            new Npgsql.NpgsqlParameter("doc", documentId),
            new Npgsql.NpgsqlParameter("ids", headingIds.ToArray()),
            new Npgsql.NpgsqlParameter("level", level));

        await TouchDocumentSqlAsync(documentId);
        await _previewCacheService.InvalidateCacheAsync(documentId);
        return await ProjectCreatedAsync(documentId, headingIds.ToArray(), Array.Empty<Guid>());
    }

    private async Task<BatchConvertResultDto> ProjectCreatedAsync(Guid documentId, Guid[] ids, Guid[] deletedIds)
    {
        var blocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId && ids.Contains(b.Id))
            .OrderBy(b => b.SortOrder)
            .ToListAsync();
        return new BatchConvertResultDto(
            Created: blocks.Select(MapToDto).ToList(),
            DeletedIds: deletedIds.ToList());
    }

    private async Task TouchDocumentSqlAsync(Guid documentId)
    {
        await _context.Documents
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
    }

    private static BlockDto MapToDto(Block b)
    {
        return new BlockDto(
            b.Id,
            b.DocumentId,
            b.Type,
            b.Content.RootElement,
            b.SortOrder,
            b.ParentId,
            b.Depth,
            b.CreatedAt,
            b.UpdatedAt
        );
    }
}
