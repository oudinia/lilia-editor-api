using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's Overleaf round 1 (18 Nov 2026): the compile blockers, #222, #223, #224, #225, #227, #230, #235.
/// </summary>
public class Batch14OverleafBlockerTests
{
    // #223: \eqref{eq} → \eq:eqref{eq:eq}.
    [Fact]
    public void A_bare_reference_renames_only_its_argument()
    {
        var latex = CrossRefSyntax.ResolveBareReferences(@"\begin{equation}a+b=\gamma\label{eq:eq}\end{equation} Use \eqref{eq}, not Eq.~\eqref{eq}.");
        latex.Should().Contain(@"\eqref{eq:eq}, not Eq.~\eqref{eq:eq}").And.NotContain(@"\eq:");
    }

    // #224: a rights command the text only names is not metadata.
    [Fact]
    public void A_rights_command_named_in_text_is_not_lifted()
    {
        const string src = """
            \documentclass[sigconf]{acmart}
            \acmConference[X]{Conf}{2026}{City}
            \begin{document}
            \title{T}
            \author{A}
            \maketitle
            Use with the \texttt{\string\acmSubmissionID} command to print the number.
            \end{document}
            """;
        var rights = PublisherFrontMatterReader.Read(src, "acmart")!.AcmRights!;
        rights.Should().Contain(@"\acmConference").And.NotContain("acmSubmissionID");
    }

    // #225: a .bib stored on one line, starting with a % comment.
    [Fact]
    public void A_flattened_bib_with_a_leading_comment_keeps_its_entries()
    {
        var entries = BibTexParser.Parse("% Journals % First the Full Name @book{Lamport:LaTeX, author={L. Lamport}, title={LaTeX}, year={1994}} @article{b, title={B}, year={2000}}");
        entries.Select(e => e.CiteKey).Should().Equal("Lamport:LaTeX", "b");
    }

    // #222: \verb's content is text.
    [Fact]
    public async Task Verb_content_is_text_and_does_not_end_the_document()
    {
        LatexParser.VerbAsTexttt(@"placed before the \verb|\end{document}| command")
            .Should().Be(@"placed before the \texttt{\textbackslash{}end\{document\}} command");
        var doc = await new LatexParser().ParseTextAsync("""
            \documentclass{article}
            \begin{document}
            \section{One}
            Put it just before the \verb|\end{document}| command.
            \section{Two}
            In \verb+\section{#1}+ the \verb+#1+ is the title.
            \begin{verbatim}
            \verb|kept| as typed
            \end{verbatim}
            \end{document}
            """);
        doc.Elements.OfType<ImportHeading>().Select(h => h.Text).Should().Equal("One", "Two");
        doc.Elements.OfType<ImportParagraph>().Should().Contain(p => p.Text.Contains("command"));
    }

    // #227: description lists keep their terms.
    [Fact]
    public async Task A_description_list_keeps_its_terms()
    {
        var doc = await new LatexParser().ParseTextAsync("""
            \documentclass{article}
            \begin{document}
            \begin{description}\item[Word] Definition \item[Concept] Explanation\end{description}
            \end{document}
            """);
        var (type, content) = LatexImportJobExecutor.MapElements(doc.Elements).Single(b => b.type == "list");
        var json = JsonDocument.Parse(JsonSerializer.Serialize(content)).RootElement;
        json.GetProperty("kind").GetString().Should().Be("description");
        json.GetProperty("items")[0].GetProperty("text").GetString().Should().Be("Word");
        json.GetProperty("items")[0].GetProperty("description").GetString().Should().Be("Definition");
    }

    // #230: the source's \newtheorem and \theoremstyle reach the preamble; #235: an environment the class defines isn't
    // defined twice.
    [Fact]
    public void Theorem_kinds_and_class_environments_in_the_preamble()
    {
        var extracted = LatexPreambleExtractor.Extract("""
            \documentclass{amsart}
            \newtheorem{theorem}{Theorem}
            \theoremstyle{remark}
            \newtheorem{df}{Definition}
            \newenvironment{IEEEkeywords}{\begin{keywords}}{\end{keywords}}
            \begin{document}x\end{document}
            """);
        extracted.CustomPreamble.Should().Contain(@"\newtheorem{df}{Definition}").And.Contain(@"\theoremstyle{remark}");
        var emitted = LaTeXPreamble.CustomPreambleForEmit(extracted.CustomPreamble);
        emitted.Should().Contain(@"\newtheorem{df}{Definition}").And.NotContain(@"\newtheorem{theorem}{Theorem}");
        emitted.TrimEnd().Should().EndWith(@"\theoremstyle{plain}");
        emitted.Should().Contain(@"\ifcsname IEEEkeywords\endcsname\else \newenvironment{IEEEkeywords}{\begin{keywords}}{\end{keywords}}\fi");
    }
}
