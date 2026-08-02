using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// The document title must survive every export.
///
/// It did not: neither the LML nor the Markdown dispatch had a "title" arm, so
/// a title block fell through to the default. LML emitted a bare "@title" —
/// syntactically valid, with the title, author and date gone — and Markdown
/// emitted an HTML comment. Export a document, re-import it, and it was
/// untitled.
///
/// This is not a de-scoped edge case. Every document has a title.
/// </summary>
public class TitleRoundTripTests
{
    // RenderService needs a DbContext for other work; the block renderers are
    // pure, so an in-memory context is enough and keeps these tests fast.
    private static RenderService Sut()
    {
        var opts = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RenderService(new LiliaDbContext(opts), NullLogger<RenderService>.Instance);
    }

    private static Block TitleBlock(string title, string author, string date) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        Type = "title",
        SortOrder = 0,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(new { title, author, date })),
    };

    [Fact]
    public void Lml_export_keeps_the_title_author_and_date()
    {
        var lml = Sut().RenderBlockToLml(
            TitleBlock("On the Origin of Blocks", "O. Dinia", "July 2026"));

        lml.Should().StartWith("@title");
        lml.Should().Contain("On the Origin of Blocks");
        lml.Should().Contain("O. Dinia");
        lml.Should().Contain("July 2026");
    }

    [Fact]
    public void Markdown_export_keeps_the_title_author_and_date()
    {
        var md = Sut().RenderBlockToMarkdown(
            TitleBlock("On the Origin of Blocks", "O. Dinia", "July 2026"));

        md.Should().Contain("# On the Origin of Blocks");
        md.Should().Contain("O. Dinia");
        md.Should().Contain("July 2026");
    }

    [Fact]
    public void A_title_with_only_a_title_does_not_emit_empty_attributes()
    {
        var lml = Sut().RenderBlockToLml(TitleBlock("Just a Title", "", ""));

        lml.Should().Contain("Just a Title");
        lml.Should().NotContain("author=");
        lml.Should().NotContain("date=");
    }

    /// <summary>
    /// The other half of the fix: when a type genuinely is unsupported, say so.
    /// A plausible-looking bare "@type" is worse than an honest note, because
    /// nothing downstream can tell that anything was lost.
    /// </summary>
    [Fact]
    public void An_unsupported_block_type_names_the_gap_instead_of_emitting_a_bare_marker()
    {
        var exotic = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "somethingnobodysupports",
            SortOrder = 0,
            Content = JsonDocument.Parse("""{"text":"content that must not vanish silently"}"""),
        };

        var lml = Sut().RenderBlockToLml(exotic);

        lml.Should().StartWith("%", "an unsupported type must be a comment, not valid-looking LML");
        lml.Should().Contain("not supported in LML");
        lml.Should().Contain("somethingnobodysupports", "the reader needs to know which type was dropped");
    }
}
