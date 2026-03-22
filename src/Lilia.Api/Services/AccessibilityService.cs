using System.Text.Json;
using Lilia.Core.Interfaces;
using Lilia.Core.Models.MathAst;
using Lilia.Core.Services.Accessibility;

namespace Lilia.Api.Services;

/// <summary>
/// Provides accessibility features: MathML generation, natural-language narration,
/// and block-level accessibility validation.
/// </summary>
public class AccessibilityService : IAccessibilityService
{
    private readonly MathMLGenerator _mathMLGenerator = new();

    public string GenerateMathML(MathNode node)
    {
        return _mathMLGenerator.Generate(node);
    }

    public string NarrateMath(MathNode node)
    {
        return MathNarrator.Narrate(node);
    }

    public List<AccessibilityWarning> ValidateBlock(string blockType, string contentJson)
    {
        var warnings = new List<AccessibilityWarning>();

        // All blocks: warn if content is empty or null
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            warnings.Add(new AccessibilityWarning
            {
                Level = "warning",
                Message = "Block has no content.",
                Field = "content"
            });
            return warnings;
        }

        JsonElement content;
        try
        {
            content = JsonDocument.Parse(contentJson).RootElement;
        }
        catch (JsonException)
        {
            warnings.Add(new AccessibilityWarning
            {
                Level = "error",
                Message = "Block content is not valid JSON.",
                Field = "content"
            });
            return warnings;
        }

        // Normalize block type (handle legacy aliases)
        var normalizedType = blockType?.ToLowerInvariant() switch
        {
            "image" => "figure",
            "quote" => "blockquote",
            "divider" => "pagebreak",
            _ => blockType?.ToLowerInvariant() ?? string.Empty
        };

        switch (normalizedType)
        {
            case "figure":
                ValidateFigureBlock(content, warnings);
                break;
            case "equation":
                ValidateEquationBlock(content, warnings);
                break;
            case "table":
                ValidateTableBlock(content, warnings);
                break;
            case "heading":
                ValidateHeadingBlock(content, warnings);
                break;
        }

        // General: check if textual content is empty
        ValidateContentNotEmpty(normalizedType, content, warnings);

        return warnings;
    }

    private static void ValidateFigureBlock(JsonElement content, List<AccessibilityWarning> warnings)
    {
        var hasAltText = false;

        if (content.TryGetProperty("altText", out var altText) &&
            altText.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(altText.GetString()))
        {
            hasAltText = true;
        }

        // Also check "alt" as a fallback property name
        if (!hasAltText &&
            content.TryGetProperty("alt", out var alt) &&
            alt.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(alt.GetString()))
        {
            hasAltText = true;
        }

        if (!hasAltText)
        {
            warnings.Add(new AccessibilityWarning
            {
                Level = "warning",
                Message = "Figure is missing alt text. Alt text is essential for screen reader users.",
                Field = "altText"
            });
        }

        // Check for missing caption (informational)
        if (!content.TryGetProperty("caption", out var caption) ||
            caption.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(caption.GetString()))
        {
            warnings.Add(new AccessibilityWarning
            {
                Level = "info",
                Message = "Figure has no caption. Captions improve document accessibility.",
                Field = "caption"
            });
        }
    }

    private static void ValidateEquationBlock(JsonElement content, List<AccessibilityWarning> warnings)
    {
        var hasLabel = false;

        if (content.TryGetProperty("label", out var label) &&
            label.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(label.GetString()))
        {
            hasLabel = true;
        }

        if (!hasLabel)
        {
            warnings.Add(new AccessibilityWarning
            {
                Level = "info",
                Message = "Equation has no label. Labels help with cross-referencing and navigation.",
                Field = "label"
            });
        }
    }

    private static void ValidateTableBlock(JsonElement content, List<AccessibilityWarning> warnings)
    {
        // Check for table caption/summary
        if (!content.TryGetProperty("caption", out var caption) ||
            caption.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(caption.GetString()))
        {
            warnings.Add(new AccessibilityWarning
            {
                Level = "info",
                Message = "Table has no caption. Captions help screen reader users understand table purpose.",
                Field = "caption"
            });
        }
    }

    private static void ValidateHeadingBlock(JsonElement content, List<AccessibilityWarning> warnings)
    {
        // Check that heading has text content
        if (content.TryGetProperty("text", out var text) &&
            text.ValueKind == JsonValueKind.String &&
            string.IsNullOrWhiteSpace(text.GetString()))
        {
            warnings.Add(new AccessibilityWarning
            {
                Level = "warning",
                Message = "Heading has empty text. Empty headings disrupt document navigation.",
                Field = "text"
            });
        }
    }

    private static void ValidateContentNotEmpty(string blockType, JsonElement content, List<AccessibilityWarning> warnings)
    {
        // For text-bearing block types, check if primary text field is present
        var textFields = new[] { "text", "content", "latex", "code", "source" };
        var hasAnyText = false;

        foreach (var field in textFields)
        {
            if (content.TryGetProperty(field, out var val) &&
                val.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(val.GetString()))
            {
                hasAnyText = true;
                break;
            }
        }

        // Also check for array-based content (e.g., list items, table rows)
        if (!hasAnyText &&
            content.TryGetProperty("items", out var items) &&
            items.ValueKind == JsonValueKind.Array &&
            items.GetArrayLength() > 0)
        {
            hasAnyText = true;
        }

        if (!hasAnyText &&
            content.TryGetProperty("rows", out var rows) &&
            rows.ValueKind == JsonValueKind.Array &&
            rows.GetArrayLength() > 0)
        {
            hasAnyText = true;
        }

        // Skip this check for structural blocks that don't need text
        var structuralBlocks = new[] { "pagebreak", "tableofcontents" };
        if (!hasAnyText && !structuralBlocks.Contains(blockType))
        {
            // Don't duplicate the "Block has no content" warning
            if (!warnings.Exists(w => w.Field == "content" && w.Message.Contains("no content")))
            {
                warnings.Add(new AccessibilityWarning
                {
                    Level = "warning",
                    Message = "Block appears to have no textual content.",
                    Field = "content"
                });
            }
        }
    }
}
