using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's conference papers (18 Nov 2026): a {{whole title}} and the report type were lost from references.bib
/// (#220), and acmart lost its multi-line \acmConference, the city and state, the ORCID and \shortauthors (#219).
/// </summary>
public class Batch13BibAcmTests
{
    [Fact]
    public void A_whole_braced_title_and_the_report_type_reach_references_bib()
    {
        var entry = BibTexParser.Parse("""
            @techreport{rfc8446,
              author       = {E. Rescorla},
              title        = {{The Transport Layer Security (TLS) Protocol Version 1.3}},
              institution  = {IETF},
              type         = {RFC},
              number       = {8446},
              year         = {2018},
              organization = {IETF}
            }
            """).Single();
        entry.Fields["titleprotected"].Should().Be("true");
        var bib = BibTeXSerializer.Serialize([new BibliographyEntry
        {
            CiteKey = entry.CiteKey, EntryType = entry.EntryType,
            Data = JsonDocument.Parse(JsonSerializer.Serialize(entry.Fields)),
        }]);
        bib.Should().Contain("title = {{The Transport Layer Security (TLS) Protocol Version 1.3}},");
        bib.Should().Contain("type = {RFC},").And.Contain("organization = {IETF},").And.NotContain("titleprotected");
    }

    [Fact]
    public void A_title_braced_in_parts_is_not_whole() =>
        BibTexParser.WholeBraced("{{MIMETIC}: a method} and {more}").Should().BeFalse();

    private const string Acm = """
        \documentclass[sigconf]{acmart}
        \acmConference[Conference acronym 'XX]{Make sure to enter the correct
          conference title from your rights confirmation email}{June 03--05,
          2018}{Woodstock, NY}
        \begin{document}
        \title{SurvEval}
        \author{YaChen Yan}
        \email{yachen_yan@intuit.com}
        \orcid{0000-0002-1213-4343}
        \affiliation{%
            \institution{Intuit Credit Karma}
            \streetaddress{760 Market Street}
            \city{Mountain View}
            \state{California}
            \country{USA}
            \postcode{94102}
        }
        \renewcommand{\shortauthors}{YaChen and LiuBo}
        \begin{abstract}A.\end{abstract}
        \maketitle
        \end{document}
        """;

    [Fact]
    public void Acmart_keeps_the_conference_city_orcid_and_short_authors()
    {
        var front = PublisherFrontMatterReader.Read(Acm, "acmart")!;
        front.AcmRights.Should().Contain(@"\acmConference[Conference acronym 'XX]{Make sure to enter the correct conference title from your rights confirmation email}{June 03--05, 2018}{Woodstock, NY}");
        AcmMetadata.Rights(front.AcmRights).Accepted.Should().Contain(@"\acmConference");
        var author = front.Authors.Single();
        author.Lines.Should().Equal("Intuit Credit Karma", "Mountain View, California", "USA", "yachen_yan@intuit.com");

        var (pre, body) = PublisherClasses.FrontMatter("acmart", "SurvEval", front.AuthorText, null, [], s => s, "SurvEval",
            acmRights: front.AcmRights, authorRunning: "YaChen and LiuBo");
        pre.Should().Contain(@"\orcid{0000-0002-1213-4343}");
        pre.Should().Contain(@"\city{Mountain View, California}").And.Contain(@"\country{USA}");
        pre.IndexOf(@"\email{", StringComparison.Ordinal).Should().BeLessThan(pre.IndexOf(@"\affiliation{", StringComparison.Ordinal));
        pre.Should().Contain(@"\acmConference");
        body.Should().Contain(@"\renewcommand{\shortauthors}{YaChen and LiuBo}");
    }

    [Fact]
    public async Task The_short_authors_are_read_on_import()
        => (await new LatexParser().ParseTextAsync(Acm)).Metadata.AuthorRunning.Should().Be("YaChen and LiuBo");
}
