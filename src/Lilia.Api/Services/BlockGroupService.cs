using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lilia.Api.Services;

public class BlockGroupService : IBlockGroupService
{
    private readonly LiliaDbContext _context;
    private readonly ILogger<BlockGroupService> _logger;

    public BlockGroupService(LiliaDbContext context, ILogger<BlockGroupService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<BlockGroupDto>> GetGroupsAsync(Guid documentId)
    {
        var groups = await _context.BlockGroups
            .Where(g => g.DocumentId == documentId)
            .Include(g => g.Memberships)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync();

        return groups.Select(MapToDto).ToList();
    }

    public async Task<BlockGroupDto?> GetGroupAsync(Guid documentId, Guid groupId)
    {
        var group = await _context.BlockGroups
            .Include(g => g.Memberships)
            .FirstOrDefaultAsync(g => g.DocumentId == documentId && g.Id == groupId);

        return group == null ? null : MapToDto(group);
    }

    public async Task<BlockGroupDto?> CreateGroupAsync(Guid documentId, CreateBlockGroupDto dto)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null) return null;

        if (string.IsNullOrWhiteSpace(dto.Dimension))
            throw new ArgumentException("Dimension is required", nameof(dto));

        var memberIds = dto.MemberBlockIds?.Distinct().ToList() ?? new List<Guid>();

        // Verify the proposed member blocks belong to this document.
        var validBlockCount = await _context.Blocks
            .CountAsync(b => b.DocumentId == documentId && memberIds.Contains(b.Id));
        if (validBlockCount != memberIds.Count)
            throw new ArgumentException("One or more member blocks do not belong to this document");

        // Enforce the one-group-per-dimension-per-block rule. We reject
        // the create rather than auto-evicting from the previous group —
        // the caller is expected to delete or rebuild the conflicting
        // group explicitly. Avoids surprising user-facing data loss.
        var conflictingBlockIds = await _context.BlockGroupMemberships
            .Where(m => memberIds.Contains(m.BlockId)
                     && m.Group.DocumentId == documentId
                     && m.Group.Dimension == dto.Dimension)
            .Select(m => m.BlockId)
            .ToListAsync();
        if (conflictingBlockIds.Count > 0)
            throw new InvalidOperationException(
                $"Blocks already belong to another group in dimension '{dto.Dimension}': "
                + string.Join(", ", conflictingBlockIds));

        var group = new BlockGroup
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Dimension = dto.Dimension,
            Attributes = JsonDocument.Parse(dto.Attributes.GetRawText()),
            Name = dto.Name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        foreach (var blockId in memberIds)
        {
            group.Memberships.Add(new BlockGroupMembership
            {
                BlockId = blockId,
                GroupId = group.Id,
            });
        }

        _context.BlockGroups.Add(group);
        await _context.SaveChangesAsync();

        return MapToDto(group);
    }

    public async Task<BlockGroupDto?> UpdateGroupAsync(Guid documentId, Guid groupId, UpdateBlockGroupDto dto)
    {
        // Load WITHOUT memberships — we rebuild membership separately to
        // avoid EF change-tracker conflicts (ExecuteDeleteAsync removes
        // DB rows but doesn't detach already-tracked entities, so adding
        // a new row with the same PK afterwards throws).
        var group = await _context.BlockGroups
            .FirstOrDefaultAsync(g => g.DocumentId == documentId && g.Id == groupId);
        if (group == null) return null;

        if (dto.Attributes is { } attrs)
        {
            group.Attributes = JsonDocument.Parse(attrs.GetRawText());
        }
        if (dto.Name != null)
        {
            group.Name = dto.Name;
        }

        if (dto.MemberBlockIds is { } memberIds)
        {
            var distinctIds = memberIds.Distinct().ToList();

            // Verify ownership.
            var validBlockCount = await _context.Blocks
                .CountAsync(b => b.DocumentId == documentId && distinctIds.Contains(b.Id));
            if (validBlockCount != distinctIds.Count)
                throw new ArgumentException("One or more member blocks do not belong to this document");

            // Conflict check, excluding memberships in *this* group.
            var conflictingBlockIds = await _context.BlockGroupMemberships
                .Where(m => distinctIds.Contains(m.BlockId)
                         && m.Group.DocumentId == documentId
                         && m.Group.Dimension == group.Dimension
                         && m.GroupId != groupId)
                .Select(m => m.BlockId)
                .ToListAsync();
            if (conflictingBlockIds.Count > 0)
                throw new InvalidOperationException(
                    $"Blocks already belong to another group in dimension '{group.Dimension}': "
                    + string.Join(", ", conflictingBlockIds));

            // Replace membership: delete old DB rows then add new ones.
            // Bulk delete + bulk add rather than diffing — simpler, and
            // membership sets are small in practice. Save attribute /
            // name changes first so we don't end up with a partially-
            // updated group if the membership add fails.
            await _context.SaveChangesAsync();

            await _context.BlockGroupMemberships
                .Where(m => m.GroupId == groupId)
                .ExecuteDeleteAsync();

            foreach (var blockId in distinctIds)
            {
                _context.BlockGroupMemberships.Add(new BlockGroupMembership
                {
                    BlockId = blockId,
                    GroupId = groupId,
                });
            }
        }

        group.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Load memberships for the response.
        await _context.Entry(group).Collection(g => g.Memberships).LoadAsync();
        return MapToDto(group);
    }

    public async Task<bool> DeleteGroupAsync(Guid documentId, Guid groupId)
    {
        var group = await _context.BlockGroups
            .FirstOrDefaultAsync(g => g.DocumentId == documentId && g.Id == groupId);
        if (group == null) return false;

        _context.BlockGroups.Remove(group);
        await _context.SaveChangesAsync();
        return true;
    }

    private static BlockGroupDto MapToDto(BlockGroup g) => new(
        g.Id,
        g.DocumentId,
        g.Dimension,
        JsonDocument.Parse(g.Attributes.RootElement.GetRawText()).RootElement,
        g.Name,
        g.Memberships.Select(m => m.BlockId).ToList(),
        g.CreatedAt,
        g.UpdatedAt
    );
}
