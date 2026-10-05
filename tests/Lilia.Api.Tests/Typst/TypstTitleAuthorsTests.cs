using System.Text;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Services;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Typst;

/// <summary>
/// The Title block's author string is one LaTeX string. The PDF through
/// pdflatex shows the authors side by side with their affiliations and
/// footnote marks; the Typst path used to flatten it to "Jane Doe, U. of South,
/// John Doe, U. of North" and drop the notes.
/// </summary>
public class TypstTitleAuthorsTests
{
    private static string TitleBlockFor(string author, string date = "May 2026")
    {
        var sb = new StringBuilder();
        TypstExportService.AppendTypstTitleBlock(sb, "A Paper", author, date);
        return sb.ToString();
    }

    private static string Build(string author)
    {
        var blocks = new List<Lilia.Core.Entities.Block>
        {
            Block("title", new { title = "A Paper", author, date = "May 2026" }, 0),
            Para("Body text.", 1),
        };
        return new TypstExportService().BuildTypstDocument(Doc("A Paper"), blocks);
    }

    [Fact]
    public void A_single_author_without_notes_renders_exactly_as_before()
    {
        TitleBlockFor("Ada Lovelace").Should().Be(
            "#align(center)[\n" +
            "  #text(size: 1.6em, weight: \"bold\")[A Paper]\n" +
            "  #v(0.6em)\n" +
            "  Ada Lovelace\n" +
            "  #v(0.4em)\n" +
            "  May 2026\n" +
            "]\n" +
            "#v(1.2em)\n\n");

        // With an affiliation line it is still the flat form.
        TitleBlockFor(@"Ada Lovelace \\ Engine Co.").Should().Contain("\n  Ada Lovelace, Engine Co.\n");
        Build("Ada Lovelace").Should().Contain("#set document(title: \"A Paper\", author: (\"Ada Lovelace\"))");
    }

    [Fact]
    public void Two_authors_render_as_a_grid_with_marks_and_footnotes()
    {
        var typst = TitleBlockFor(TitleAuthorsTests.Example);

        typst.Should().Contain("columns: (1fr, 1fr),");
        typst.Should().Contain("[Jane Doe#super[\\*] \\ U. of South],");
        typst.Should().Contain("[John Doe#super[†] \\ U. of North],");
        typst.Should().NotContain("Jane Doe, U. of South");
        typst.IndexOf("May 2026").Should().BeGreaterThan(typst.IndexOf("#grid("));
        var notes = typst[typst.IndexOf("#text(size: 0.85em)[")..];
        notes.Should().Contain("#super[\\*] I am thankful for the funding provided by Institution. \\");
        notes.Should().Contain("#super[†] E-mail: jdoe\\@unorth.edu");
    }

    [Fact]
    public void The_document_author_stays_a_plain_array_of_names()
    {
        Build(TitleAuthorsTests.Example).Should().Contain(
            "#set document(title: \"A Paper\", author: (\"Jane Doe\", \"John Doe\"))");
    }

    [Fact]
    public void A_single_author_with_a_note_gets_the_grid_and_the_footnote()
    {
        var typst = TitleBlockFor(@"Ada\thanks{Funded.}");
        typst.Should().Contain("columns: (1fr,),").And.Contain("[Ada#super[\\*]],");
        typst.Should().Contain("#super[\\*] Funded.");
        Build(@"Ada\thanks{Funded.}").Should().Contain("author: (\"Ada\",))");
    }

    [Fact]
    public void Four_authors_wrap_after_three_columns()
    {
        var typst = TitleBlockFor(@"A \and B \and C \and D");
        typst.Should().Contain("columns: (1fr, 1fr, 1fr),");
        typst.Split("\n    [").Length.Should().Be(5); // four cells
    }

    [Fact]
    public void Typst_syntax_in_names_and_notes_is_escaped()
    {
        var typst = TitleBlockFor(@"A_B #x \and C\thanks{See $5 [1] <a>}");
        typst.Should().Contain("[A\\_B \\#x],");
        typst.Should().Contain("See \\$5 \\[1\\] \\<a\\>");
    }

    [Theory]
    [InlineData(TitleAuthorsTests.Example)]
    [InlineData(@"A \and B \and C \and D \and E")]
    [InlineData(@"Ada\thanks{Funded by {\bf X}.}")]
    [InlineData(@"O_o #1 \and P@q\thanks{*star* $ [x] <y> `z`}\\Lab_1 \& Co")]
    public void Multi_author_documents_compile(string author)
    {
        var result = Compile(Build(author));
        result.Ok.Should().BeTrue($"typst said: {result.FirstProblem}");
    }

    [Fact]
    public void The_compiled_pdf_shows_both_authors_the_marks_and_the_footnotes()
    {
        var (text, error) = CompileToText(Build(TitleAuthorsTests.Example));

        text.Should().NotBeNull(error);
        text!.Should().Contain("Jane Doe").And.Contain("John Doe");
        text.Should().Contain("U. of South").And.Contain("U. of North");
        text.Should().Contain("I am thankful for the funding provided by Institution.");
        text.Should().Contain("E-mail: jdoe@unorth.edu");
        var line = text.Split('\n').First(l => l.Contains("Jane Doe"));
        line.Should().Contain("John Doe", "the authors sit side by side");
    }

    [Fact]
    public void The_live_preview_path_renders_the_same_grid()
    {
        var sb = new StringBuilder();
        TypstRenderService.AppendMetadata(sb, "A Paper", TitleAuthorsTests.Example);
        TypstRenderService.AppendTitleBlock(sb, "A Paper", TitleAuthorsTests.Example, "May 2026");
        var typst = sb.ToString();

        typst.Should().Contain("author: (\"Jane Doe\", \"John Doe\"),");
        typst.Should().Contain("[Jane Doe#super[\\*] \\ U. of South],");
        typst.Should().Contain("#super[\u2020] E-mail: jdoe\\@unorth.edu");
        Compile(typst).Ok.Should().BeTrue();
    }

    [Fact]
    public void The_live_preview_path_keeps_a_single_author_flat()
    {
        var sb = new StringBuilder();
        TypstRenderService.AppendMetadata(sb, "A Paper", "Ada Lovelace");
        TypstRenderService.AppendTitleBlock(sb, "A Paper", "Ada Lovelace", "May 2026");
        sb.ToString().Should().Be(
            "#set document(\n  title: \"A Paper\",\n  author: (\"Ada Lovelace\"),\n)\n\n" +
            "#align(center)[\n  #text(size: 1.6em, weight: \"bold\")[A Paper]\n  #v(0.6em)\n  Ada Lovelace\n" +
            "  #v(0.4em)\n  May 2026\n]\n#v(1.2em)\n\n");
    }

    // ── review of 5 Oct: "//" is a Typst comment ─────────────────────────

    [Fact]
    public void A_double_slash_in_an_author_line_does_not_break_the_compile()
    {
        var (text, error) = CompileToText(Build(@"Jane \\ Dept. A // B \and John"));
        text.Should().NotBeNull($"the document must compile ({error})");
        text!.Should().Contain("Dept. A // B");
    }

    [Fact]
    public void A_url_still_reads_as_a_url()
    {
        var (text, error) = CompileToText(Build(@"Jane \\ https://example.org/lab \and John"));
        text.Should().NotBeNull($"the document must compile ({error})");
        text!.Should().Contain("https://example.org/lab");
    }

    [Fact]
    public void Accented_and_bold_names_print_as_written()
    {
        var (text, error) = CompileToText(Build("Kurt G\\\"odel \\and \\textbf{Paul Erd\\H{o}s}"));
        text.Should().NotBeNull($"the document must compile ({error})");
        text!.Should().Contain("Kurt Gödel").And.Contain("Paul Erdős");
    }
}
