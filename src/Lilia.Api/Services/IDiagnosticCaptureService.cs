using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IDiagnosticCaptureService
{
    /// <summary>
    /// Persist a new bundle and hand back the ref token. The caller
    /// (controller) supplies the user id; it can be null for
    /// anonymous captures (rare; used only for boot-time errors).
    /// </summary>
    Task<DiagnosticCaptureCreatedDto> CreateAsync(string? userId, CreateDiagnosticCaptureDto dto);

    /// <summary>
    /// Fetch a capture by ref token. Returns null when the token is
    /// unknown OR when the requester is not allowed to read it (the
    /// controller layer decides what role grants read access).
    /// </summary>
    Task<DiagnosticCaptureDto?> GetByRefTokenAsync(string refToken, string? requesterUserId, bool isAdmin);

    /// <summary>
    /// List the requester's own captures, newest first. Used by the
    /// "my captures" tab in the DevTools panel.
    /// </summary>
    Task<List<DiagnosticCaptureDto>> ListMineAsync(string userId, int limit = 20);
}
