using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Mcp;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Services.AskLiliaHarness;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Ask Lilia checks its own fixes (15 Oct): check_document in its tool loop, the connector's check
/// (<see cref="DocumentCheckService"/>, not a copy), and the repair loop that stops after three
/// edit-and-check attempts and says what still fails. The model is scripted; the compiler is faked
/// (the RequiresTeX tests compile for real).
/// </summary>
public class AskLiliaCheckDocumentTests : IDisposable
{
    private readonly string _memoDir = Directory.CreateTempSubdirectory("lilia-check-").FullName;
    private readonly Guid _good = Guid.NewGuid();
    private readonly Guid _bad = Guid.NewGuid();

    public void Dispose() => Directory.Delete(_memoDir, true);

    /// <summary>What Lilia emits: a marker before each block, so a line maps to its block. The bad block is line 7.</summary>
    private string Source(string badBody) => $"""
        \documentclass{"{article}"}
        \begin{"{document}"}
        % block:{_good}
        Hello, world.

        % block:{_bad}
        {badBody}

        \end{"{document}"}
        """;

    private (DocumentCheckService Checker, Mock<Lilia.Engines.IRenderService> Render) Checker(DocumentDto doc, string source, ILaTeXRenderService latex)
    {
        var render = new Mock<Lilia.Engines.IRenderService>();
        render.Setup(r => r.RenderToLatexAsync(doc.Id)).ReturnsAsync(source);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:CheckMemoDir"] = _memoDir }).Build();
        var memos = new CheckMemoStore(new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().Object, config, NullLogger<CheckMemoStore>.Instance);
        return (new DocumentCheckService(render.Object, latex, memos, config, NullLogger<DocumentCheckService>.Instance), render);
    }

    private static Mock<ILaTeXRenderService> Compiler(LatexValidationResult result)
    {
        var latex = new Mock<ILaTeXRenderService>();
        latex.Setup(l => l.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(result);
        return latex;
    }

    private static LatexValidationResult Broken(int line) => new(false, "LaTeX compilation failed:\n! Undefined control sequence.",
        [], new LaTeXErrorParser.ParsedLatexError("undefined_control_sequence", @"\badcommand", line, "! Undefined control sequence."), 120, "pdflatex", "");

    private DocumentDto Document() => Doc(Block(_good), Block(_bad));

    [Fact]
    public async Task The_tool_is_offered_and_names_the_block_and_line_of_the_error()
    {
        var doc = Document();
        var latex = Compiler(Broken(7));
        var (checker, _) = Checker(doc, Source(@"\badcommand{x}"), latex.Object);
        var h = new AskLiliaHarness(checker: checker);
        var repair = new AskLiliaService.CompileRepair(new List<string>());

        var tools = h.Tools(doc, allowWrite: false, repair: repair);
        tools.OfType<AIFunction>().Select(f => f.Name).Should().Contain("check_document", "it only reads, so it is there without Edit mode too");

        var r = await Call(Fn(tools, "check_document"));
        r.GetProperty("compiles").GetBoolean().Should().BeFalse();
        r.GetProperty("errorBlockId").GetString().Should().Be(_bad.ToString());
        var problem = r.GetProperty("problems").EnumerateArray().Single();
        problem.GetProperty("blockId").GetString().Should().Be(_bad.ToString());
        problem.GetProperty("line").GetInt32().Should().Be(7);
        problem.GetProperty("severity").GetString().Should().Be("error");
        problem.GetProperty("message").GetString().Should().Contain("Undefined control sequence");
        r.GetProperty("repairAttempts").GetInt32().Should().Be(0, "a check before any edit is the diagnosis, not an attempt");
        r.GetProperty("next").GetString().Should().Contain("does not compile");

        // The same version again: the verdict is kept, nothing is compiled or charged.
        await Call(Fn(tools, "check_document"));
        latex.Verify(l => l.ValidateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        repair.Compiles.Should().Be(1);
    }

    [Fact]
    public void Without_a_checker_the_tool_is_not_offered()
    {
        var doc = Document();
        new AskLiliaHarness().Tools(doc, allowWrite: true).OfType<AIFunction>().Select(f => f.Name)
            .Should().NotContain("check_document");
    }

    [Fact]
    public async Task A_document_that_compiles_reports_clean_with_its_warnings_on_their_blocks()
    {
        var doc = Document();
        var latex = Compiler(new LatexValidationResult(true, null, ["LaTeX Warning: Something odd on input line 4."], null, 90, "pdflatex", ""));
        var (checker, _) = Checker(doc, Source("Fine text."), latex.Object);
        var h = new AskLiliaHarness(checker: checker);
        var repair = new AskLiliaService.CompileRepair(new List<string>());

        var r = await Call(Fn(h.Tools(doc, allowWrite: true, repair: repair), "check_document"));

        r.GetProperty("compiles").GetBoolean().Should().BeTrue();
        r.GetProperty("printsCorrectly").GetBoolean().Should().BeTrue();
        r.GetProperty("errorBlockId").ValueKind.Should().Be(JsonValueKind.Null);
        var warning = r.GetProperty("problems").EnumerateArray().Single();
        warning.GetProperty("severity").GetString().Should().Be("warning");
        warning.GetProperty("blockId").GetString().Should().Be(_good.ToString());
        warning.GetProperty("line").GetInt32().Should().Be(4);
        r.GetProperty("next").GetString().Should().StartWith("It compiles");
        AskLiliaService.CompileVerdict(repair, AskLiliaService.CompileDoctorSkill, "en")
            .Should().Be("✅ **Compile check:** the document compiles.");
    }

    [Fact]
    public async Task The_connector_returns_the_same_check()
    {
        var doc = Document();
        var (checker, _) = Checker(doc, Source(@"\badcommand{x}"), Compiler(Broken(7)).Object);
        var connector = (await checker.CheckAsync(doc)).Report;
        var json = JsonSerializer.SerializeToElement(connector);
        json.GetProperty("errorBlockId").GetString().Should().Be(_bad.ToString());
        json.GetProperty("errorCategory").GetString().Should().Be("undefined_control_sequence");
        json.GetProperty("problems").EnumerateArray().Single().GetProperty("line").GetInt32().Should().Be(7);
    }

    /// <summary>A model that never gives up: it edits the bad block, checks, edits again, forever.</summary>
    private ToolCallingChatClient Stubborn() => new(round => round % 2 == 0
        ? [ToolCallingChatClient.Call("edit_block", new() { ["blockId"] = _bad.ToString(), ["content"] = "{\"text\":\"try " + round + "\"}" })]
        : [ToolCallingChatClient.Call("check_document")])
    { Answer = "I could not fix it." };

    private static Mock<IDocumentCheckService> AlwaysBroken(string blockId)
    {
        var checker = new Mock<IDocumentCheckService>();
        checker.Setup(c => c.CheckAsync(It.IsAny<DocumentDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new DocumentCheck(new { compiles = false }, false, false,
                [new BlockProblem(blockId, 7, "error", "! Undefined control sequence.")], false));
        return checker;
    }

    [Fact]
    public async Task The_fix_stops_after_three_attempts_and_says_which_block_still_fails()
    {
        var doc = Document();
        var model = Stubborn();
        var checker = AlwaysBroken(_bad.ToString());
        var h = new AskLiliaHarness(model, checker.Object);
        h.Blocks.Setup(b => b.UpdateBlockAsync(doc.Id, _bad, It.IsAny<UpdateBlockDto>())).ReturnsAsync(Block(_bad));
        var changed = new List<string>();
        var repair = new AskLiliaService.CompileRepair(changed);
        var tools = h.Tools(doc, allowWrite: true, changed, repair: repair);
        var options = new ChatOptions { Tools = tools };

        var loop = await h.Service.RunToolLoopAsync(
            [new ChatMessage(ChatRole.User, "It won't compile, fix it")], options, tools, webSearch: false, repair,
            AskLiliaService.CompileDoctorSkill, doc.Id, Guid.NewGuid(), default);

        checker.Verify(c => c.CheckAsync(It.IsAny<DocumentDto>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        h.Blocks.Verify(b => b.UpdateBlockAsync(doc.Id, _bad, It.IsAny<UpdateBlockDto>()), Times.Exactly(3));
        repair.Attempts.Should().Be(3);
        repair.Exhausted.Should().BeTrue();
        model.ToolModes.Should().HaveCount(7, "three edits, three checks, then the answer");
        model.ToolModes[^1].Should().BeOfType<NoneChatToolMode>("after the third failed check the model must answer");
        loop.PartialApply.Should().BeFalse();
        loop.Response.Text.Should().Be("I could not fix it.");

        var verdict = AskLiliaService.CompileVerdict(repair, AskLiliaService.CompileDoctorSkill, "en");
        verdict.Should().Contain("still does not compile after 3 attempts").And.Contain(_bad.ToString()).And.Contain("l.7");
        AskLiliaService.CompileVerdict(repair, AskLiliaService.CompileDoctorSkill, "fr").Should().Contain("ne compile toujours pas");
    }

    [Fact]
    public async Task Once_the_attempts_are_used_up_edits_and_checks_are_refused()
    {
        var doc = Document();
        var checker = AlwaysBroken(_bad.ToString());
        var model = new ToolCallingChatClient(round => round == 0
            ? [ToolCallingChatClient.Call("edit_block", new() { ["blockId"] = _bad.ToString(), ["content"] = "{\"text\":\"again\"}" }),
               ToolCallingChatClient.Call("check_document")]
            : []);
        var h = new AskLiliaHarness(model, checker.Object);
        var changed = new List<string> { "a", "b", "c" };
        var repair = new AskLiliaService.CompileRepair(changed)
        {
            Attempts = 3, WritesAtLastCheck = 3,
            Last = new DocumentCheck(new { }, false, false, [new BlockProblem(_bad.ToString(), 7, "error", "boom")], false),
        };
        var tools = h.Tools(doc, allowWrite: true, changed, repair: repair);

        await h.Service.RunToolLoopAsync([new ChatMessage(ChatRole.User, "try again")], new ChatOptions { Tools = tools }, tools,
            false, repair, AskLiliaService.CompileDoctorSkill, doc.Id, Guid.NewGuid(), default);

        h.Blocks.Verify(b => b.UpdateBlockAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<UpdateBlockDto>()), Times.Never);
        checker.Verify(c => c.CheckAsync(It.IsAny<DocumentDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_fix_that_compiles_ends_the_loop_with_the_document_compiling()
    {
        var doc = Document();
        var checks = 0;
        var checker = new Mock<IDocumentCheckService>();
        checker.Setup(c => c.CheckAsync(It.IsAny<DocumentDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++checks == 1
                ? new DocumentCheck(new { compiles = false }, false, false, [new BlockProblem(_bad.ToString(), 7, "error", "boom")], false)
                : new DocumentCheck(new { compiles = true }, true, true, [], false));
        var model = new ToolCallingChatClient(round => round switch
        {
            0 => [ToolCallingChatClient.Call("check_document")],
            1 => [ToolCallingChatClient.Call("edit_block", new() { ["blockId"] = _bad.ToString(), ["content"] = "{\"text\":\"fixed\"}" })],
            2 => [ToolCallingChatClient.Call("check_document")],
            _ => [],
        }) { Answer = "Fixed the command in the second paragraph." };
        var h = new AskLiliaHarness(model, checker.Object);
        h.Blocks.Setup(b => b.UpdateBlockAsync(doc.Id, _bad, It.IsAny<UpdateBlockDto>())).ReturnsAsync(Block(_bad));
        var changed = new List<string>();
        var repair = new AskLiliaService.CompileRepair(changed);
        var tools = h.Tools(doc, allowWrite: true, changed, repair: repair);

        await h.Service.RunToolLoopAsync([new ChatMessage(ChatRole.User, "fix it")], new ChatOptions { Tools = tools }, tools,
            false, repair, AskLiliaService.CompileDoctorSkill, doc.Id, Guid.NewGuid(), default);

        repair.Attempts.Should().Be(1);
        repair.Compiles.Should().Be(2);
        repair.Exhausted.Should().BeFalse();
        model.ToolModes.Should().HaveCount(4).And.NotContain(m => m is NoneChatToolMode);
        AskLiliaService.CompileVerdict(repair, AskLiliaService.CompileDoctorSkill, "es").Should().Contain("el documento compila");
    }

    [Fact]
    public void The_compile_doctor_is_told_the_repair_loop_and_the_others_only_the_tool()
    {
        AskLiliaService.CompileCheckNote(editMode: true, AskLiliaService.CompileDoctorSkill)
            .Should().Contain("check_document").And.Contain("at most 3").And.Contain("whether the document now compiles");
        AskLiliaService.CompileCheckNote(editMode: true, "lilia-polish").Should().NotContain("REPAIR LOOP");
        AiSkillGuidance.Get(AskLiliaService.CompileDoctorSkill).Should().Contain("check_document").And.Contain("at most 3");
    }

    // ── The real compiler (nightly, ops/tex-tests.sh) ──────────────────

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task For_real_a_broken_block_is_named_and_a_clean_document_compiles()
    {
        var latex = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        var broken = Document();
        var (brokenChecker, _) = Checker(broken, Source(@"\notacommand{x}"), latex);
        var b = await brokenChecker.CheckAsync(broken);
        b.Compiles.Should().BeFalse();
        b.Problems.Should().Contain(p => p.Severity == "error" && p.BlockId == _bad.ToString() && p.Line == 7);

        var clean = Document();
        var (cleanChecker, _) = Checker(clean, Source("Fine text."), latex);
        var c = await cleanChecker.CheckAsync(clean);
        c.Compiles.Should().BeTrue();
        c.Problems.Should().NotContain(p => p.Severity == "error");
    }
}
