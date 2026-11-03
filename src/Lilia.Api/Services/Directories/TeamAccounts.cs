using System.Data.Common;
using Lilia.Api.Directories;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lilia.Api.Services.Directories;

/// <summary>Accounts' answer to <see cref="ITeamAccounts"/>: the default team kept on the person's row.</summary>
public sealed class TeamAccounts(LiliaDbContext db) : ITeamAccounts
{
    public Task<Guid?> DefaultTeamOfAsync(string userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.DefaultTeamId)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> ClaimDefaultTeamAsync(string userId, Guid teamId, DbTransaction? within = null, CancellationToken ct = default)
    {
        if (within is not null && db.Database.CurrentTransaction?.GetDbTransaction() != within)
            await db.Database.UseTransactionAsync(within, ct);
        var now = DateTime.UtcNow;
        return await db.Users
            .Where(u => u.Id == userId && u.DefaultTeamId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DefaultTeamId, teamId).SetProperty(u => u.UpdatedAt, now), ct) == 1;
    }
}
