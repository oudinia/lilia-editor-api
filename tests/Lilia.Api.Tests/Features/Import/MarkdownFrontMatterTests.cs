using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Import.Services;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// A Markdown file's YAML front matter is metadata, not a heading (the launch bot's TeX Stack Exchange run,
/// 14 Nov 2026, #106: "1 title: … author: Me date: … header-includes: - …" as one heading, and the isodate
/// line never reached the preamble). The source is the one the bot imported.
/// </summary>
public class MarkdownFrontMatterTests : IntegrationTestBase
{
    public MarkdownFrontMatterTests(TestDatabaseFixture fixture) : base(fixture) { }

    private const string Pandoc = """
        ---
        title: "SE 767765 pandoc today date format"
        author: Me
        date: \today
        header-includes:
          - \usepackage[USenglish,iso]{isodate}
        ---

        Hello.
        """;

    [Fact]
    public void The_front_matter_is_read_and_the_body_starts_after_it()
    {
        var front = MarkdownFrontMatter.Read(Pandoc);
        front.Found.Should().BeTrue();
        front.Title.Should().Be("SE 767765 pandoc today date format");
        front.Author.Should().Be("Me");
        front.Date.Should().Be(@"\today");
        front.HeaderIncludes.Should().Equal(@"\usepackage[USenglish,iso]{isodate}");
        front.Body.Trim().Should().Be("Hello.");
    }

    [Fact]
    public void Lists_block_scalars_and_raw_latex_marks_are_read()
    {
        var front = MarkdownFrontMatter.Read("""
            ---
            title: A
            author:
              - Ann
              - Bob
            header-includes: |
              ```{=latex}
              \usepackage{xcolor}
              ```
            ---
            Body
            """);
        front.Author.Should().Be("Ann and Bob");
        front.HeaderIncludes.Should().Equal(@"\usepackage{xcolor}");
    }

    [Theory]
    [InlineData("Text\n---\nMore")]                 // a rule after text: Markdown, not front matter
    [InlineData("---\nNot yaml at all\n---\nBody")]   // a rule, a line, a rule
    [InlineData("---\ntitle: x\nno closing line")]
    public void Anything_else_is_left_as_it_was(string markdown)
    {
        var front = MarkdownFrontMatter.Read(markdown);
        front.Found.Should().BeFalse();
        front.Body.Should().Be(markdown);
    }

    [Fact]
    public async Task A_Markdown_import_gets_its_title_block_and_preamble_and_no_heading()
    {
        const string user = "md-front-matter";
        await SeedUserAsync(user);
        var client = CreateClientAs(user);
        var response = await client.PostAsJsonAsync("/api/lilia/jobs/import",
            new { content = Pandoc, format = "MARKDOWN", filename = "se767765.md", skipReview = true });
        response.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("document").GetProperty("id").GetGuid();

        await using var db = CreateDbContext();
        var blocks = await db.Blocks.Where(b => b.DocumentId == id).OrderBy(b => b.SortOrder).ToListAsync();
        blocks.Select(b => b.Type).Should().Equal("title", "paragraph");
        var title = blocks[0].Content.RootElement;
        title.GetProperty("title").GetString().Should().Be("SE 767765 pandoc today date format");
        title.GetProperty("author").GetString().Should().Be("Me");
        title.GetProperty("date").GetString().Should().Be(@"\today");
        blocks[1].Content.RootElement.GetProperty("text").GetString().Should().Be("Hello.");

        var doc = await db.Documents.SingleAsync(d => d.Id == id);
        doc.Title.Should().Be("SE 767765 pandoc today date format");
        doc.LatexPackages.Should().Contain("isodate").And.Contain("USenglish,iso");
    }
}
