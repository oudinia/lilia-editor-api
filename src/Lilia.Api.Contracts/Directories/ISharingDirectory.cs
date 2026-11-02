namespace Lilia.Api.Directories;

/// <summary>
/// What other contexts may know of Sharing's records (2 Nov 2026, the bounded contexts, step 3): who a
/// document is shared with and at which role, asked here, never by reading the collaborators, invites or
/// group-share tables. Sharing implements it. "Directly" means a share with the person; a share with a
/// group reaches its members too, and is counted only where the method says so.
/// </summary>
public interface ISharingDirectory
{
    /// <summary>Whether a share, with the person or with a group they are in, grants <paramref name="permission"/>.</summary>
    Task<bool> GrantsAsync(Guid documentId, string userId, string permission, CancellationToken ct = default);

    /// <summary>The role the document is shared with the person at, directly; null when it isn't.</summary>
    Task<string?> RoleOfAsync(Guid documentId, string userId, CancellationToken ct = default);

    /// <summary>The roles, by document, among <paramref name="documentIds"/> shared with the person directly.</summary>
    Task<IReadOnlyDictionary<Guid, string>> RolesOfAsync(string userId, IEnumerable<Guid> documentIds, CancellationToken ct = default);

    /// <summary>The documents shared with the person: directly, and, when asked, with a group they are in.</summary>
    Task<IReadOnlyList<Guid>> DocumentsSharedWithAsync(string userId, bool throughGroups, CancellationToken ct = default);

    /// <summary>The people the documents are shared with directly.</summary>
    Task<IReadOnlyList<string>> PeopleSharedWithAsync(IEnumerable<Guid> documentIds, CancellationToken ct = default);

    /// <summary>
    /// Of <paramref name="documentIds"/>, how many are shared directly with someone other than
    /// <paramref name="exceptUserId"/>, and with how many such people.
    /// </summary>
    Task<(int Documents, int People)> SharesOfAsync(IEnumerable<Guid> documentIds, string exceptUserId, CancellationToken ct = default);

    /// <summary>Among <paramref name="userIds"/>, those who have something shared with them directly.</summary>
    Task<IReadOnlySet<string>> WithSharesAsync(IEnumerable<string> userIds, CancellationToken ct = default);
}

/// <summary>
/// Sharing's part in an account's life (2 Nov 2026, the bounded contexts, step 3). Accounts calls it at
/// sign-up and when an account is erased, in its own unit of work.
/// </summary>
public interface ISharingAccounts
{
    /// <summary>
    /// At sign-up: the live invitations sent to <paramref name="email"/> become shares, and each inviter
    /// hears about it. Saved here.
    /// </summary>
    Task ClaimInvitesAsync(string userId, string email, CancellationToken ct = default);

    /// <summary>
    /// When an account is erased: its shares go. Staged on the request's database context, so the caller's
    /// save (and its transaction) removes them with the rest of the account.
    /// </summary>
    Task ForgetAsync(string userId, CancellationToken ct = default);
}
