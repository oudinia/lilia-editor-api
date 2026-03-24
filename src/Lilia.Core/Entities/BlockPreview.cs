namespace Lilia.Core.Entities;

public class BlockPreview
{
    public Guid Id { get; set; }
    public Guid BlockId { get; set; }
    public string Format { get; set; } = "html";
    public byte[]? Data { get; set; }
    public DateTime RenderedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual Block Block { get; set; } = null!;
}
