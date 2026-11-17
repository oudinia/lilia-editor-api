using Lilia.Api.Features.Sharing.Data;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary><see cref="IDocumentComments"/> on Sharing's own context.</summary>
public sealed class DocumentComments(SharingDbContext db) : IDocumentComments
{
    public async Task<(IReadOnlyList<Comment> Comments, int Total, int Resolved)> ListAsync(Guid documentId, bool includeResolved, int take, CancellationToken ct = default)
    {
        var query = db.Comments.AsNoTracking().Include(c => c.Replies).Where(c => c.DocumentId == documentId);
        if (!includeResolved) query = query.Where(c => !c.Resolved);
        var comments = await query.OrderByDescending(c => c.CreatedAt).Take(take).ToListAsync(ct);
        var total = await db.Comments.CountAsync(c => c.DocumentId == documentId, ct);
        var resolved = await db.Comments.CountAsync(c => c.DocumentId == documentId && c.Resolved, ct);
        return (comments, total, resolved);
    }

    public Task<Comment?> FindAsync(Guid documentId, Guid commentId, CancellationToken ct = default) =>
        db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == commentId && c.DocumentId == documentId, ct);

    public async Task<Comment> AddAsync(Guid documentId, Guid? blockId, string userId, string content, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var comment = new Comment
        {
            Id = Guid.NewGuid(), DocumentId = documentId, BlockId = blockId, UserId = userId,
            Content = content, Resolved = false, CreatedAt = now, UpdatedAt = now,
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment;
    }

    public async Task<CommentReply?> ReplyAsync(Guid documentId, Guid commentId, string userId, string content, CancellationToken ct = default)
    {
        if (!await db.Comments.AnyAsync(c => c.Id == commentId && c.DocumentId == documentId, ct)) return null;
        var reply = new CommentReply { Id = Guid.NewGuid(), CommentId = commentId, UserId = userId, Content = content, CreatedAt = DateTime.UtcNow };
        db.CommentReplies.Add(reply);
        await db.SaveChangesAsync(ct);
        return reply;
    }

    public async Task<bool> ResolveAsync(Guid documentId, Guid commentId, CancellationToken ct = default)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId && c.DocumentId == documentId, ct);
        if (comment is null) return false;
        if (!comment.Resolved)
        {
            comment.Resolved = true;
            comment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return true;
    }
}
