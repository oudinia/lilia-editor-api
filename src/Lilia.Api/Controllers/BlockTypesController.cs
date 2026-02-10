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

    public BlockTypesController(IBlockTypeService blockTypeService)
    {
        _blockTypeService = blockTypeService;
    }

    [HttpGet]
    public ActionResult<List<BlockTypeMetadataDto>> GetBlockTypes([FromQuery] string? query)
    {
        var blockTypes = string.IsNullOrWhiteSpace(query)
            ? _blockTypeService.GetAllBlockTypes()
            : _blockTypeService.SearchBlockTypes(query);

        return Ok(blockTypes);
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
