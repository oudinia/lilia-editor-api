using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/documents/{docId:guid}/[controller]")]
[Authorize]
public class AssetsController : ControllerBase
{
    private readonly IAssetService _assetService;
    private readonly IDocumentService _documentService;

    public AssetsController(IAssetService assetService, IDocumentService documentService)
    {
        _assetService = assetService;
        _documentService = documentService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    [HttpGet]
    public async Task<ActionResult<List<AssetDto>>> GetAssets(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var assets = await _assetService.GetAssetsAsync(docId);
        return Ok(assets);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AssetDto>> GetAsset(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var asset = await _assetService.GetAssetAsync(docId, id);
        if (asset == null) return NotFound();
        return Ok(asset);
    }

    [HttpPost]
    public async Task<ActionResult<AssetUploadDto>> CreateAsset(Guid docId, [FromBody] CreateAssetDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var result = await _assetService.CreateAssetAsync(docId, userId, dto);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteAsset(Guid docId, Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        var result = await _assetService.DeleteAssetAsync(docId, id);
        if (!result) return NotFound();
        return NoContent();
    }
}
