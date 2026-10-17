using System.Text.Json;
using Lilia.Core.Documents;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Infrastructure.Data;

/// <summary>
/// Refills block_shapes from block_shape_queue (the triggers on blocks and block_validations
/// queue a block on every write, whichever path wrote it: EF, ExecuteUpdate or raw SQL). Reads a
/// block's content here, in the API, and stores only its shape (<see cref="BlockShaper"/>). A
/// queue entry is removed only if it hasn't been queued again since it was read, so a write that
/// lands mid-batch is shaped on the next pass.
/// </summary>
public static class BlockShapeProcessor
{
    public static async Task<int> ProcessAsync(LiliaDbContext db, int batch = 200, CancellationToken ct = default)
    {
        var queued = await db.BlockShapeQueue.AsNoTracking().OrderBy(q => q.QueuedAt).Take(batch).ToListAsync(ct);
        if (queued.Count == 0) return 0;
        var ids = queued.Select(q => q.BlockId).ToList();

        var blocks = await db.Blocks.AsNoTracking().Where(b => ids.Contains(b.Id))
            .Select(b => new { b.Id, b.DocumentId, b.Type, b.Content }).ToListAsync(ct);
        var validations = (await db.BlockValidations.AsNoTracking().Where(v => ids.Contains(v.BlockId))
                .Select(v => new { v.BlockId, v.Status, v.ErrorMessage, v.Warnings, v.ValidatedAt }).ToListAsync(ct))
            .GroupBy(v => v.BlockId).ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.ValidatedAt).First());
        var shapes = await db.BlockShapes.Where(s => ids.Contains(s.BlockId)).ToDictionaryAsync(s => s.BlockId, ct);
        var now = DateTime.UtcNow;

        foreach (var b in blocks)
        {
            BlockShapeInfo info;
            using (b.Content) info = BlockShaper.Shape(b.Type, b.Content.RootElement, b.Id);
            validations.TryGetValue(b.Id, out var v);
            if (!shapes.TryGetValue(b.Id, out var shape))
            {
                shape = new BlockShape { BlockId = b.Id };
                db.BlockShapes.Add(shape);
            }
            shape.DocumentId = b.DocumentId;
            shape.Shape = info.Shape.Length > 200 ? info.Shape[..200] : info.Shape;
            shape.Preview = info.Preview;
            shape.Words = info.Words;
            shape.Characters = info.Characters;
            shape.ValidationStatus = v?.Status;
            shape.ValidationKind = v is null || v.Status == "valid" ? null
                : v.Status == "warning" ? "warning" : BlockShaper.ErrorKind(v.ErrorMessage);
            shape.Warnings = v?.Warnings is { } w && w.RootElement.ValueKind == JsonValueKind.Array ? w.RootElement.GetArrayLength() : 0;
            shape.ComputedAt = now;
            v?.Warnings?.Dispose();
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Blocks deleted meanwhile are simply dequeued (their shape went with them).
        foreach (var q in queued)
            await db.BlockShapeQueue.Where(x => x.BlockId == q.BlockId && x.QueuedAt <= q.QueuedAt).ExecuteDeleteAsync(ct);
        return queued.Count;
    }
}
