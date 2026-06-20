using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for the AiArchitectService response-parsing helpers. These are
/// pure (no AI / DB), covering the robustness of extracting the model's
/// {reply, operations} JSON from prose / fenced output.
/// </summary>
public class AiArchitectServiceTests
{
    [Fact]
    public void ParseResponse_ParsesCleanJson()
    {
        var raw = """
            {"reply":"Here is a skeleton.","operations":[
              {"op":"add","afterId":null,"block":{"type":"abstract","content":{"title":"Abstract","text":"[summary]"}}},
              {"op":"add","afterId":null,"block":{"type":"heading","content":{"text":"Introduction","level":1}}}
            ]}
            """;

        var (reply, ops) = AiArchitectService.ParseResponse(raw);

        reply.Should().Be("Here is a skeleton.");
        ops.Should().HaveCount(2);
        ops[0].Op.Should().Be("add");
        ops[0].Block!.Type.Should().Be("abstract");
        ops[1].Block!.Type.Should().Be("heading");
    }

    [Fact]
    public void ParseResponse_StripsFencesAndSurroundingProse()
    {
        var raw = """
            Sure! Here's my proposal:

            ```json
            {"reply":"Added a methods section.","operations":[{"op":"add","afterId":"abc","block":{"type":"paragraph","content":{"text":"[describe the method]"}}}]}
            ```

            Let me know if you'd like to iterate.
            """;

        var (reply, ops) = AiArchitectService.ParseResponse(raw);

        reply.Should().Be("Added a methods section.");
        ops.Should().HaveCount(1);
        ops[0].AfterId.Should().Be("abc");
    }

    [Fact]
    public void ParseResponse_DropsInvalidOpsAndOutOfVocabTypes()
    {
        var raw = """
            {"reply":"ok","operations":[
              {"op":"frobnicate","id":"x"},
              {"op":"add","block":{"type":"wormhole","content":{}}},
              {"op":"remove","id":"keep-me"},
              {"op":"add","afterId":null,"block":{"type":"equation","content":{"latex":"x=1","displayMode":true}}}
            ]}
            """;

        var (_, ops) = AiArchitectService.ParseResponse(raw);

        ops.Should().HaveCount(2);
        ops.Should().Contain(o => o.Op == "remove" && o.Id == "keep-me");
        ops.Should().Contain(o => o.Op == "add" && o.Block!.Type == "equation");
    }

    [Fact]
    public void ParseResponse_NonJsonBecomesReplyWithNoOps()
    {
        var raw = "What kind of document are you writing — a paper, thesis, or talk?";

        var (reply, ops) = AiArchitectService.ParseResponse(raw);

        reply.Should().Be(raw);
        ops.Should().BeEmpty();
    }

    [Fact]
    public void ExtractJsonObject_HandlesNestedBracesAndStrings()
    {
        var raw = "noise {\"a\":{\"b\":\"}{ not a brace }{\"}} trailing";

        var json = AiArchitectService.ExtractJsonObject(raw);

        json.Should().NotBeNull();
        var doc = JsonDocument.Parse(json!);
        doc.RootElement.GetProperty("a").GetProperty("b").GetString().Should().Be("}{ not a brace }{");
    }

    [Fact]
    public void ExtractJsonObject_ReturnsNullWhenNoObject()
    {
        AiArchitectService.ExtractJsonObject("just prose, no json here").Should().BeNull();
    }
}
