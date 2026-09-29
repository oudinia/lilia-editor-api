using FluentAssertions;
using Lilia.Core.DTOs;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Services.AskLiliaHarness;

namespace Lilia.Api.Tests.Services;

public class AskLiliaBlockErrorTests
{
    private static readonly Guid Real = Guid.Parse("3f2a9c1e-7b40-4d1a-9a52-0c8e5b6d7f10");
    private static readonly Guid Other = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static (AskLiliaHarness H, DocumentDto Doc, IList<Microsoft.Extensions.AI.AITool> Tools) Setup()
    {
        var h = new AskLiliaHarness();
        var doc = Doc(Block(Real, "heading"), Block(Other));
        return (h, doc, h.Tools(doc, allowWrite: true));
    }

    private static string Err(System.Text.Json.JsonElement e) => e.GetProperty("error").GetString()!;

    [Fact]
    public async Task Get_block_names_the_block_for_a_prefix()
    {
        var (_, _, tools) = Setup();
        var r = await Call(Fn(tools, "get_block"), ("blockId", "3f2a9c1e-7b40"));
        Err(r).Should().Contain(Real.ToString()).And.Contain("heading");
    }

    [Fact]
    public async Task Get_block_names_the_block_when_one_character_is_off()
    {
        var (_, _, tools) = Setup();
        var typo = Real.ToString().Remove(10, 1).Insert(10, "0"); // substitution
        var r = await Call(Fn(tools, "get_block"), ("blockId", typo));
        Err(r).Should().StartWith("block not found").And.Contain($"Did you mean {Real}");
    }

    [Fact]
    public async Task Get_block_points_at_get_outline_when_nothing_is_close()
    {
        var (_, _, tools) = Setup();
        var r = await Call(Fn(tools, "get_block"), ("blockId", Guid.NewGuid().ToString()));
        Err(r).Should().StartWith("block not found").And.Contain("get_outline").And.NotContain("Did you mean");
    }

    [Fact]
    public async Task Invalid_id_says_ids_are_guids_and_hints()
    {
        var (_, _, tools) = Setup();
        var r = await Call(Fn(tools, "get_block"), ("blockId", "intro"));
        Err(r).Should().StartWith("invalid block id 'intro'").And.Contain("get_outline");
    }

    [Fact]
    public async Task Get_lilia_latex_gives_the_same_help()
    {
        var (_, _, tools) = Setup();
        var r = await Call(Fn(tools, "get_lilia_latex"), ("blockId", "3f2a9c1e-7b40-4d1a"));
        Err(r).Should().Contain(Real.ToString());
        var r2 = await Call(Fn(tools, "get_lilia_latex"), ("blockId", "nope"));
        Err(r2).Should().Contain("get_outline");
    }

    [Fact]
    public async Task Edit_and_remove_report_helpfully_when_the_service_finds_nothing()
    {
        var (h, doc, tools) = Setup();
        h.Blocks.Setup(b => b.UpdateBlockAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateBlockDto>()))
            .ReturnsAsync((BlockDto?)null);
        h.Blocks.Setup(b => b.DeleteBlockAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(false);
        var missing = Real.ToString().Remove(35, 1) + "1"; // one char off
        var e = await Call(Fn(tools, "edit_block"), ("blockId", missing), ("content", "{\"text\":\"x\"}"));
        Err(e).Should().Contain($"Did you mean {Real}");
        var d = await Call(Fn(tools, "remove_block"), ("blockId", Guid.NewGuid().ToString()));
        Err(d).Should().Contain("get_outline");
        var bad = await Call(Fn(tools, "remove_block"), ("blockId", "xyz"));
        Err(bad).Should().StartWith("invalid block id");
    }
}
