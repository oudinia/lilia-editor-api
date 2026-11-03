using System.Data.Common;

namespace Lilia.Api.Directories;

/// <summary>
/// A person's default team (3 Nov 2026, Teams' bounded context, step 4). Only Teams uses it, but it is kept on
/// the person's account row, so Teams reads and sets it through Accounts instead of the users table.
/// </summary>
public interface ITeamAccounts
{
    /// <summary>The team minted for the person at sign-up, if any.</summary>
    Task<Guid?> DefaultTeamOfAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Makes <paramref name="teamId"/> the person's default team, only while they have none. False when they
    /// already have one or don't exist: two sign-up events for one person then keep one team. Inside
    /// <paramref name="within"/>, the caller's transaction on the request's connection, when it passes one.
    /// </summary>
    Task<bool> ClaimDefaultTeamAsync(string userId, Guid teamId, DbTransaction? within = null, CancellationToken ct = default);
}
