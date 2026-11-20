using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #237 (Overleaf round 1, 19 Nov 2026): the Overleaf Thesis template's front matter printed the arguments of
/// layout commands: "1.3" and "1.5" (\setstretch), "toc{ }" (\addtocontents{toc}{\vspace{1em}}) and "Signed: 25em0.5pt"
/// (\rule[1em]{25em}{0.5pt}). \pagestyle, also in the issue, prints nothing since #183. Now \setstretch goes with its argument
/// (the line spacing is the document's own setting), \addtocontents is kept as raw LaTeX like \addcontentsline, and a \rule
/// on its own line is kept as raw LaTeX so it still draws; one inside running text is dropped.
/// </summary>
public class Batch237ThesisCommandsTests
{
    private const string Thesis = @"\documentclass[11pt]{report}
\usepackage{setspace}
\begin{document}
\pagestyle{empty}
\clearpage
\setstretch{1.3}
\chapter*{Declaration of Authorship}
\addtocontents{toc}{\vspace{1em}}
I declare this thesis is mine.
Signed:\\
\rule[1em]{25em}{0.5pt}
\clearpage
\pagestyle{fancy}
\setstretch{1.5}
\addtocontents{toc}{\protect\vspace{2em}}
\chapter{Introduction}
Body text with an inline \textbf{mark \rule{1cm}{0.4pt} here}.
\end{document}";

    private static async Task<ImportDocument> Parse() => await new LatexParser().ParseTextAsync(Thesis);

    private static string AllText(ImportDocument doc) => string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));

    [Fact]
    public async Task Line_spacing_page_style_and_page_breaks_print_nothing()
    {
        var text = AllText(await Parse());
        text.Should().Contain("I declare this thesis is mine.");
        text.Should().NotContain("1.3").And.NotContain("1.5").And.NotContain("fancy").And.NotContain("empty").And.NotContain("clearpage");
    }

    [Fact]
    public async Task Addtocontents_is_kept_as_raw_LaTeX_and_not_printed()
    {
        var doc = await Parse();
        AllText(doc).Should().NotContain("toc");
        doc.Elements.OfType<ImportLatexPassthrough>().Select(p => p.LatexCode)
            .Should().Contain(@"\addtocontents{toc}{\vspace{1em}}").And.Contain(@"\addtocontents{toc}{\protect\vspace{2em}}");
    }

    [Fact]
    public async Task A_rule_on_its_own_line_is_kept_so_the_signature_line_still_draws()
    {
        var doc = await Parse();
        AllText(doc).Should().NotContain("25em").And.NotContain("0.5pt");
        doc.Elements.OfType<ImportLatexPassthrough>().Select(p => p.LatexCode).Should().Contain(@"\rule[1em]{25em}{0.5pt}");
        AllText(doc).Should().Contain("Signed:");
    }

    [Fact]
    public async Task A_rule_inside_running_text_prints_nothing_and_its_group_stays_whole()
    {
        var text = AllText(await Parse());
        text.Should().Contain("Body text with an inline");
        text.Should().NotContain("1cm").And.NotContain("0.4pt");
        text.Should().Contain("here", "the bold group around the rule is read whole");
    }
}
