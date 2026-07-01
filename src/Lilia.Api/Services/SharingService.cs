using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class SharingService : ISharingService
{
    private readonly LiliaDbContext _context;
    private readonly ICollaboratorService _collaboratorService;

    // Avatar-stack cap for the "Shared by me" card; counts carry the rest.
    private const int TopCollaborators = 5;

    public SharingService(LiliaDbContext context, ICollaboratorService collaboratorService)
    {
        _context = context;
        _collaboratorService = collaboratorService;
    }

    public async Task<List<SharedByMeDto>> GetSharedByMeAsync(string ownerUserId)
    {
        var docs = await _context.Documents
            .Where(d => d.OwnerId == ownerUserId
                && d.DeletedAt == null
                && !d.IsTemplate
                && !d.IsPlayground)
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.UpdatedAt,
                d.LastOpenedAt,
                d.IsPublic,
                d.ShareLink
            })
            .ToListAsync();

        var docIds = docs.Select(d => d.Id).ToList();

        var collaborators = await _context.DocumentCollaborators
            .Where(c => docIds.Contains(c.DocumentId))
            .Select(c => new
            {
                c.DocumentId,
                c.UserId,
                c.User.Name,
                c.User.Email,
                c.User.Image,
                Role = c.Role.Name,
                c.CreatedAt
            })
            .ToListAsync();

        var pendingCounts = await _context.DocumentPendingInvites
            .Where(p => docIds.Contains(p.DocumentId) && p.Status == "pending")
            .GroupBy(p => p.DocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Count);

        var collabByDoc = collaborators
            .GroupBy(c => c.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.CreatedAt).ToList());

        var result = new List<SharedByMeDto>();
        foreach (var d in docs)
        {
            var docCollabs = collabByDoc.GetValueOrDefault(d.Id) ?? new();
            var pendingCount = pendingCounts.GetValueOrDefault(d.Id, 0);

            // A document surfaces here only if it's exposed to someone —
            // a public link, an explicit collaborator, or a pending invite.
            if (!d.IsPublic && docCollabs.Count == 0 && pendingCount == 0)
                continue;

            result.Add(new SharedByMeDto(
                d.Id,
                d.Title,
                d.UpdatedAt,
                d.LastOpenedAt,
                d.IsPublic,
                d.ShareLink != null,
                docCollabs.Count,
                pendingCount,
                docCollabs
                    .Take(TopCollaborators)
                    .Select(c => new SharedCollaboratorSummaryDto(c.UserId, c.Name, c.Email, c.Image, c.Role))
                    .ToList()
            ));
        }

        return result
            .OrderByDescending(r => r.LastOpenedAt ?? r.UpdatedAt)
            .ToList();
    }

    public async Task<List<SharedPersonDto>> GetPeopleAsync(string ownerUserId)
    {
        var activeRows = await _context.DocumentCollaborators
            .Where(c => c.Document.OwnerId == ownerUserId
                && c.Document.DeletedAt == null
                && !c.Document.IsTemplate
                && !c.Document.IsPlayground)
            .Select(c => new
            {
                c.UserId,
                c.User.Name,
                c.User.Email,
                c.User.Image,
                c.DocumentId,
                Title = c.Document.Title,
                Role = c.Role.Name
            })
            .ToListAsync();

        var pendingRows = await _context.DocumentPendingInvites
            .Where(p => p.Status == "pending"
                && p.Document.OwnerId == ownerUserId
                && p.Document.DeletedAt == null
                && !p.Document.IsTemplate
                && !p.Document.IsPlayground)
            .Select(p => new
            {
                p.Email,
                p.DocumentId,
                Title = p.Document.Title,
                p.Role
            })
            .ToListAsync();

        // Merge by email (case-insensitive): a registered collaborator and a
        // lingering pending row for the same address are one person.
        var people = new Dictionary<string, PersonAccumulator>(StringComparer.OrdinalIgnoreCase);

        PersonAccumulator ForKey(string key, string email)
        {
            if (!people.TryGetValue(key, out var acc))
            {
                acc = new PersonAccumulator { Key = key, Email = email };
                people[key] = acc;
            }
            return acc;
        }

        foreach (var row in activeRows)
        {
            // Registered collaborators always carry an email; fall back to a
            // user-id key on the off chance one doesn't.
            var email = row.Email ?? string.Empty;
            var key = !string.IsNullOrEmpty(email) ? email : $"uid:{row.UserId}";
            var acc = ForKey(key, email);
            acc.UserId ??= row.UserId;
            acc.Name ??= row.Name;
            acc.Image ??= row.Image;
            acc.SetDoc(row.DocumentId, row.Title, row.Role, "active");
        }

        foreach (var row in pendingRows)
        {
            var acc = ForKey(row.Email, row.Email);
            // "active" wins over "pending" for the same (person, document),
            // so an accepted collaborator with a stale pending row reads as active.
            acc.SetDoc(row.DocumentId, row.Title, row.Role, "pending");
        }

        return people.Values
            .Select(p => p.ToDto())
            .OrderByDescending(p => p.Status == "active")
            .ThenByDescending(p => p.DocumentCount)
            .ThenBy(p => p.Name ?? p.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<InviteResultDto> ResendInviteAsync(string ownerUserId, ResendInviteDto dto)
    {
        var doc = await _context.Documents.FindAsync(dto.DocumentId);
        if (doc == null)
            return new InviteResultDto(false, false, dto.Email, "Document not found.");
        if (doc.OwnerId != ownerUserId)
            return new InviteResultDto(false, false, dto.Email, "Only the owner can resend invitations.");

        var invite = await _context.DocumentPendingInvites
            .FirstOrDefaultAsync(p => p.DocumentId == dto.DocumentId
                && p.Email == dto.Email
                && p.Status == "pending");
        if (invite == null)
            return new InviteResultDto(false, false, dto.Email, "No pending invitation found for that address.");

        // Reuse the invite path — it refreshes the pending row's expiry and
        // re-sends the same email, and enforces owner-only itself.
        return await _collaboratorService.InviteByEmailAsync(
            dto.DocumentId, ownerUserId, new InviteCollaboratorDto(dto.Email, invite.Role));
    }

    // Mutable in-flight accumulator; collapsed to SharedPersonDto at the end.
    private sealed class PersonAccumulator
    {
        public string Key = string.Empty;
        public string Email = string.Empty;
        public string? UserId;
        public string? Name;
        public string? Image;
        private readonly Dictionary<Guid, SharedPersonDocDto> _docs = new();

        public void SetDoc(Guid docId, string title, string role, string status)
        {
            // Active always wins; never downgrade an active row to pending.
            if (_docs.TryGetValue(docId, out var existing) && existing.Status == "active")
                return;
            _docs[docId] = new SharedPersonDocDto(docId, title, role, status);
        }

        public SharedPersonDto ToDto()
        {
            var docs = _docs.Values.ToList();
            var status = docs.Any(d => d.Status == "active") ? "active" : "pending";
            return new SharedPersonDto(Key, UserId, Name, Email, Image, status, docs.Count, docs);
        }
    }
}
