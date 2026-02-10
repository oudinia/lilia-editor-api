using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;

namespace Lilia.Api.Services;

public class BlockTypeService : IBlockTypeService
{
    private static readonly List<BlockTypeMetadataDto> BlockTypeDefinitions = BuildBlockTypes();

    private static List<BlockTypeMetadataDto> BuildBlockTypes()
    {
        return
        [
            MakeBlockType(BlockTypes.Paragraph, "Paragraph", "Plain text paragraph", "paragraphMark", new { text = "" }),
            MakeBlockType(BlockTypes.Heading, "Heading", "Section heading (H1-H4)", "fontFamily", new { text = "", level = 1 }),
            MakeBlockType(BlockTypes.Equation, "Equation", "LaTeX math equation", "calculator", new { latex = "", displayMode = true }),
            MakeBlockType(BlockTypes.Figure, "Figure", "Image with caption", "image", new { src = "", caption = "", alt = "" }),
            MakeBlockType(BlockTypes.Code, "Code", "Code block with syntax highlighting", "code", new { code = "", language = "javascript" }),
            MakeBlockType(BlockTypes.List, "List", "Numbered or bulleted list", "listOrdered", new { items = Array.Empty<string>(), ordered = false }),
            MakeBlockType(BlockTypes.Blockquote, "Quote", "Block quotation", "rightDoubleQuotes", new { text = "" }),
            MakeBlockType(BlockTypes.Table, "Table", "Data table", "table", new { headers = new[] { "Column 1", "Column 2", "Column 3" }, rows = new[] { new[] { "", "", "" }, new[] { "", "", "" } } }),
            MakeBlockType(BlockTypes.Theorem, "Theorem", "Theorem, definition, lemma, proof", "stickyNote", new { theoremType = "theorem", title = "", text = "", label = "" }),
            MakeBlockType(BlockTypes.Abstract, "Abstract", "Document abstract section", "file", new { title = "Abstract", text = "" }),
            MakeBlockType(BlockTypes.Bibliography, "Bibliography", "Reference list", "book", new { title = "References", style = "apa", entries = Array.Empty<object>() }),
            MakeBlockType(BlockTypes.TableOfContents, "Table of Contents", "Auto-generated contents from headings", "listUnordered", new { title = "Table of Contents" }),
            MakeBlockType(BlockTypes.PageBreak, "Page Break", "Force content to start on new page", "horizontalRule", new { }),
            MakeBlockType(BlockTypes.ColumnBreak, "Column Break", "Force content to next column", "layoutSideByLarge", new { })
        ];
    }

    private static BlockTypeMetadataDto MakeBlockType(string type, string label, string description, string iconName, object defaultContent)
    {
        var json = JsonSerializer.Serialize(defaultContent);
        using var doc = JsonDocument.Parse(json);
        return new BlockTypeMetadataDto(type, label, description, iconName, doc.RootElement.Clone());
    }

    public List<BlockTypeMetadataDto> GetAllBlockTypes()
    {
        return BlockTypeDefinitions;
    }

    public List<BlockTypeMetadataDto> SearchBlockTypes(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BlockTypeDefinitions;

        var q = query.Trim().ToLowerInvariant();
        return BlockTypeDefinitions
            .Where(b =>
                b.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Type.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public JsonDocument GetDefaultContent(string blockType)
    {
        var meta = BlockTypeDefinitions.FirstOrDefault(b =>
            b.Type.Equals(blockType, StringComparison.OrdinalIgnoreCase));

        if (meta == null)
            return JsonDocument.Parse("{}");

        return JsonDocument.Parse(meta.DefaultContent.GetRawText());
    }

    public bool IsValidBlockType(string blockType)
    {
        return BlockTypeDefinitions.Any(b =>
            b.Type.Equals(blockType, StringComparison.OrdinalIgnoreCase));
    }
}
