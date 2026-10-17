namespace Lilia.Core.Entities;

/// <summary>
/// What the admin console knows of a block (block_shapes, 17 Oct 2026): its shape, sizes, a lorem
/// preview and its validation kind, never its text. Kept as the document lives: a trigger queues
/// the block on every write (<see cref="BlockShapeQueueItem"/>) and the API's worker refills the
/// row; it goes with the block (ON DELETE CASCADE).
/// </summary>
public class BlockShape
{
    public Guid BlockId { get; set; }
    public Guid DocumentId { get; set; }
    public string Shape { get; set; } = "";
    public string? Preview { get; set; }
    public int Words { get; set; }
    public int Characters { get; set; }
    /// <summary>valid, warning or error, from the latest validation; null when never validated.</summary>
    public string? ValidationStatus { get; set; }
    /// <summary>The error reduced to a known kind (BlockShaper.ErrorKind): never the message itself.</summary>
    public string? ValidationKind { get; set; }
    public int Warnings { get; set; }
    public DateTime ComputedAt { get; set; }
}

/// <summary>A block waiting for its shape: queued by a database trigger on blocks and block_validations.</summary>
public class BlockShapeQueueItem
{
    public Guid BlockId { get; set; }
    public DateTime QueuedAt { get; set; }
}
