using System.Text.Json;
using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IBlockTypeService
{
    List<BlockTypeMetadataDto> GetAllBlockTypes();
    List<BlockTypeMetadataDto> GetBlockTypesByCategory(string category);
    List<BlockTypeMetadataDto> SearchBlockTypes(string query, string? category = null);
    JsonDocument GetDefaultContent(string blockType);
    bool IsValidBlockType(string blockType);
}
