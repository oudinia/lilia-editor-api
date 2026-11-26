using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lilia.Api.Mcp;

/// <summary>
/// Corrections to the tool list the connector publishes (api#700, Claude Connectors Directory tool check, 26 Nov 2026).
///
/// <c>set_document_settings</c> takes <c>settings</c> as a <see cref="JsonElement"/>, so that the stringified object some
/// clients send still works (the tool parses it). A <see cref="JsonElement"/> parameter is published without a
/// <c>type</c>, and the directory's check asks for one: the published schema says <c>"type": "object"</c> and lists the
/// fields, while the tool itself stays lenient about the string form.
/// </summary>
public static class McpToolSchemaFixes
{
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Filter(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) => async (request, ct) =>
    {
        var result = await next(request, ct);
        foreach (var tool in result.Tools)
        {
            if (tool.Name == "set_document_settings") tool.InputSchema = TypeSettings(tool.InputSchema);
        }
        return result;
    };

    /// <summary>The settings parameter as an object whose properties are the settings Lilia knows (extra keys are refused by the tool).</summary>
    internal static JsonElement TypeSettings(JsonElement schema)
    {
        var root = JsonNode.Parse(schema.GetRawText())!.AsObject();
        if (root["properties"]?["settings"] is JsonObject settings && settings["type"] is null)
        {
            settings["type"] = "object";
            var props = new JsonObject();
            foreach (var name in LiliaMcpWriteTools.SettingNamesForSchema)
                props[name] = new JsonObject { ["type"] = LiliaMcpWriteTools.IsNumericSetting(name) ? new JsonArray("number", "null") : new JsonArray("string", "number", "boolean", "null") };
            settings["properties"] = props;
            settings["minProperties"] = 1;
        }
        return JsonSerializer.SerializeToElement(root);
    }
}
