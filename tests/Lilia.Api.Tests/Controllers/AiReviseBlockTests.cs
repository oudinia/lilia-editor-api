using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Engines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Controllers;

/// <summary>
/// POST /api/ai/block/revise — the part worth testing is not that a model was
/// called, but what happens to what it returns: compiled before it is handed
/// over, retried once with the compiler's error, and never reported good on the
/// strength of the model's own say-so.
/// </summary>
public class AiReviseBlockTests
{
    private readonly Mock<IAiService> _ai = new();
    private readonly Mock<IRenderService> _render = new();
    private readonly Mock<ILatexVerifier> _verifier = new();
    private readonly AiController _sut;

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static readonly JsonElement Table = Json("""
        { "caption": "Results", "headers": ["A", "B"], "rows": [["1", "2"]] }
        """);

    public AiReviseBlockTests()
    {
        _sut = new AiController(
            _ai.Object,
            new Mock<ILogger<AiController>>().Object,
            new Mock<IAiCatalogService>().Object,
            new Mock<IEntitlementService>().Object,
            _render.Object,
            _verifier.Object);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user123")], "TestAuth")),
            },
        };

        _render.Setup(r => r.RenderBlockToLatex(It.IsAny<Lilia.Core.Entities.Block>()))
            .Returns("\\begin{tabular}{ll}A & B\\\\\\end{tabular}");
    }

    private void ModelReturns(params (string note, string content)[] turns)
    {
        var i = 0;
        _ai.Setup(a => a.ReviseBlockAsync(
                It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(() =>
            {
                var t = turns[System.Math.Min(i++, turns.Length - 1)];
                return new ReviseBlockResult("table", Json(t.content), t.note);
            });
    }

    private void CompilerSays(params LatexVerdict[] verdicts)
    {
        var i = 0;
        _verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<LatexEngine?>()))
            .ReturnsAsync(() => verdicts[System.Math.Min(i++, verdicts.Length - 1)]);
    }

    private static ReviseBlockResponse Body(IActionResult result) =>
        (ReviseBlockResponse)((OkObjectResult)result).Value!;

    private Task<IActionResult> Revise(bool verify = true) =>
        _sut.ReviseBlock(new ReviseBlockRequest("table", Table, "Add a column for standard deviation.", verify));

    [Fact]
    public async Task Returns_a_verified_block_without_asking_the_model_twice()
    {
        ModelReturns(("Added a column.", """{ "headers": ["A", "B", "SD"] }"""));
        CompilerSays(new LatexVerdict("verified", [], 120, "pdflatex", true));

        var body = Body(await Revise());

        body.Verified.Should().BeTrue();
        body.Error.Should().BeNull();
        body.Attempts.Should().Be(1);
        body.Note.Should().Be("Added a column.");
        _ai.Verify(a => a.ReviseBlockAsync(
            It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task Hands_the_compiler_error_back_to_the_model_and_keeps_the_second_answer()
    {
        ModelReturns(
            ("First try.", """{ "headers": ["A", "B", "SD"] }"""),
            ("Escaped the percent sign.", """{ "headers": ["A", "B", "SD\\%"] }"""));
        CompilerSays(
            new LatexVerdict("failed", ["! You can't use `macro parameter character #'"], 90),
            new LatexVerdict("verified", [], 110, "pdflatex", true));

        var body = Body(await Revise());

        body.Verified.Should().BeTrue();
        body.Attempts.Should().Be(2);
        body.Note.Should().Be("Escaped the percent sign.");
        // The retry is only worth making if the model is told what went wrong.
        _ai.Verify(a => a.ReviseBlockAsync(
            "table", It.IsAny<JsonElement>(), It.IsAny<string>(),
            It.Is<string?>(e => e != null && e.Contains("macro parameter character"))),
            Times.Once);
    }

    [Fact]
    public async Task Returns_the_block_unverified_when_the_second_attempt_fails_too()
    {
        ModelReturns(("Still broken.", """{ "headers": ["A"] }"""));
        CompilerSays(new LatexVerdict("failed", ["! Undefined control sequence."], 90));

        var body = Body(await Revise());

        // Not an error response: the author gets the block and the reason, and
        // decides for themselves. Silence would just hide the work.
        body.Verified.Should().BeFalse();
        body.Error.Should().Contain("Undefined control sequence");
        body.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task An_unreachable_compiler_is_not_a_reason_to_retry()
    {
        ModelReturns(("Added a column.", """{ "headers": ["A", "B", "SD"] }"""));
        CompilerSays(LatexVerdict.Unchecked);

        var body = Body(await Revise());

        // "unchecked" is not "invalid". There is nothing for the model to fix,
        // so asking it again burns tokens and the author's time for nothing.
        body.Verified.Should().BeFalse();
        body.Error.Should().BeNull();
        body.Attempts.Should().Be(1);
        _ai.Verify(a => a.ReviseBlockAsync(
            It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task Skips_the_compiler_when_the_caller_asks_it_to()
    {
        ModelReturns(("Added a column.", """{ "headers": ["A", "B", "SD"] }"""));

        var body = Body(await Revise(verify: false));

        body.Verified.Should().BeFalse();
        _verifier.Verify(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<LatexEngine?>()), Times.Never);
    }

    [Fact]
    public async Task Refuses_an_empty_instruction_rather_than_guessing_at_one()
    {
        var result = await _sut.ReviseBlock(new ReviseBlockRequest("table", Table, "   "));
        result.Should().BeOfType<BadRequestObjectResult>();
        _ai.VerifyNoOtherCalls();
    }
}
