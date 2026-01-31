using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/documents/{docId:guid}/[controller]")]
[Authorize]
public class CollaboratorsController : ControllerBase
{
    private readonly ICollaboratorService _collaboratorService;
    private readonly IDocumentService _documentService;

    public CollaboratorsController(ICollaboratorService collaboratorService, IDocumentService documentService)
    {
        _collaboratorService = collaboratorService;
        _documentService = documentService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<CollaboratorListDto>> GetCollaborators(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var collaborators = await _collaboratorService.GetCollaboratorsAsync(docId);
        return Ok(collaborators);
    }

    [HttpPost("users")]
    public async Task<ActionResult<UserCollaboratorDto>> AddUserCollaborator(Guid docId, [FromBody] AddUserCollaboratorDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Manage))
            return Forbid();

        var collaborator = await _collaboratorService.AddUserCollaboratorAsync(docId, userId, dto);
        if (collaborator == null) return NotFound();
        return Ok(collaborator);
    }

    [HttpPost("groups")]
    public async Task<ActionResult<GroupCollaboratorDto>> AddGroupCollaborator(Guid docId, [FromBody] AddGroupCollaboratorDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Manage))
            return Forbid();

        var collaborator = await _collaboratorService.AddGroupCollaboratorAsync(docId, userId, dto);
        if (collaborator == null) return NotFound();
        return Ok(collaborator);
    }

    [HttpPut("users/{targetUserId}")]
    public async Task<ActionResult<UserCollaboratorDto>> UpdateUserCollaboratorRole(Guid docId, string targetUserId, [FromBody] UpdateCollaboratorRoleDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Manage))
            return Forbid();

        var collaborator = await _collaboratorService.UpdateUserCollaboratorRoleAsync(docId, targetUserId, userId, dto);
        if (collaborator == null) return NotFound();
        return Ok(collaborator);
    }

    [HttpPut("groups/{groupId:guid}")]
    public async Task<ActionResult<GroupCollaboratorDto>> UpdateGroupCollaboratorRole(Guid docId, Guid groupId, [FromBody] UpdateCollaboratorRoleDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Manage))
            return Forbid();

        var collaborator = await _collaboratorService.UpdateGroupCollaboratorRoleAsync(docId, groupId, userId, dto);
        if (collaborator == null) return NotFound();
        return Ok(collaborator);
    }

    [HttpDelete("users/{targetUserId}")]
    public async Task<ActionResult> RemoveUserCollaborator(Guid docId, string targetUserId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Manage))
            return Forbid();

        var result = await _collaboratorService.RemoveUserCollaboratorAsync(docId, targetUserId, userId);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpDelete("groups/{groupId:guid}")]
    public async Task<ActionResult> RemoveGroupCollaborator(Guid docId, Guid groupId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Manage))
            return Forbid();

        var result = await _collaboratorService.RemoveGroupCollaboratorAsync(docId, groupId, userId);
        if (!result) return NotFound();
        return NoContent();
    }
}
