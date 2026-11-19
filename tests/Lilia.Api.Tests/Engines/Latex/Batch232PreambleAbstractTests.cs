using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #232 (Overleaf round 1, 19 Nov 2026): the Overleaf article template (olplainarticle) sets its abstract and
/// \keywords{…} in the preamble, for the class to print at \maketitle, and the import dropped both: only the body was read.
/// Its \flushbottom also printed as a paragraph. The preamble's abstract is now the document's abstract block and its keywords
/// the title's.
/// </summary>
public class Batch232PreambleAbstractTests
{
    private const string Template = @"\documentclass[fleqn,10pt]{olplainarticle}
\title{Overleaf article template}
\author[1]{Author One}
\keywords{Keyword1, Keyword2, Keyword3}
% \begin{abstract}A commented-out abstract.\end{abstract}
\begin{abstract}
Please provide an abstract of no more than 300 words.
\end{abstract}
\begin{document}
\flushbottom
\maketitle
\thispagestyle{empty}
\section*{Introduction}
Body text.
\end{document}";

    private static async Task<ImportDocument> Parse(string tex) => await new LatexParser().ParseTextAsync(tex);

    [Fact]
    public async Task A_preamble_abstract_becomes_the_documents_abstract_first()
    {
        var elements = (await Parse(Template)).Elements.OrderBy(e => e.Order).ToList();
        elements[0].Should().BeOfType<ImportAbstract>().Which.Text.Should().Contain("Please provide an abstract of no more than 300 words.");
        elements.OfType<ImportAbstract>().Should().ContainSingle("the commented-out one is not read");
    }

    [Fact]
    public async Task Preamble_keywords_become_the_titles_keywords()
    {
        (await Parse(Template)).Metadata.Keywords.Should().Be("Keyword1, Keyword2, Keyword3");
    }

    [Fact]
    public async Task Flushbottom_prints_nothing()
    {
        var doc = await Parse(Template);
        doc.Elements.OfType<ImportParagraph>().Select(p => p.Text).Should().NotContain(t => t.Contains("flushbottom"));
    }

    [Fact]
    public async Task An_abstract_in_the_body_is_not_doubled_by_one_in_the_preamble()
    {
        var doc = await Parse(Template.Replace(@"\section*{Introduction}", @"\begin{abstract}The body's own.\end{abstract}" + "\n" + @"\section*{Introduction}"));
        doc.Elements.OfType<ImportAbstract>().Should().ContainSingle().Which.Text.Should().Contain("The body's own.");
    }
}
