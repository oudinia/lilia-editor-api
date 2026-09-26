namespace Lilia.Core.Entities;

/// <summary>
/// A document a non-owner has removed from their own list ("Remove from my
/// documents" — Olivia, documents-actions reply, 26 Sep). It deletes nothing
/// and changes nobody else's access: the share stays, the document just stops
/// being listed for this user. Undo removes the row.
///
/// <para>Per user rather than a flag on the collaborator's share, so it works
/// the same whether access comes from an invitation or from a group.</para>
/// </summary>
public class DocumentHide
{
    public string UserId { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public DateTime HiddenAt { get; set; } = DateTime.UtcNow;

    public virtual Document Document { get; set; } = null!;
}
