using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class BlockService : IBlockService
{
    private readonly LiliaDbContext _context;

    public BlockService(LiliaDbContext context)
    {
        _context = context;
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

        return true;
    }

    public async Task<List<BlockDto>> BatchUpdateBlocksAsync(Guid documentId, List<BatchUpdateBlockDto> blocks)
    {
        var blockIds = blocks.Select(b => b.Id).ToList();
        var existingBlocks = await _context.Blocks
            .Where(b => b.DocumentId == documentId && blockIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id);

        foreach (var update in blocks)
        {
            if (existingBlocks.TryGetValue(update.Id, out var block))
            {
                if (update.Type != null) block.Type = update.Type;
                if (update.Content.HasValue) block.Content = JsonDocument.Parse(update.Content.Value.GetRawText());
                if (update.SortOrder.HasValue) block.SortOrder = update.SortOrder.Value;
                if (update.ParentId.HasValue) block.ParentId = update.ParentId.Value;
                if (update.Depth.HasValue) block.Depth = update.Depth.Value;
                block.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Update document timestamp
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null) document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return existingBlocks.Values.OrderBy(b => b.SortOrder).Select(MapToDto).ToList();
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

        return blocks.Values.OrderBy(b => b.SortOrder).Select(MapToDto).ToList();
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
