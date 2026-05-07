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

    /// <summary>
    /// LILIA-121 D1 — apply per-class filtering to the catalog. Pure
    /// function (no DB) so the singleton stays scope-free; callers fetch
    /// the document's class + allowed-sections list separately via
    /// <see cref="IDocumentClassResolver"/> and pipe the results in here.
    ///
    /// When <paramref name="allowedSections"/> is empty (class hasn't been
    /// seeded with sectioning info yet, or "Other" / user-typed class) the
    /// catalog passes through unfiltered — matches the brief's "no docId,
    /// no filter" backward-compat rule.
    /// </summary>
    DocumentBlockTypesResult FilterForDocument(
        IReadOnlyList<string> allowedSections,
        string? documentClass,
        string? query = null,
        string? category = null);
}

/// <summary>
/// Response for <see cref="IBlockTypeService.GetBlockTypesForDocumentAsync"/>.
/// Carries both the filtered catalog AND the raw allowed-sectioning list so
/// the editor can drive heading-level menus without duplicating the lookup.
/// </summary>
public class DocumentBlockTypesResult
{
    public List<BlockTypeMetadataDto> BlockTypes { get; set; } = [];

    /// <summary>
    /// Sectioning slugs allowed for this document's class — e.g.
    /// ["section","subsection","subsubsection","paragraph","subparagraph","part"]
    /// for an article. Empty when the class has no sectioning info seeded.
    /// </summary>
    public List<string> AllowedSections { get; set; } = [];

    /// <summary>The class slug we resolved for this document, for debug.</summary>
    public string? DocumentClass { get; set; }
}
