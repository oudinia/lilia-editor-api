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
    private readonly IDocumentSizeService _sizeService;

    public AssetsController(IAssetService assetService, IDocumentService documentService, IDocumentSizeService sizeService)
    {
        _assetService = assetService;
        _documentService = documentService;
        _sizeService = sizeService;
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

    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<AssetDto>> UploadAsset(Guid docId, IFormFile file)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Write))
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File is required" });
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "File exceeds 10MB limit" });
        if (string.IsNullOrEmpty(file.ContentType) || !file.ContentType.StartsWith("image/"))
            return BadRequest(new { message = "Only image files are allowed" });

        await using var stream = file.OpenReadStream();
        try
        {
            var result = await _assetService.UploadAssetAsync(docId, userId, file.FileName, file.ContentType, file.Length, stream);
            return Ok(result);
        }
        catch (AssetTooLargeException ex)
        {
            return StatusCode(413, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Return the document's storage footprint: block bytes, asset bytes,
    /// counts, and an "unusually large" flag. Single SQL aggregate — no
    /// rows transit the app.
    /// </summary>
    [HttpGet("/api/documents/{docId:guid}/size")]
    public async Task<ActionResult<DocumentSizeDto>> GetSize(Guid docId)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _documentService.HasAccessAsync(docId, userId, Permissions.Read))
            return Forbid();

        var size = await _sizeService.GetSizeAsync(docId);
        return Ok(size);
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
