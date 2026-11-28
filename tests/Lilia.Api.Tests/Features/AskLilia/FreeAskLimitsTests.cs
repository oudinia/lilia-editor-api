using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// Ask Lilia on the Free plan (28 Nov 2026, the owner): 75 credits must not be enough to write or repair a whole
/// document, or Free replaces Pro. So Free answers come from the light model and each answer stops at a per-answer credit
/// limit (the pending tools are refused and the model must answer). The same evening he let a short whole document
/// through: apply_lml is back on Free, and a large rewrite stops at the limit instead of being refused up front.
/// Pro and Beta are unchanged. Nothing is sent to Anthropic.
/// </summary>
public class FreeAskLimitsTests
{
    private static readonly AiSkill Citations = new("lilia-citations", "Citations", "", [], "default", 6);

    private static ActivePlanDto Plan(string slug) => new(Guid.NewGuid(), slug, slug, default(JsonElement), default(JsonElement), "active", null, false);

    /// <param name="plan">The account's plan; null = no plan row, which is Free.</param>
    /// <param name="creditsPerRound">What each 100-token round of the scripted model costs.</param>
    private static AskLiliaService Service(ActivePlanDto? plan, IChatClient? chat = null, int creditsPerRound = 3, bool freeSettings = true)
    {
        var catalog = new Mock<IAiCatalogService>();
        catalog.Setup(c => c.DefaultModelId()).Returns("claude-sonnet-5");
        catalog.Setup(c => c.IsAllowedFor(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        catalog.Setup(c => c.Get(It.IsAny<string>())).Returns<string>(id => new AiModel { Id = id });
        catalog.Setup(c => c.CreditsFor(It.IsAny<string>(), It.IsAny<AiTokenUse>()))
            .Returns<string, AiTokenUse>((_, use) => use.Input / 100 * creditsPerRound);
        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(e => e.GetActivePlanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var settings = new Dictionary<string, string?>
        {
            ["AI:Tiers:fast:Model"] = "claude-haiku-5-5",
            ["AI:Tiers:fast:Effort"] = "low",
        };
        if (freeSettings)
        {
            // As appsettings.json ships them.
            settings["AI:Free:Model"] = "claude-haiku-5-5";
            settings["AI:Free:CreditCapPerAnswer"] = "6";
        }
        var hub = new Mock<IHubContext<Lilia.Api.Hubs.DocumentHub>>();
        return new AskLiliaService(
            chat ?? Mock.Of<IChatClient>(), entitlement.Object, catalog.Object,
            Mock.Of<IAskLiliaRouter>(), Mock.Of<IKbService>(),
            Mock.Of<IDocumentService>(), Mock.Of<IBlockService>(), Mock.Of<IRenderService>(), Mock.Of<IVersionService>(),
            Mock.Of<ILmlTextParser>(), new Lilia.Import.Services.LatexParser(), hub.Object,
            new LiliaDbContext(new DbContextOptionsBuilder<LiliaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
            Options.Create(new AiOptions()), new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<AskLiliaService>.Instance);
    }

    [Fact]
    public async Task A_Free_account_is_answered_by_the_light_model_whatever_the_skill_or_the_model_asked_for()
    {
        (await Service(plan: null).ChooseModelAsync("u1", null, Citations, default)).Should().Be(("claude-haiku-5-5", (string?)null));
        (await Service(Plan("free")).ChooseModelAsync("u1", "claude-sonnet-5", Citations, default)).Should().Be(("claude-haiku-5-5", (string?)null));
    }

    [Theory]
    [InlineData("pro")]
    [InlineData("beta")]
    public async Task Pro_and_Beta_keep_the_skill_s_model(string slug)
    {
        (await Service(Plan(slug)).ChooseModelAsync("u1", null, Citations, default)).Should().Be(("claude-sonnet-5", (string?)null));
        (await Service(Plan(slug)).IsFreeAskAsync("u1", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Without_the_Free_settings_a_Free_account_is_answered_as_before()
    {
        (await Service(plan: null, freeSettings: false).ChooseModelAsync("u1", null, Citations, default))
            .Should().Be(("claude-sonnet-5", (string?)null));
    }

    /// <summary>A model that wants another tool every round, like a whole-document rewrite done block by block.</summary>
    private static ToolCallingChatClient Endless() => new(_ => [ToolCallingChatClient.Call("search_kb", new() { ["query"] = "x" })])
    { Answer = "I fixed the first section." };

    [Fact]
    public async Task A_Free_answer_stops_at_the_credit_limit_and_the_model_must_answer()
    {
        var model = Endless();
        var service = Service(plan: null, model);
        var options = new ChatOptions { Tools = [] };

        var loop = await service.RunToolLoopAsync(
            [new ChatMessage(ChatRole.User, "Rewrite the whole document")], options, [], webSearch: false,
            new AskLiliaService.CompileRepair(new List<string>()), "lilia-polish", null, Guid.NewGuid(), default,
            creditCap: 8, model: "claude-haiku-5-5");

        // 3 credits a round: 3, 6, 9 → the third round reaches 8, its tool call is refused, the fourth is the answer.
        model.ToolModes.Should().HaveCount(4);
        model.ToolModes[^1].Should().BeOfType<NoneChatToolMode>("after the limit the model must answer, not call more tools");
        loop.CapReached.Should().BeTrue();
        loop.PartialApply.Should().BeFalse();
        loop.Response.Text.Should().Be("I fixed the first section.");
    }

    [Fact]
    public async Task A_large_one_step_rewrite_that_costs_more_than_the_limit_is_not_applied()
    {
        // Writing a long document's LML in one round costs more than the limit on its own: the call is refused.
        var model = new ToolCallingChatClient(round => round == 0 ? [ToolCallingChatClient.Call("apply_lml", new() { ["lml"] = "# A long document" })] : [])
        { Answer = "That document is too long for one Free answer." };
        var loop = await Service(plan: null, model, creditsPerRound: 7).RunToolLoopAsync(
            [new ChatMessage(ChatRole.User, "Rewrite the whole document in French")], new ChatOptions { Tools = [] }, [], webSearch: false,
            new AskLiliaService.CompileRepair(new List<string>()), "lilia-polish", null, Guid.NewGuid(), default,
            creditCap: 6, model: "claude-haiku-5-5");

        model.ToolModes.Should().HaveCount(2, "the rewrite round, then the answer");
        model.ToolModes[^1].Should().BeOfType<NoneChatToolMode>();
        loop.CapReached.Should().BeTrue();
        loop.ToolCalls.Should().Be(0, "the refused rewrite was never run");
    }

    [Fact]
    public async Task Without_a_cap_the_loop_runs_as_before()
    {
        var model = new ToolCallingChatClient(round => round < 5 ? [ToolCallingChatClient.Call("search_kb", new() { ["query"] = "x" })] : []);
        var loop = await Service(Plan("pro"), model).RunToolLoopAsync(
            [new ChatMessage(ChatRole.User, "Rewrite the whole document")], new ChatOptions { Tools = [] }, [], webSearch: false,
            new AskLiliaService.CompileRepair(new List<string>()), "lilia-polish", null, Guid.NewGuid(), default);

        model.ToolModes.Should().HaveCount(6);
        model.ToolModes.Should().NotContain(m => m is NoneChatToolMode);
        loop.CapReached.Should().BeFalse();
    }

    [Fact]
    public void The_Free_notes_name_Pro_and_undo_in_each_language()
    {
        AskLiliaService.FreeCapNote("en").Should().Contain("Free plan").And.Contain("Pro").And.Contain("Undo AI changes");
        AskLiliaService.FreeCapNote("fr").Should().Contain("Gratuit").And.Contain("Pro");
        AskLiliaService.FreeCapNote("es").Should().Contain("Gratis").And.Contain("Pro");
        // 28 Nov 2026 evening: a short whole document goes through on Free; only a large one stops, at the limit.
        AskLiliaService.FreeBlockedTools.Should().BeEquivalentTo(["import_latex"]);
        AskLiliaService.FreePlanNote.Should().Contain("apply_lml once").And.Contain("Do not refuse up front").And.Contain("Pro-sized");
    }

    [Fact]
    public void The_whole_document_tools_exist_so_blocking_them_means_something()
    {
        var h = new AskLiliaHarness();
        var names = h.Tools(AskLiliaHarness.Doc(), allowWrite: true).OfType<AIFunction>().Select(f => f.Name).ToList();
        names.Should().Contain(AskLiliaService.FreeBlockedTools);
    }
}
