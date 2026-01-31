namespace Lilia.Core.Entities;

public class DocumentLabel
{
    public Guid DocumentId { get; set; }
    public Guid LabelId { get; set; }

    // Navigation properties
    public virtual Document Document { get; set; } = null!;
    public virtual Label Label { get; set; } = null!;
}
