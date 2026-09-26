namespace Lilia.Core.Entities;

/// <summary>
/// A user's favourite among the system snippets. System snippets are one row
/// shared by everyone, so a flag on that row made a favourite everyone's;
/// the favourite is kept per user here instead. A user's own snippets keep
/// <see cref="Snippet.IsFavorite"/>, since only that user sees the row.
/// </summary>
public class SnippetFavorite
{
    public string UserId { get; set; } = string.Empty;
    public Guid SnippetId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Snippet Snippet { get; set; } = null!;
}
