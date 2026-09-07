using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IVersionService
{
    Task<List<VersionListDto>> GetVersionsAsync(Guid documentId);
    Task<VersionDto?> GetVersionAsync(Guid documentId, Guid versionId);
    Task<VersionDto> CreateVersionAsync(Guid documentId, string userId, CreateVersionDto dto);
    Task<DocumentDto?> RestoreVersionAsync(Guid documentId, Guid versionId, string userId);

    /// <summary>
    /// Copy a stored version into a new document, leaving this one untouched.
    ///
    /// <para>Restore is <c>checkout</c>; this is <c>checkout -b</c>. It is the
    /// action that append-on-restore was pretending to be: taking an old state
    /// forward as its own thing, rather than pushing it onto the end of a
    /// history it does not belong at the end of.</para>
    /// </summary>
    Task<DocumentDto?> BranchVersionAsync(Guid documentId, Guid versionId, string userId, string? title);
    Task<bool> DeleteVersionAsync(Guid documentId, Guid versionId, string userId);
    Task CreateAutoVersionAsync(Guid documentId, string userId);
}
