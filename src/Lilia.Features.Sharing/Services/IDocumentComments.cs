using Lilia.Core.Entities;

namespace Lilia.Api.Services;

/// <summary>
/// A document's comments, for code outside Sharing (the connector's comment tools, 17 Nov 2026): Sharing owns the
/// tables; the others ask it. The rules on who may comment, reply and resolve stay with the caller, as in
/// <c>CommentsController</c>.
/// </summary>
public interface IDocumentComments
{
    /// <summary>The newest comments (open only, unless <paramref name="includeResolved"/>), with their replies; and the counts.</summary>
    Task<(IReadOnlyList<Comment> Comments, int Total, int Resolved)> ListAsync(Guid documentId, bool includeResolved, int take, CancellationToken ct = default);

    /// <summary>One comment of the document, or null.</summary>
    Task<Comment?> FindAsync(Guid documentId, Guid commentId, CancellationToken ct = default);

    Task<Comment> AddAsync(Guid documentId, Guid? blockId, string userId, string content, CancellationToken ct = default);

    /// <summary>A reply to a comment of the document; null when the comment isn't one of its.</summary>
    Task<CommentReply?> ReplyAsync(Guid documentId, Guid commentId, string userId, string content, CancellationToken ct = default);

    /// <summary>Marks the comment resolved; false when it isn't one of the document's.</summary>
    Task<bool> ResolveAsync(Guid documentId, Guid commentId, CancellationToken ct = default);
}
