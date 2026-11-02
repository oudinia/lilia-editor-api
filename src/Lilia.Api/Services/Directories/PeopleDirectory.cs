using Lilia.Api.Directories;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services.Directories;

/// <summary>Accounts' answer to <see cref="IPeopleDirectory"/>: read-only, untracked.</summary>
public sealed class PeopleDirectory(LiliaDbContext db) : IPeopleDirectory
{
    public Task<PersonCard?> FindAsync(string userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new PersonCard(u.Id, u.Name, u.Email, u.Image, u.Locale))
            .FirstOrDefaultAsync(ct);

    public Task<PersonCard?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        db.Users.AsNoTracking()
            .Where(u => u.Email == email)
            .Select(u => new PersonCard(u.Id, u.Name, u.Email, u.Image, u.Locale))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<string, PersonCard>> FindManyAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, PersonCard>();
        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new PersonCard(u.Id, u.Name, u.Email, u.Image, u.Locale))
            .ToDictionaryAsync(p => p.Id, ct);
    }
}
