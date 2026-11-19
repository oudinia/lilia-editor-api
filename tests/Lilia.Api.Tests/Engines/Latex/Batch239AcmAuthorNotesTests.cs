using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #239 (Overleaf round 1, 19 Nov 2026): ACM's sigconf template lost its author notes on import.
/// \authornote{Both authors contributed equally…} and \orcid{1234-5678-9012} were dropped, and \correspondingauthor and
/// \authornotemark[1] were left in the body after \maketitle ("Undefined control sequence"). The front matter reader read
/// only \author, \affiliation, \email and a full-length \orcid; its scanner skipped commands with no braced argument; and
/// acmart's front matter never wrote an author's notes. They now stay with their \author, both ways.
/// </summary>
public class Batch239AcmAuthorNotesTests
{
    private const string Sigconf = @"\documentclass[sigconf]{acmart}
\begin{document}
\title{The Name of the Title Is Hope}
\author{Ben Trovato}
\authornote{Both authors contributed equally to this research.}
\email{trovato@corporation.com}
\orcid{1234-5678-9012}
\affiliation{\institution{Institute for Clarity in Documentation}\city{Dublin}\country{USA}}
\author{G.K.M. Tobin}
\correspondingauthor
\authornotemark[1]
\email{webmaster@marysville-ohio.com}
\affiliation{\institution{Institute for Clarity in Documentation}\city{Dublin}\country{USA}}
\author{Julius P. Kumquat}
\correspondingauthor
\affiliation{\institution{The Kumquat Consortium}\city{New York}\country{USA}}
\maketitle
Body text.
\end{document}";

    private static IReadOnlyList<TitleAuthor> ReadAuthors() =>
        TitleAuthors.Parse(PublisherFrontMatterReader.Read(Sigconf, "acmart")!.AuthorText);

    [Fact]
    public void An_authornote_and_a_short_ORCID_stay_with_their_author()
    {
        var trovato = ReadAuthors()[0];
        trovato.Name.Should().Be("Ben Trovato");
        trovato.Notes.Should().Equal("Both authors contributed equally to this research.");
        trovato.Orcid.Should().Be("1234-5678-9012", "ACM's template's placeholder, kept as written");
    }

    [Fact]
    public void A_corresponding_author_and_a_note_mark_stay_with_their_author()
    {
        var authors = ReadAuthors();
        authors.Select(a => a.Name).Should().Equal("Ben Trovato", "G.K.M. Tobin", "Julius P. Kumquat");
        authors[1].Corresponding.Should().BeTrue();
        authors[1].NoteMarks.Should().Equal("1");
        authors[1].Notes.Should().BeEmpty("a mark points at Trovato's note, it is not a second one");
        authors[2].Corresponding.Should().BeTrue();
        authors[0].Corresponding.Should().BeFalse();
    }

    [Fact]
    public async Task The_marks_no_longer_land_in_the_body_after_maketitle()
    {
        var doc = await new LatexParser().ParseTextAsync(Sigconf);
        var body = string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        body.Should().Contain("Body text.");
        body.Should().NotContain("correspondingauthor").And.NotContain("authornotemark");
    }

    [Fact]
    public void Acmart_writes_the_notes_back_after_each_author()
    {
        var doc = Doc("acmart", "sigconf", author: PublisherFrontMatterReader.Read(Sigconf, "acmart")!.AuthorText);
        foreach (var latex in new[]
        {
            new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc),
            new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
                .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true }),
        })
        {
            latex.Should().Contain("\\author{Ben Trovato}\n\\authornote{Both authors contributed equally to this research.}");
            latex.Should().Contain(@"\orcid{1234-5678-9012}");
            latex.Should().Contain("\\author{G.K.M. Tobin}\n\\authornotemark[1]\n\\correspondingauthor");
            latex.Should().Contain("\\author{Julius P. Kumquat}\n\\correspondingauthor");
            var afterMaketitle = latex[latex.IndexOf(@"\maketitle", StringComparison.Ordinal)..];
            afterMaketitle.Should().NotContain(@"\correspondingauthor").And.NotContain(@"\authornotemark");
        }
    }

    [Fact]
    public void Another_class_compiles_an_author_text_that_keeps_the_marks()
    {
        // A paper imported from acmart and later set to article keeps "\correspondingauthor" in its author text.
        LaTeXPreamble.JournalShims.Should().Contain(@"\providecommand{\correspondingauthor}{}")
            .And.Contain(@"\providecommand{\authornotemark}[1][]{}");
    }

    [Fact]
    public void A_real_ORCID_is_still_read_for_other_classes_and_a_short_one_is_not()
    {
        // The placeholder is accepted for acmart only, which prints what it is given.
        var revtex = PublisherFrontMatterReader.Read(@"\documentclass{revtex4-2}
\begin{document}
\title{T}
\author{Ada Lovelace}
\orcid{1234-5678-9012}
\affiliation{Analytical Engine Ltd}
\maketitle
\end{document}", "revtex4-2")!;
        revtex.AuthorText.Should().NotContain("1234-5678-9012");
    }
}
