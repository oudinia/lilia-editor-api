using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Lilia.Api.Tests.Integration.References;

/// <summary>
/// GET /api/documents/{id}/references, on real Postgres — it now reads the
/// numbers the last PDF compile kept, and the in-memory provider cannot map
/// this context.
///
/// <para>The behaviour pinned: the index is free and always correct; stored
/// numbers are served with their date and cost no compile; asking for fresh
/// numbers compiles; and a failed compile costs the numbers, not the index.</para>
/// </summary>
[Collection("Integration")]
public class ReferencesControllerTests : IntegrationTestBase
{
    public ReferencesControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private readonly Mock<IBlockService> _blocks = new();
    private readonly Mock<IDocumentService> _documents = new();
    private readonly Mock<IRenderService> _render = new();
    private readonly Mock<ILaTeXRenderService> _latex = new();

    private async Task<(ReferencesController Sut, Guid DocId)> Arrange(string? storedNumbers = null, DateTime? storedAt = null)
    {
        var owner = $"user-{Guid.NewGuid():N}";
        await SeedUserAsync(owner);
        var doc = await SeedDocumentAsync(owner);

        if (storedNumbers is not null)
        {
            await using var db = CreateDbContext();
            var row = await db.Documents.FindAsync(doc.Id);
            row!.LabelNumbers = storedNumbers;
            row.LabelNumbersAt = storedAt;
            await db.SaveChangesAsync();
        }

        _documents.Setup(d => d.HasAccessAsync(doc.Id, owner, It.IsAny<string>())).ReturnsAsync(true);
        _blocks.Setup(b => b.GetBlocksAsync(doc.Id)).ReturnsAsync([
            new BlockDto(Guid.NewGuid(), doc.Id, BlockTypes.Table,
                JsonDocument.Parse("""{"label":"tab:results","caption":"Accuracy"}""").RootElement,
                0, null, 0, DateTime.UtcNow, DateTime.UtcNow),
            new BlockDto(Guid.NewGuid(), doc.Id, BlockTypes.Table,
                JsonDocument.Parse("""{"label":"tab:new","caption":"Added after the compile"}""").RootElement,
                1, null, 0, DateTime.UtcNow, DateTime.UtcNow),
            new BlockDto(Guid.NewGuid(), doc.Id, BlockTypes.Paragraph,
                JsonDocument.Parse("""{"text":"See \\ref{tab:results} and \\ref{fig:gone}."}""").RootElement,
                2, null, 0, DateTime.UtcNow, DateTime.UtcNow),
        ]);

        var sut = new ReferencesController(
            _blocks.Object, _documents.Object, _render.Object, _latex.Object,
            CreateDbContext(), NullLogger<ReferencesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", owner)], "TestAuth")),
                },
            },
        };
        return (sut, doc.Id);
    }

    private static ReferenceReportDto Body(IActionResult r) => (ReferenceReportDto)((OkObjectResult)r).Value!;

    private void NeverCompiles() =>
        _latex.Verify(l => l.RenderToAuxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);

    [Fact]
    public async Task Serves_the_numbers_the_last_compile_kept_without_compiling()
    {
        var at = new DateTime(2026, 9, 25, 14, 6, 0, DateTimeKind.Utc);
        var (sut, doc) = await Arrange(LabelNumbers.FromAux(@"\newlabel{tab:results}{{3}{7}{Accuracy}{table.3}{}}"), at);

        var body = Body(await sut.Get(doc));

        body.Numbered.Should().BeTrue();
        body.NumberedAt.Should().Be(at);
        body.Targets.Single(t => t.Key == "tab:results").Number.Should().Be("3");
        NeverCompiles();
    }

    [Fact]
    public async Task A_target_added_since_the_compile_has_no_number_and_the_date_says_why()
    {
        // The panel's "1 target added since": numbered, dated, and this one
        // simply was not in that compile.
        var (sut, doc) = await Arrange(
            LabelNumbers.FromAux(@"\newlabel{tab:results}{{3}{7}}"), DateTime.UtcNow);

        var body = Body(await sut.Get(doc));

        body.NumberedAt.Should().NotBeNull();
        body.Targets.Single(t => t.Key == "tab:new").Number.Should().BeNull();
    }

    [Fact]
    public async Task With_nothing_kept_the_numbers_are_absent_not_invented()
    {
        var (sut, doc) = await Arrange();

        var body = Body(await sut.Get(doc));

        body.Numbered.Should().BeFalse();
        body.NumberedAt.Should().BeNull();
        body.Targets.Should().OnlyContain(t => t.Number == null);
        body.Problems.Should().ContainSingle(p => p.Kind == "dangling" && p.Key == "fig:gone");
        NeverCompiles();
    }

    [Fact]
    public async Task A_corrupt_stored_row_reads_as_no_numbers_not_as_an_error()
    {
        var (sut, doc) = await Arrange("{not json", DateTime.UtcNow);

        var body = Body(await sut.Get(doc));

        body.Numbered.Should().BeFalse();
        body.Targets.Should().HaveCount(2);
    }

    [Fact]
    public async Task Asking_for_fresh_numbers_compiles_now()
    {
        var (sut, doc) = await Arrange();
        _render.Setup(r => r.RenderToLatexAsync(doc)).ReturnsAsync(@"\documentclass{article}...");
        _latex.Setup(l => l.RenderToAuxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(@"\newlabel{tab:results}{{4}{9}}");

        var body = Body(await sut.Get(doc, numbers: true));

        body.Numbered.Should().BeTrue();
        body.Targets.Single(t => t.Key == "tab:results").Number.Should().Be("4");
    }

    [Fact]
    public async Task A_failed_compile_costs_the_numbers_not_the_index()
    {
        var (sut, doc) = await Arrange();
        _render.Setup(r => r.RenderToLatexAsync(doc)).ReturnsAsync(@"\documentclass{article}...");
        _latex.Setup(l => l.RenderToAuxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("pdflatex not found"));

        var body = Body(await sut.Get(doc, numbers: true));

        body.Numbered.Should().BeFalse();
        body.Targets.Should().HaveCount(2);
        body.Problems.Should().Contain(p => p.Kind == "dangling");
    }

    [Fact]
    public async Task Refuses_a_document_the_caller_cannot_read()
    {
        var (sut, doc) = await Arrange();
        _documents.Setup(d => d.HasAccessAsync(doc, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

        (await sut.Get(doc)).Should().BeOfType<ForbidResult>();
    }
}
