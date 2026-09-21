using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Controllers;

/// <summary>
/// GET /api/documents/{id}/references.
///
/// <para>The behaviour worth pinning is what happens around the compile: the
/// index is free and always correct, numbers cost a LaTeX run, and a failed run
/// must not take the index down with it.</para>
/// </summary>
public class ReferencesControllerTests
{
    private readonly Mock<IBlockService> _blocks = new();
    private readonly Mock<IDocumentService> _documents = new();
    private readonly Mock<IRenderService> _render = new();
    private readonly Mock<ILaTeXRenderService> _latex = new();
    private readonly ReferencesController _sut;
    private static readonly Guid Doc = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public ReferencesControllerTests()
    {
        _sut = new ReferencesController(
            _blocks.Object, _documents.Object, _render.Object, _latex.Object,
            new Mock<ILogger<ReferencesController>>().Object);

        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user123")], "TestAuth")),
            },
        };

        _documents.Setup(d => d.HasAccessAsync(Doc, "user123", It.IsAny<string>())).ReturnsAsync(true);
        _blocks.Setup(b => b.GetBlocksAsync(Doc)).ReturnsAsync([
            new BlockDto(Guid.NewGuid(), Doc, BlockTypes.Table,
                JsonDocument.Parse("""{"label":"tab:results","caption":"Accuracy"}""").RootElement,
                0, null, 0, DateTime.UtcNow, DateTime.UtcNow),
            new BlockDto(Guid.NewGuid(), Doc, BlockTypes.Paragraph,
                JsonDocument.Parse("""{"text":"See \\ref{tab:results} and \\ref{fig:gone}."}""").RootElement,
                1, null, 0, DateTime.UtcNow, DateTime.UtcNow),
        ]);
    }

    private static ReferenceReportDto Body(IActionResult r) =>
        (ReferenceReportDto)((OkObjectResult)r).Value!;

    [Fact]
    public async Task Returns_the_index_without_compiling_anything()
    {
        var body = Body(await _sut.Get(Doc));

        body.Targets.Should().ContainSingle(t => t.Key == "tab:results" && t.Kind == "table");
        body.Uses.Should().HaveCount(2);
        body.Problems.Should().ContainSingle(p => p.Kind == "dangling" && p.Key == "fig:gone");

        // The index is free. Compiling to answer it would make a picker that
        // opens on every keystroke cost a LaTeX run each time.
        _latex.Verify(l => l.RenderToAuxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
        body.Numbered.Should().BeFalse();
        body.Targets[0].Number.Should().BeNull();
    }

    [Fact]
    public async Task Numbers_are_the_ones_latex_assigned()
    {
        _render.Setup(r => r.RenderToLatexAsync(Doc)).ReturnsAsync(@"\documentclass{article}...");
        _latex.Setup(l => l.RenderToAuxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(@"\newlabel{tab:results}{{3}{7}{Accuracy}{table.3}{}}");

        var body = Body(await _sut.Get(Doc, numbers: true));

        body.Numbered.Should().BeTrue();
        body.Targets[0].Number.Should().Be("3");
        body.Targets[0].Page.Should().Be(7);
    }

    [Fact]
    public async Task A_failed_compile_costs_the_numbers_not_the_index()
    {
        _render.Setup(r => r.RenderToLatexAsync(Doc)).ReturnsAsync("\\documentclass{article}...");
        _latex.Setup(l => l.RenderToAuxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("pdflatex not found"));

        var body = Body(await _sut.Get(Doc, numbers: true));

        // The labels and the dangling reference are still correct and still
        // worth having; only the numbers are missing, and Numbered says so.
        body.Numbered.Should().BeFalse();
        body.Targets.Should().ContainSingle();
        body.Problems.Should().ContainSingle(p => p.Kind == "dangling");
    }

    [Fact]
    public async Task Refuses_a_document_the_caller_cannot_read()
    {
        _documents.Setup(d => d.HasAccessAsync(Doc, "user123", It.IsAny<string>())).ReturnsAsync(false);

        (await _sut.Get(Doc)).Should().BeOfType<ForbidResult>();
        _blocks.Verify(b => b.GetBlocksAsync(It.IsAny<Guid>()), Times.Never);
    }
}
