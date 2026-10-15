using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// A paragraph's inline marks are already Markdown's: <c>**bold**</c> and <c>*italic*</c>, as the
/// LaTeX (<c>\textbf</c>, <c>\textit</c>) and HTML (<c>strong</c>, <c>em</c>) exports read them.
/// The Markdown export rewrote <c>*x*</c> to <c>**x**</c>, so italic came out bold and bold came out
/// <c>***bold***</c> (bold italic). Found by the e2e export spec, 15 Oct 2026.
/// </summary>
public class MarkdownInlineMarksTests
{
    private static readonly RenderService Render = new(
        new LiliaDbContext(new DbContextOptionsBuilder<LiliaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        NullLogger<RenderService>.Instance);

    private static string Paragraph(string text) => Render.RenderBlockToMarkdown(new Block
    {
        Id = Guid.NewGuid(),
        Type = "paragraph",
        Content = JsonDocument.Parse(JsonSerializer.Serialize(new { text })),
    });

    [Fact]
    public void Bold_stays_bold() =>
        Paragraph("Plain prose with **bold** words.").Should().Be("Plain prose with **bold** words.");

    [Fact]
    public void Italic_stays_italic() =>
        Paragraph("A *quiet* word.").Should().Be("A *quiet* word.");

    [Fact]
    public void Bold_and_italic_side_by_side() =>
        Paragraph("Both **loud** and *quiet*.").Should().Be("Both **loud** and *quiet*.");

    [Fact]
    public void Math_and_code_are_left_alone() =>
        Paragraph("Area $a*b*c$ and `x*y*z`.").Should().Be("Area $a*b*c$ and `x*y*z`.");
}
