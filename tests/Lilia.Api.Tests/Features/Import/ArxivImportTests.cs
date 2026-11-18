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

    // launch-bot #143, retest (18 Nov 2026): amsart's \thanks after the authors (affiliations, e-mails, grants) and on the
    // title were lost; it is the last author's note, as REVTeX's is, and a title's goes to the first author (amsart prints
    // every \thanks as a first-page footnote).
    [Fact]
    public async Task Amsart_thanks_after_the_authors_and_on_the_title_are_kept()
    {
        var (_, doc) = await Import("""
            \documentclass{amsart}
            \title{Minimal\thanks{Title note.}}
            \author{Ann Author}
            \address{Example University}
            \author{Bob Author}
            \thanks{Funded by X.}
            \begin{document}
            \maketitle
            Text.
            \end{document}
            """);

        doc.Metadata.Author.Should().Be(@"Ann Author\thanks{Title note.}\\Example University \and Bob Author\thanks{Funded by X.}");
        doc.Title.Should().Be("Minimal");
        var authors = Lilia.Engines.TitleAuthors.Parse(doc.Metadata.Author);
        authors[0].Notes.Should().Equal("Title note.");
        authors[1].Notes.Should().Equal("Funded by X.");
    }

    // launch-bot #143, retest: REVTeX's \orcidlink printed its number as text, and \vspace{5pt} between two affiliations
    // left "5pt" in the address.
    [Fact]
    public async Task Revtex_orcid_is_kept_as_orcidlink_and_affiliation_spacing_goes()
    {
        var (_, doc) = await Import("""
            \documentclass[aps,pra]{revtex4-2}
            \usepackage{orcidlink}
            \begin{document}
            \title{Minimal}
            \author{Philipp Kleinpa\ss\,\orcidlink{0009-0009-9005-702X}}
            \affiliation{German Aerospace Center, Weßling, Germany\\ \vspace{5pt} Institut f\"ur Theoretische Physik, Ulm, Germany}
            \author[0000-0002-1825-0097]{Ann Author}
            \affiliation{Example University, Ulm, Germany}
            \author{Bob Author}
            \orcid{0000-0001-2345-6789}
            \maketitle
            Text.
            \end{document}
            """);

        var authors = Lilia.Engines.TitleAuthors.Parse(doc.Metadata.Author);
        authors.Select(a => a.Name).Should().Equal("Philipp Kleinpaß", "Ann Author", "Bob Author");
        authors.Select(a => a.Orcid).Should().Equal("0009-0009-9005-702X", "0000-0002-1825-0097", "0000-0001-2345-6789");
        authors[0].Lines.Should().HaveCount(2).And.NotContain(l => l.Contains("5pt") || l.Contains("vspace"));
        doc.Metadata.Author.Should().NotContain("0009-0009-9005-702X}}").And.Contain(@"\orcidlink{0009-0009-9005-702X}");
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

    // launch-bot #146 (18 Nov 2026): the appendix's sections numbered on (VII, VIII; 4, 5) instead of A, B: \appendix was
    // dropped. It is now the Appendix block (back matter, which prints \appendix) before the first appendix section.
    [Theory]
    [InlineData("\\begin{appendix}\n\\section{First}\nText.\n\\section{Second}\nMore.\n\\end{appendix}")]
    [InlineData("\\appendix\n\\section{First}\nText.\n\\section{Second}\nMore.")]
    public async Task The_appendix_switch_is_an_appendix_block_before_its_sections(string appendix)
    {
        var (blocks, _) = await ImportBody("\\section{Main}\nBody.\n" + appendix);

        blocks.Select(b => b.Type).Should().Equal("heading", "paragraph", "backMatter", "heading", "paragraph", "heading", "paragraph");
        var start = blocks[2].Content;
        Text(start, "subType").Should().Be("appendix");
        Text(start).Should().BeEmpty();
        Text(start, "title").Should().BeEmpty();
    }

    [Fact]
    public async Task An_appendixname_is_not_the_appendix_switch()
    {
        var (blocks, _) = await ImportBody("See \\appendixname{} below.");

        blocks.Should().NotContain(b => b.Type == "backMatter");
    }

    // launch-bot #162 (18 Nov 2026): a \label inside an equation (or a minipage figure) in a proof was given to the
    // proof, leaving a whitespace-only line in the maths ("Missing $ inserted") and \eqref pointing at the proof.
    [Fact]
    public async Task A_label_inside_an_equation_in_a_proof_stays_with_the_equation()
    {
        var (blocks, _) = await ImportBody("""
            \begin{proof}
            We define
            \begin{equation}
            	\label{eq:w}
            	w = 1
            \end{equation}
            and use \eqref{eq:w}.
            \end{proof}
            """);

        var proof = blocks.Should().ContainSingle(b => b.Type == "theorem").Subject.Content;
        Text(proof, "label").Should().BeEmpty();
        Text(proof).Should().Contain("\\begin{equation}\n\t\\label{eq:w}\n\tw = 1\n\\end{equation}");
    }

    [Fact]
    public async Task A_label_in_a_minipage_in_a_proof_is_not_the_proofs()
    {
        var (blocks, _) = await ImportBody("""
            \begin{proof}
            See below.
            \begin{minipage}{0.4\textwidth}
            \captionof{figure}{A caption.}\label{fig:p}
            \end{minipage}
            \end{proof}
            """);

        Text(blocks.Should().ContainSingle(b => b.Type == "theorem").Subject.Content, "label").Should().BeEmpty();
    }

    // launch-bot #140, the part left (18 Nov 2026): a minipage figure inside a proof stayed in the proof's text, so its
    // caption sat in a narrow column beside the proof and "Figure~\ref{fig:p}" printed the wrong number. It is now its own
    // figure block, with its caption and label, after the proof.
    [Fact]
    public async Task A_minipage_figure_in_a_proof_is_a_figure_after_the_proof()
    {
        var (blocks, _) = await ImportBody("""
            \begin{proof}
            See below.
            \noindent\begin{minipage}[t]{0.4\textwidth}
            \begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}
            \captionof{figure}{In a proof.}\label{fig:p}
            \prevdepth=\dp\strutbox
            \end{minipage}
            See Figure~\ref{fig:p}.
            \end{proof}
            """);

        blocks.Select(b => b.Type).Should().Equal("theorem", "figure");
        var proof = blocks[0].Content;
        Text(proof, "label").Should().BeEmpty();
        Text(proof).Should().NotContain("minipage").And.NotContain("captionof").And.NotContain("tikzpicture").And.Contain(@"\ref{fig:p}");
        Text(blocks[1].Content, "caption").Should().Be("In a proof.");
        Text(blocks[1].Content, "label").Should().Be("fig:p");
    }

    [Fact]
    public async Task A_minipage_without_a_float_stays_in_the_theorem()
    {
        var (blocks, _) = await ImportBody("\\begin{theorem}\nA.\n\\begin{minipage}{0.4\\textwidth}Side text.\\end{minipage}\n\\end{theorem}");

        blocks.Should().ContainSingle(b => b.Type == "theorem").Which.Content.GetProperty("text").GetString().Should().Contain("Side text.");
        blocks.Should().NotContain(b => b.Type == "figure");
    }

    [Fact]
    public async Task A_theorems_own_label_is_still_its_label_and_leaves_no_blank_line()
    {
        var (blocks, _) = await ImportBody("\\begin{lemma}\nFirst line.\n\\label{lem:a}\nSecond line, $x$.\n\\end{lemma}");

        var lemma = blocks.Should().ContainSingle(b => b.Type == "theorem").Subject.Content;
        Text(lemma, "label").Should().Be("lem:a");
        Text(lemma).Should().Be("First line.\nSecond line, $x$.");
    }

    // launch-bot #138, the gap (18 Nov 2026): an algorithm2e body was read as algorithmic, so \KwTo, \tcp, \nl and \; stayed
    // in Lilia's algorithm and the PDF stopped ("Undefined control sequence \KwTo"). It is read into the block's lines.
    [Fact]
    public async Task An_algorithm2e_body_becomes_the_algorithm_blocks_lines()
    {
        var doc = await new LatexParser().ParseTextAsync("""
            \documentclass{article}
            \usepackage[ruled,vlined]{algorithm2e}
            \begin{document}
            \begin{algorithm}[!t]
            \caption{Main}\label{alg:m}
            \DontPrintSemicolon
            \SetKwFunction{FSum}{Sum}
            \KwIn{an instance $I$}
            \KwOut{a path}
            \nl $x \gets 0$\;
            \For{$i=1$ \KwTo $n$}{
              \eIf{$a_i > 0$}{
                $x \gets x + a_i$ \tcp*{add}
              }{
                \tcp{skip it}
                $y \gets \FSum{y, 1}$\;
              }
            }
            \While{$x > 0$}{ $x \gets x - 1$\; }
            \lIf{$x = 0$}{\KwRet{$x$}}
            \KwRet $y$\;
            \end{algorithm}
            \end{document}
            """);

        var algo = doc.Elements.OfType<Lilia.Import.Models.ImportAlgorithm>().Single();
        algo.Caption.Should().Be("Main");
        algo.Label.Should().Be("alg:m");
        algo.LineNumbers.Should().BeTrue();
        algo.Lines.Select(l => (l.Kind, l.Text, l.Comment)).Should().Equal(
            ("require", "an instance $I$", ""),
            ("ensure", "a path", ""),
            ("statement", @"$x \gets 0$", ""),
            ("for", "$i=1$ to $n$", ""),
            ("if", "$a_i > 0$", ""),
            ("statement", @"$x \gets x + a_i$", "add"),
            ("else", "", ""),
            ("comment", "skip it", ""),
            ("statement", @"$y \gets \textsc{Sum}(y, 1)$", ""),
            ("endif", "", ""),
            ("endfor", "", ""),
            ("while", "$x > 0$", ""),
            ("statement", @"$x \gets x - 1$", ""),
            ("endwhile", "", ""),
            ("if", "$x = 0$", ""),
            ("return", "$x$", ""),
            ("endif", "", ""),
            ("return", "$y$", ""));
    }

    [Fact]
    public void An_algorithmic_body_is_not_read_as_algorithm2e()
        => Algorithm2e.Is("\\begin{algorithmic}\\STATE $x \\gets 0$\\end{algorithmic}").Should().BeFalse();

    // launch-bot #164 (18 Nov 2026): an algorithm's caption with nested braces was cut at the first }, and the caption and
    // label printed again as a step.
    [Fact]
    public async Task An_algorithm_caption_keeps_its_nested_braces_and_is_not_a_step()
    {
        var doc = await new LatexParser().ParseTextAsync("""
            \documentclass{article}
            \usepackage[ruled,vlined]{algorithm2e}
            \begin{document}
            \begin{algorithm}[!t]
            \caption{\textup{\textsc{PathTSP}}$(V,c,s,t)$}
            \label{alg:main}
            \KwIn{an instance}
            \KwOut{a path}
            \end{algorithm}
            \end{document}
            """);
        var algo = doc.Elements.OfType<Lilia.Import.Models.ImportAlgorithm>().Single();
        algo.Caption.Should().Be(@"\textup{\textsc{PathTSP}}$(V,c,s,t)$");
        algo.Label.Should().Be("alg:main");
        algo.Code.Should().NotContain(@"\caption").And.NotContain(@"\label");
    }

    // launch-bot #167 (18 Nov 2026): a figure captioned with \captionof kept it in the picture's source, so the block had no
    // caption, the export added an empty one that took the label, and every later figure was numbered one too high.
    [Theory]
    [InlineData(@"\begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}")]
    [InlineData(@"\includegraphics[width=0.5\textwidth]{lune.png}")]
    public async Task A_figure_captioned_with_captionof_has_that_caption_and_label(string picture)
    {
        var (blocks, _) = await ImportBody(
            "\\begin{figure}[htbp]\n\\centering\n" + picture + "\n\\captionof{figure}{A lune.}\n\\label{fig:lune}\n\\end{figure}\nSee Figure~\\ref{fig:lune}.");

        var figure = blocks.Should().ContainSingle(b => b.Type == "figure").Subject.Content;
        Text(figure, "caption").Should().Be("A lune.");
        Text(figure, "label").Should().Be("fig:lune");
        Text(figure, "source").Should().NotContain("captionof");
    }

    // launch-bot #168 (18 Nov 2026): \vspace{0pt} at the top of a text minipage printed as the text "0pt".
    [Fact]
    public async Task A_vspace_doesnt_leave_its_length_as_text()
    {
        var (blocks, _) = await ImportBody("""
            \noindent
            \begin{minipage}[t]{0.59\textwidth}
            \vspace{0pt}
            Each set is an increasing sequence. See Figure~\ref{fig:side}.
            \end{minipage}\hfill
            \begin{minipage}[t]{0.37\textwidth}
            \vspace{0pt}\centering
            \begin{tikzpicture}\draw (0,0)--(1,0);\end{tikzpicture}
            \captionof{figure}{Beside the text.}
            \label{fig:side}
            \end{minipage}\par
            After a gap\vspace{3pt} it goes on\hspace{1cm} here.
            """);

        var text = blocks.Where(b => b.Type == "paragraph").Select(b => Text(b.Content)).ToList();
        text[0].Should().StartWith("Each set is an increasing sequence.");
        text.Should().NotContain(t => t.Contains("0pt") || t.Contains("1cm"));
        text[^1].Should().Be("After a gap it goes on here.");
        Text(blocks.Single(b => b.Type == "figure").Content, "caption").Should().Be("Beside the text.");
    }

    // launch-bot #169 (18 Nov 2026): \r{a} was dropped in an amsart \thanks and became "a" in the body, and
    // {\tt \char`\{ann, bob\char`\}@example.org} lost "{ann" and both braces (the body version did not compile).
    [Fact]
    public async Task Accents_and_char_survive_in_the_thanks_and_the_body()
    {
        var (blocks, doc) = await Import("""
            \documentclass{amsart}
            \title{Accents in thanks}
            \author{Ann Author}
            \thanks{AA: Ume\r{a} University, Sweden. Email: {\tt \char`\{ann, bob\char`\}@example.org}.}
            \date{}
            \begin{document}
            \maketitle
            \section{Intro}
            Body: Ume\r{a} University and {\tt \char`\{ann, bob\char`\}@example.org}.
            \end{document}
            """);

        var authors = Lilia.Engines.TitleAuthors.Parse(doc.Metadata.Author);
        authors.Single().Notes.Should().Equal("AA: Umeå University, Sweden. Email: {ann, bob}@example.org.");
        Text(blocks.Single(b => b.Type == "paragraph").Content)
            .Should().Be(@"Body: Umeå University and \{ann, bob\}@example.org.");
    }

    [Theory]
    [InlineData(@"\r{a} \r a \u{a} \v{s} \v s \H{o} \c{c} \k{a}", "å å ă š š ő ç ą")]
    [InlineData(@"\`a \'e \'{\i} \^o \""u \~n \=a \.z", "à é í ô ü ñ ā ż")]
    [InlineData(@"\d{o} \b{o} \t{oo}", @"\d{o} \b{o} \t{oo}")]
    [InlineData(@"name\char64 domain, \char""41, \char'101, \char`\}", @"name@domain, A, A, \}")]
    public async Task Text_accents_and_char_are_their_characters_in_the_body(string written, string expected)
    {
        var (blocks, _) = await ImportBody("Say " + written + ".");

        Text(blocks.Single().Content).Should().Be("Say " + expected + ".");
    }

    // launch-bot #143 leftover (18 Nov 2026): a \\ inside one REVTeX \affiliation split it into two, and REVTeX printed the
    // street address as a second affiliation ("…Weßling, Germany and").
    [Fact]
    public async Task A_line_break_inside_a_revtex_affiliation_keeps_it_one_affiliation()
    {
        var (_, doc) = await Import("""
            \documentclass[aps,pra]{revtex4-2}
            \begin{document}
            \title{Minimal}
            \author{Philipp Kleinpass}
            \affiliation{%
             Deutsches Zentrum f\"ur Luft- und Raumfahrt (DLR) e.V., Institute of Communications and Navigation,\\
             M\"unchener Stra\ss e 20, 82234 We\ss ling, Germany}
            \affiliation{Institut f\"ur Theoretische Physik, Universit\"at Ulm, 89069 Ulm, Germany}
            \maketitle
            Text.
            \end{document}
            """);

        var lines = Lilia.Engines.TitleAuthors.Parse(doc.Metadata.Author).Single().Lines;
        lines.Should().Equal(
            "Deutsches Zentrum für Luft- und Raumfahrt (DLR) e.V., Institute of Communications and Navigation, Münchener Straße 20, 82234 Weßling, Germany",
            "Institut für Theoretische Physik, Universität Ulm, 89069 Ulm, Germany");
        var (_, body) = Lilia.Engines.Publishers.PublisherClasses.FrontMatter(
            "revtex4-2", "Minimal", doc.Metadata.Author, null, [], s => s, "Minimal");
        System.Text.RegularExpressions.Regex.Matches(body, @"\\affiliation\{").Should().HaveCount(2);
        body.Should().Contain(@"\affiliation{Deutsches Zentrum für Luft- und Raumfahrt (DLR) e.V., Institute of Communications and Navigation, Münchener Straße 20, 82234 Weßling, Germany}");
    }
}
