using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// What five arXiv papers imported through the connector lost (launch-bot #137, 17 Nov 2026), each with the
/// smallest LaTeX the bot gave: a preamble \DeclareMathOperator's last brace (#139), a minipage figure's
/// \captionof (#140), thebibliography (#142), the authors and an empty \date{} (#143), a $\$$ formula (#144) and
/// REVTeX's appendix environment (#146).
/// </summary>
public class ArxivImportTests
{
    private static async Task<(List<(string Type, JsonElement Content)> Blocks, Lilia.Import.Models.ImportDocument Doc)> Import(string source)
    {
        var doc = await new LatexParser().ParseTextAsync(source, new Lilia.Import.Interfaces.LatexImportOptions
        {
            ExtractDocumentTitle = true,
            ConvertEquationEnvironments = true,
            ConvertDisplayMath = true,
        });
        var blocks = LatexImportJobExecutor.MapElements(doc.Elements)
            .Select(b => (b.type, JsonSerializer.SerializeToElement(b.content)))
            .ToList();
        return (blocks, doc);
    }

    private static Task<(List<(string Type, JsonElement Content)> Blocks, Lilia.Import.Models.ImportDocument Doc)> ImportBody(string body) =>
        Import("\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n");

    private static string Text(JsonElement content, string field = "text") =>
        content.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    // #139
    [Fact]
    public void A_math_operator_whose_text_holds_a_group_keeps_its_last_brace()
    {
        var ex = LatexPreambleExtractor.Extract(
            "\\documentclass{article}\n\\usepackage{amsmath}\n\\DeclareMathOperator{\\Tr}{\\mathrm{Tr}}\n\\begin{document}\n$\\Tr A$\n\\end{document}\n");

        ex.CustomPreamble.Should().Be(@"\DeclareMathOperator{\Tr}{\mathrm{Tr}}");
    }

    [Fact]
    public async Task A_math_operator_in_the_body_leaves_no_brace_behind()
    {
        var (blocks, _) = await ImportBody("\\DeclareMathOperator{\\Tr}{\\mathrm{Tr}}\nText.");

        blocks.Should().ContainSingle();
        Text(blocks[0].Content).Should().Be("Text.");
    }

    // #140
    [Fact]
    public async Task A_minipage_figure_with_captionof_is_a_figure_with_its_caption_and_label()
    {
        var (blocks, _) = await Import("""
            \documentclass{article}
            \usepackage{tikz,capt-of}
            \begin{document}
            \noindent\begin{minipage}[t]{0.4\textwidth}
            \begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}
            \captionof{figure}{A caption.}\label{fig:a}
            \prevdepth=\dp\strutbox
            \end{minipage}
            See Figure~\ref{fig:a}.
            \end{document}
            """);

        blocks.Should().NotContain(b => b.Type == "paragraph" && Text(b.Content) == "");
        var figure = blocks.Should().ContainSingle(b => b.Type == "figure").Subject.Content;
        Text(figure, "caption").Should().Be("A caption.");
        Text(figure, "label").Should().Be("fig:a");
        var text = string.Join("\n", blocks.Where(b => b.Type == "paragraph").Select(b => Text(b.Content)));
        text.Should().NotContain("figure{").And.NotContain("prevdepth").And.NotContain(@"\label{fig:a}");
        text.Should().Contain(@"\ref{fig:a}");
    }

    [Fact]
    public void A_minipage_without_captionof_only_loses_its_prevdepth()
        => LatexParser.MinipageBody("Text.\n\\prevdepth=\\dp\\strutbox\n").Trim().Should().Be("Text.");

    // #142
    [Fact]
    public async Task Thebibliography_is_a_bibliography_block_of_references_the_citations_find()
    {
        var (blocks, _) = await ImportBody("""
            See \cite{K1} and \cite{K2}.
            \begin{thebibliography}{9}
            \bibitem{K1} A. Author, A title, 2020.
            \bibitem[B]{K2} B. Author,
              \emph{Another title}, 2021.
            \end{thebibliography}
            """);

        blocks.Should().NotContain(b => b.Type == "paragraph" && Text(b.Content) == "");
        var bib = blocks.Should().ContainSingle(b => b.Type == "bibliography").Subject.Content;
        Text(bib).Should().Be("@cite[K1] A. Author, A title, 2020.\n@cite[K2] B. Author, \\emph{Another title}, 2021.");
        Lilia.Engines.BibliographyText.Parse(Text(bib)).Select(e => e.Key).Should().Equal("K1", "K2");
    }

    // #143
    [Fact]
    public async Task The_authors_are_the_title_blocks_and_an_empty_date_stays_empty()
    {
        var (_, doc) = await Import("""
            \documentclass{article}
            \title{Minimal}
            \author{Ann Author \and Bob Author\\ Example University}
            \date{}
            \begin{document}
            \maketitle
            Text.
            \end{document}
            """);

        var title = LatexImportJobExecutor.TitleBlockFor(doc);
        title.Should().NotBeNull();
        var content = JsonSerializer.SerializeToElement(title!.Value.content);
        Text(content, "author").Should().Be(@"Ann Author \and Bob Author\\Example University");
        Text(content, "date").Should().Be("");
        Text(content, "dateMode").Should().Be("none");
        Lilia.Engines.TitleAuthors.Parse(Text(content, "author")).Select(a => (a.Name, string.Join("|", a.Lines)))
            .Should().Equal(("Ann Author", ""), ("Bob Author", "Example University"));
    }

    [Fact]
    public async Task A_paper_without_date_keeps_the_default()
    {
        var (_, doc) = await Import("\\documentclass{article}\n\\title{T}\n\\author{A}\n\\begin{document}\nText.\n\\end{document}\n");

        var content = JsonSerializer.SerializeToElement(LatexImportJobExecutor.TitleBlockFor(doc)!.Value.content);
        content.TryGetProperty("dateMode", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Revtex_authors_keep_their_affiliation_email_thanks_and_altaffiliation()
    {
        var (blocks, doc) = await Import("""
            \documentclass[aps,pra]{revtex4-2}
            \begin{document}
            \title{Minimal}
            \author{Philipp Kleinpass}
            \email{philipp@example.org}
            \altaffiliation{E-mail: philipp.kleinpass@dlr.de}
            \affiliation{German Aerospace Center, Ulm, Germany}
            \author{Ann Author}
            \thanks{Funded by X.}
            \affiliation{Example University}
            \begin{abstract}
            An abstract.
            \end{abstract}
            \maketitle
            Text.
            \end{document}
            """);

        doc.Metadata.Author.Should().Be(
            @"Philipp Kleinpass\thanks{E-mail: philipp.kleinpass@dlr.de}\\German Aerospace Center, Ulm, Germany\\philipp@example.org"
            + @" \and Ann Author\thanks{Funded by X.}\\Example University");
        blocks.Should().NotContain(b => Text(b.Content).Contains("E-mail"));
        var authors = Lilia.Engines.TitleAuthors.Parse(doc.Metadata.Author);
        authors.Select(a => a.Name).Should().Equal("Philipp Kleinpass", "Ann Author");
        authors[0].Notes.Should().Equal("E-mail: philipp.kleinpass@dlr.de");
        authors[1].Notes.Should().Equal("Funded by X.");
    }

    // #144
    [Fact]
    public async Task An_escaped_dollar_in_maths_doesnt_open_a_display_formula()
    {
        var (blocks, _) = await ImportBody(@"It costs $\$$1000 per year, and $x=1$ here; it was $\$$5 before.");

        blocks.Should().ContainSingle();
        blocks[0].Type.Should().Be("paragraph");
        Text(blocks[0].Content).Should().Be(@"It costs $\$$1000 per year, and $x=1$ here; it was $\$$5 before.");
    }

    [Fact]
    public async Task A_display_formula_after_a_line_break_is_still_one()
    {
        var (blocks, _) = await ImportBody("A line\\\\\n$$x = 1$$\nafter.");

        blocks.Should().Contain(b => b.Type == "equation");
    }

    // #146
    [Fact]
    public async Task Revtex_appendix_environment_is_read_as_appendix_blocks()
    {
        var (blocks, _) = await ImportBody("""
            \section{Main}
            Body.
            \begin{appendix}
            \section{Six-State Parameter Estimation}
            Text.
            \begin{lemma} A lemma. \end{lemma}
            \begin{equation} x = 1 \end{equation}
            \end{appendix}
            """);

        blocks.Should().NotContain(b => b.Type == "embed");
        blocks.Where(b => b.Type == "heading").Select(b => Text(b.Content)).Should().Equal("Main", "Six-State Parameter Estimation");
        blocks.Should().Contain(b => b.Type == "theorem").And.Contain(b => b.Type == "equation");
        blocks.Should().NotContain(b => Text(b.Content).Contains("appendix"));
    }
}
