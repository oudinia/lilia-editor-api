using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The tool schema is what the model sees. A parameter the description calls
/// optional but the schema lists as required forces the model to invent a value
/// (get_lilia_latex used to demand a blockId for "the whole document").
/// </summary>
public class AskLiliaToolSchemaTests
{
    private static HashSet<string> Required(AIFunction f)
    {
        var schema = f.JsonSchema;
        return schema.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Select(e => e.GetString()!).ToHashSet()
            : new HashSet<string>();
    }

    private static HashSet<string> Properties(AIFunction f) =>
        f.JsonSchema.TryGetProperty("properties", out var p)
            ? p.EnumerateObject().Select(x => x.Name).ToHashSet()
            : new HashSet<string>();

    [Fact]
    public void Optional_parameters_are_not_listed_as_required()
    {
        var h = new AskLiliaHarness();
        var tools = h.Tools(AskLiliaHarness.Doc(), allowWrite: true);

        // tool -> parameters documented as optional ("Omit ...", "Optional ...").
        var optional = new Dictionary<string, string[]>
        {
            ["get_lilia_latex"] = new[] { "blockId" },
            ["apply_lml"] = new[] { "title" },
            ["add_block"] = new[] { "afterId" },
            ["edit_block"] = new[] { "type" },
            ["set_title"] = new[] { "author", "date" },
            ["set_document_kind"] = new[] { "category", "documentClass" },
        };
        foreach (var (tool, names) in optional)
        {
            var f = AskLiliaHarness.Fn(tools, tool);
            var req = Required(f);
            foreach (var n in names)
            {
                Properties(f).Should().Contain(n, $"{tool} should expose {n}");
                req.Should().NotContain(n, $"{tool}.{n} is optional; schema was: {f.JsonSchema}");
            }
        }
    }

    [Fact]
    public void Truly_required_parameters_stay_required()
    {
        var h = new AskLiliaHarness();
        var tools = h.Tools(AskLiliaHarness.Doc(), allowWrite: true);
        Required(AskLiliaHarness.Fn(tools, "get_block")).Should().Contain("blockId");
        Required(AskLiliaHarness.Fn(tools, "edit_block")).Should().Contain(new[] { "blockId", "content" });
        Required(AskLiliaHarness.Fn(tools, "set_title")).Should().Contain("title");
    }
}
