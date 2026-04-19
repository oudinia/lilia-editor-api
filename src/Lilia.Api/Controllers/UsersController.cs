using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    // Per-user rolling rate limit on the search endpoint. 10 calls per 60s.
    // Keeps email enumeration difficult even if the other privacy guards
    // were somehow bypassed (they shouldn't be — this is defense in depth).
    // In-memory is fine until we horizontally scale; #59 entitlement brick
    // will replace with the entitlement service / Redis counter.
    private static readonly ConcurrentDictionary<string, (int count, DateTime windowStart)> _searchBuckets = new();
    private const int SearchMaxPerMinute = 10;

    private readonly LiliaDbContext _context;
    private readonly ILogger<UsersController> _logger;

    public UsersController(LiliaDbContext context, ILogger<UsersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Privacy-safe user search for the share / invite flows.
    ///
    /// Spec (lilia-docs/specs/sharing-and-notifications.md §Search rules):
    ///   - Result set is restricted to users who already share a document
    ///     or team with the requester. No global user-directory query.
    ///   - Query matches display name (case-insensitive contains) ONLY —
    ///     never email. An email-shaped query returns empty.
    ///   - Response never contains email. Only {userId, displayName,
    ///     avatarUrl} make it out.
    ///   - 10 queries / minute per requester. 11th → 429.
    ///
    /// This is the ONLY way a caller gets user IDs for the new share-by-ID
    /// flow. The email-based invite path (CollaboratorService.InviteByEmailAsync)
    /// stays for when Resend is fully wired.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<UserSearchResult>>> Search([FromQuery] string q, [FromQuery] int limit = 8, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2) return Ok(Array.Empty<UserSearchResult>());
        if (limit < 1 || limit > 20) limit = 8;

        // Email-shaped queries return empty deliberately (anti-enumeration).
        if (q.Contains('@') && q.Contains('.')) return Ok(Array.Empty<UserSearchResult>());

        // Rate limit. Rolling 60s window per user.
        var now = DateTime.UtcNow;
        var bucket = _searchBuckets.AddOrUpdate(userId,
            _ => (1, now),
            (_, prev) =>
            {
                if ((now - prev.windowStart).TotalSeconds >= 60) return (1, now);
                return (prev.count + 1, prev.windowStart);
            });
        if (bucket.count > SearchMaxPerMinute)
        {
            Response.Headers.Append("Retry-After", "60");
            return StatusCode(429, new { error = "rate_limited", limit = SearchMaxPerMinute, windowSeconds = 60 });
        }

        var queryLower = q.Trim().ToLowerInvariant();

        // Scope: users who share a document or group with the requester.
        // Expressed via intermediate ID sets so EF emits clean joins and
        // we never scan the full Users table.
        var myGroupIds = _context.GroupMembers
            .Where(gm => gm.UserId == userId)
            .Select(gm => gm.GroupId);

        var teamPeers = _context.GroupMembers
            .Where(gm => myGroupIds.Contains(gm.GroupId))
            .Select(gm => gm.UserId);

        // Documents where the requester is owner or collaborator.
        var myDocIds = _context.Documents
            .Where(d => d.OwnerId == userId)
            .Select(d => d.Id)
            .Union(_context.DocumentCollaborators
                .Where(dc => dc.UserId == userId)
                .Select(dc => dc.DocumentId));

        // Other participants on those documents.
        var docPeers = _context.DocumentCollaborators
            .Where(dc => myDocIds.Contains(dc.DocumentId))
            .Select(dc => dc.UserId);

        var docOwners = _context.Documents
            .Where(d => myDocIds.Contains(d.Id))
            .Select(d => d.OwnerId);

        var peerIds = teamPeers.Union(docPeers).Union(docOwners).Distinct();

        var results = await _context.Users
            .AsNoTracking()
            .Where(u =>
                u.Id != userId &&
                peerIds.Contains(u.Id) &&
                u.Name != null &&
                EF.Functions.ILike(u.Name!, $"%{queryLower}%"))
            .OrderBy(u => u.Name)
            .Take(limit)
            .Select(u => new UserSearchResult(
                u.Id,
                u.Name ?? "",
                u.Image
            ))
            .ToListAsync(ct);

        return Ok(results);
    }
}

public record UserSearchResult(string UserId, string DisplayName, string? AvatarUrl);
