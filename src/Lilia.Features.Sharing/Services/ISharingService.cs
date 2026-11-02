using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

/// <summary>
/// Owner-scoped aggregation behind the Sharing surface (/shared).
/// The per-document <see cref="ICollaboratorService"/> answers "who is on
/// this document"; this service answers the inverse — "which of my
/// documents are shared, and with whom" — across everything I own.
/// </summary>
public interface ISharingService
{
    Task<List<SharedByMeDto>> GetSharedByMeAsync(string ownerUserId);
    Task<List<SharedPersonDto>> GetPeopleAsync(string ownerUserId);
    Task<InviteResultDto> ResendInviteAsync(string ownerUserId, ResendInviteDto dto);
}
