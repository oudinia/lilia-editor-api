using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #238 (Overleaf round 1, 19 Nov 2026): the APS REVTeX template's \preprint{APS/123-QED}, its two
/// \collaboration lines and \homepage printed as body paragraphs after the title ("APS/123-QED", "MUSO Collaboration", the URL
/// without its ~), and \altaffiliation[Also at ]{…} became a \thanks without "Also at". The front matter reader read
/// \altaffiliation as a note and none of the others, so they fell through to the body. They now stay REVTeX commands: \preprint
/// as the title block's front lines, the others with the author they follow. (The title's \thanks, also in the issue, has been
/// kept since launch-bot #249: the first author's note.)
/// </summary>
public class Batch238RevtexFrontMatterTests
{
    private const string Aps = @"\documentclass[aps,prl,reprint]{revtex4-2}
\begin{document}
\preprint{APS/123-QED}
\title{Manuscript Title:\\with Forced Linebreak}
\thanks{A footnote to the article title}
\author{Ann Author}
\altaffiliation[Also at ]{Physics Department, XYZ University.}
\author{Second Author}
\email{Second.Author@institution.edu}
\affiliation{Authors' institution and/or address}
\collaboration{MUSO Collaboration}
\author{Charlie Author}
\homepage{http://www.Second.institution.edu/~Charlie.Author}
\affiliation{Second institution and/or address}
\collaboration{CLEO Collaboration}
\begin{abstract}An abstract.\end{abstract}
\maketitle
Body text.
\end{document}";

    private static PublisherFrontMatterReader.Result Front() => PublisherFrontMatterReader.Read(Aps, "revtex4-2")!;

    [Fact]
    public void The_preprint_number_is_kept_as_a_front_line()
    {
        Front().FrontExtra.Should().Be(@"\preprint{APS/123-QED}");
    }

    [Fact]
    public void Each_REVTeX_author_command_stays_with_the_author_it_follows()
    {
        var authors = TitleAuthors.Parse(Front().AuthorText);
        authors.Select(a => a.Name).Should().Equal("Ann Author", "Second Author", "Charlie Author");
        authors[0].ClassCommands.Should().Equal(@"\altaffiliation[Also at ]{Physics Department, XYZ University.}");
        authors[0].Notes.Should().Equal("A footnote to the article title");
        authors[1].ClassCommands.Should().Equal(@"\collaboration{MUSO Collaboration}");
        authors[2].ClassCommands.Should().Equal(@"\homepage{http://www.Second.institution.edu/~Charlie.Author}", @"\collaboration{CLEO Collaboration}");
    }

    [Fact]
    public async Task Nothing_from_the_front_matter_prints_in_the_body()
    {
        var doc = await new LatexParser().ParseTextAsync(Aps);
        var body = string.Join(" | ", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        body.Should().Contain("Body text.");
        body.Should().NotContain("APS/123-QED").And.NotContain("Collaboration").And.NotContain("institution.edu/");
    }

    [Fact]
    public void REVTeX_writes_them_back_where_the_template_has_them()
    {
        var front = Front();
        var doc = Doc("revtex4-2", "aps,prl,reprint", author: front.AuthorText);
        var title = doc.Blocks.First(b => b.Type == "title");
        title.Content = JsonDocument.Parse(JsonSerializer.Serialize(new { title = "Manuscript Title", author = front.AuthorText, date = "", frontExtra = front.FrontExtra }));
        foreach (var latex in new[]
        {
            new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc),
            new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
                .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true }),
        })
        {
            latex.IndexOf(@"\preprint{APS/123-QED}", StringComparison.Ordinal).Should().BeLessThan(latex.IndexOf(@"\title", StringComparison.Ordinal)).And.BeGreaterThan(0);
            latex.Should().Contain("\\thanks{A footnote to the article title}\n\\altaffiliation[Also at ]{Physics Department, XYZ University.}");
            latex.Should().Contain(@"\homepage{http://www.Second.institution.edu/~Charlie.Author}");
            var muso = latex.IndexOf(@"\collaboration{MUSO Collaboration}", StringComparison.Ordinal);
            muso.Should().BeGreaterThan(latex.IndexOf(@"\affiliation{Authors' institution and/or address}", StringComparison.Ordinal));
            muso.Should().BeLessThan(latex.IndexOf(@"\author{Charlie Author}", StringComparison.Ordinal));
            var afterMaketitle = latex[latex.IndexOf(@"\maketitle", StringComparison.Ordinal)..];
            afterMaketitle.Should().NotContain(@"\collaboration").And.NotContain(@"\preprint").And.NotContain(@"\homepage");
        }
    }

    [Fact]
    public void Another_class_compiles_an_author_text_that_keeps_them()
    {
        LaTeXPreamble.JournalShims.Should().Contain(@"\providecommand{\preprint}[1]{}")
            .And.Contain(@"\providecommand{\collaboration}[1]{}")
            .And.Contain(@"\providecommand{\altaffiliation}[2][]{}")
            .And.Contain(@"\providecommand{\homepage}[1]{}");
    }
}
