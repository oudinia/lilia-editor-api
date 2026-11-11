using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// Paste → LaTeX phase 1 (lilia-launch-bot #60, 10 Nov 2026): pasted Markdown or plain text
/// becomes real blocks, and an AUTO import reads the format from the text. On the test copy,
/// Markdown pasted as Lilia text lost its list (one paragraph) and its $$…$$ (text).
/// </summary>
public class PasteMarkdownImportTests : IntegrationTestBase
{
    public PasteMarkdownImportTests(TestDatabaseFixture fixture) : base(fixture) { }

    private const string ChatAnswer = """
        ## Results

        The loss **drops** fast, see [the paper](https://example.org/a_b).
        It is _stable_ and uses `adam`.

        - First point
        - Second point
          continued here
            - A sub-point
        - Third point

        $$
        E = mc^2
        $$

        1. One
        2. Two

        | Model | Score |
        |---|---|
        | A | 0.9 |

        > A quote
        > on two lines

        ```python
        print("hi")
        ```
        """;

    [Fact]
    public void Markdown_becomes_headings_lists_equations_tables_quotes_and_code()
    {
        var els = MarkdownTextParser.Parse(ChatAnswer);
        els.OfType<ImportHeading>().Single().Should().Match<ImportHeading>(h => h.Level == 2 && h.Text == "Results");
        els.OfType<ImportParagraph>().Single().Text.Should().Be(
            @"The loss **drops** fast, see \href{https://example.org/a_b}{the paper}. It is *stable* and uses `adam`.");

        var bullets = els.OfType<ImportListItem>().Where(i => !i.IsNumbered).ToList();
        bullets.Select(i => i.Text).Should().Equal("First point", "Second point continued here", "Third point");
        bullets.Select(i => i.ListGroup).Distinct().Should().ContainSingle("one list, not one per item");
        bullets[1].Children.Single().Text.Should().Be("A sub-point");
        els.OfType<ImportListItem>().Where(i => i.IsNumbered).Select(i => i.Text).Should().Equal("One", "Two");

        els.OfType<ImportEquation>().Single().LatexContent.Should().Be("E = mc^2");
        var table = els.OfType<ImportTable>().Single();
        table.HasHeaderRow.Should().BeTrue();
        table.Rows.Select(r => r.Select(c => c.Text).ToArray()).Should().BeEquivalentTo(new[] { new[] { "Model", "Score" }, new[] { "A", "0.9" } });
        els.OfType<ImportBlockquote>().Single().Text.Should().Be("A quote on two lines");
        els.OfType<ImportCodeBlock>().Single().Should().Match<ImportCodeBlock>(c => c.Language == "python" && c.Text == "print(\"hi\")");
    }

    [Theory]
    [InlineData(@"Area is \(\pi r^2\) here.", @"Area is $\pi r^2$ here.")]
    [InlineData(@"Keep $a_b$ and `x_y` as they are, but __this__ is bold.", @"Keep $a_b$ and `x_y` as they are, but **this** is bold.")]
    [InlineData(@"snake_case_name stays", @"snake_case_name stays")]
    [InlineData(@"An escaped \*star\* and <https://example.org>.", @"An escaped *star* and \url{https://example.org}.")]
    [InlineData(@"As in Section~\ref{sec:a}.", @"As in Section~\ref{sec:a}.")]
    public void Inline_marks_take_Lilias_form(string markdown, string lilia) =>
        MarkdownTextParser.Inline(markdown).Should().Be(lilia);

    [Fact]
    public void Display_maths_in_brackets_and_a_setext_heading()
    {
        var els = MarkdownTextParser.Parse("Title\n=====\n\n\\[\n\\int_0^1 x\\,dx\n\\]\n\nPlain text, no marks.\nSecond line.");
        els[0].Should().BeOfType<ImportHeading>().Which.Level.Should().Be(1);
        els[1].Should().BeOfType<ImportEquation>().Which.LatexContent.Should().Be(@"\int_0^1 x\,dx");
        els[2].Should().BeOfType<ImportParagraph>().Which.Text.Should().Be("Plain text, no marks. Second line.");
    }

    [Theory]
    [InlineData("\\documentclass{article}\n\\begin{document}\nHi\n\\end{document}", "LATEX")]
    [InlineData("\\section{Intro}\\label{sec:i}\nText with $x$.\n\\begin{itemize}\\item a\\end{itemize}", "LATEX")]
    [InlineData("@heading(level=1) Intro\n@paragraph\nText.", "LML")]
    [InlineData("{\"document\":{\"blocks\":[]}}", "LML")]
    [InlineData("## Intro\n\n- a\n- b\n\nThe value $\\frac{1}{2}$ is **small**.", "MARKDOWN")]
    [InlineData("Just a plain paragraph of text.", "MARKDOWN")]
    public void The_format_is_read_from_the_text(string text, string format) =>
        PasteFormat.Detect(text).Should().Be(format);

    [Fact]
    public async Task An_AUTO_import_of_Markdown_makes_the_blocks()
    {
        const string user = "paste-md-auto";
        await SeedUserAsync(user);
        var client = CreateClientAs(user);
        var response = await client.PostAsJsonAsync("/api/lilia/jobs/import",
            new { content = ChatAnswer, format = "AUTO", filename = "pasted.md", skipReview = true });
        response.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("document").GetProperty("id").GetGuid();

        await using var db = CreateDbContext();
        var blocks = await db.Blocks.Where(b => b.DocumentId == id).OrderBy(b => b.SortOrder).Select(b => b.Type).ToListAsync();
        blocks.Should().Equal("heading", "paragraph", "list", "equation", "list", "table", "blockquote", "code");
        var list = (await db.Blocks.Where(b => b.DocumentId == id && b.Type == "list").OrderBy(b => b.SortOrder).FirstAsync()).Content.RootElement;
        list.GetProperty("items").GetArrayLength().Should().Be(3);
        (await db.Jobs.SingleAsync(j => j.DocumentId == id)).SourceFormat.Should().Be("markdown");
    }
}
