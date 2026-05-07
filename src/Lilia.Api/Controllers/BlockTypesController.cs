using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class BlockTypesController : ControllerBase
{
    private readonly IBlockTypeService _blockTypeService;
    private readonly IDocumentClassResolver _classResolver;

    public BlockTypesController(IBlockTypeService blockTypeService, IDocumentClassResolver classResolver)
    {
        _blockTypeService = blockTypeService;
        _classResolver = classResolver;
    }

    /// <summary>
    /// Returns the catalog of block types the editor can offer in the slash
    /// menu / ⌘K palette / insertions panel.
    ///
    /// LILIA-121 D1: when <paramref name="docId"/> is supplied the catalog
    /// is filtered against the document's LaTeX class — sectioning blocks
    /// that don't apply to the class are removed (e.g. <c>frontMatter</c>
    /// hidden in articles), and the response wraps the catalog with the
    /// raw allowed-sections list the editor uses to gate heading levels.
    ///
    /// When <paramref name="docId"/> is omitted the response shape stays
    /// the bare list <c>BlockTypeMetadataDto[]</c> (existing behaviour, so
    /// older clients still work). The two response shapes are documented
    /// at <c>BlockTypesEnvelope</c> below.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBlockTypes(
        [FromQuery] string? query,
        [FromQuery] string? category,
        [FromQuery] Guid? docId)
    {
        // No docId → existing behaviour, return the flat list.
        if (!docId.HasValue)
        {
            var blockTypes = string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(category)
                ? _blockTypeService.GetAllBlockTypes()
                : _blockTypeService.SearchBlockTypes(query ?? "", category);
            return Ok(blockTypes);
        }

        // docId supplied → resolve the class and filter. Doc not found =>
        // we still return a useful (unfiltered) response so a stale docId
        // doesn't make the menu vanish entirely.
        var classInfo = await _classResolver.ResolveAsync(docId.Value);
        var allowedSections = classInfo?.AllowedSections ?? [];
        var filtered = _blockTypeService.FilterForDocument(
            allowedSections,
            classInfo?.DocumentClass,
            query,
            category);

        return Ok(new BlockTypesEnvelope
        {
            BlockTypes = filtered.BlockTypes,
            AllowedSections = filtered.AllowedSections,
            DocumentClass = filtered.DocumentClass,
        });
    }

    [HttpGet("{type}")]
    public ActionResult<BlockTypeMetadataDto> GetBlockType(string type)
    {
        var all = _blockTypeService.GetAllBlockTypes();
        var match = all.FirstOrDefault(b => b.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

        if (match == null) return NotFound();
        return Ok(match);
    }
}

/// <summary>
/// Response shape for <c>GET /api/blocktypes?docId=X</c>. Carries both the
/// filtered catalog and the raw allowed-sections list the editor uses to
/// drive heading-level menus. Shipped only when <c>docId</c> is supplied;
/// the bare-list shape is kept for backward compat when it isn't.
/// </summary>
public class BlockTypesEnvelope
{
    public List<BlockTypeMetadataDto> BlockTypes { get; set; } = [];
    public List<string> AllowedSections { get; set; } = [];
    public string? DocumentClass { get; set; }
}
