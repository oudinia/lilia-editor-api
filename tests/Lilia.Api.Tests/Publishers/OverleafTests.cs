using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines.Publishers;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>Overleaf import (25 Oct 2026): a publisher paper's front matter read into Lilia's fields.</summary>
public class PublisherFrontMatterReaderTests
{
    private const string Acm = """
        \documentclass[sigconf,review,anonymous]{acmart}
        \setcopyright{acmlicensed}
        \acmConference[CHI '27]{CHI Conference}{April 2027}{Paris, France}
        \begin{document}
        \title{A Paper}
        \author{Ada Lovelace}
        \affiliation{\department{Mathematics}\institution{Analytical Engines}\city{London}\country{UK}}
        \email{ada@engines.uk}
        \author{Alan Turing}
        \affiliation{\institution{Bletchley Park}\country{UK}}
        \begin{abstract}It works.\end{abstract}
        \begin{CCSXML}
        <ccs2012></ccs2012>
        \end{CCSXML}
        \ccsdesc[500]{Software and its engineering~Compilers}
        \keywords{LaTeX, publishing}
        \maketitle
        \section{Intro} Text.
        \end{document}
        """;

    [Fact]
    public void ACM_authors_affiliations_emails_keywords_CCS_and_rights()
    {
        var r = PublisherFrontMatterReader.Read(Acm, "acmart")!;
        r.Authors.Select(a => a.Name).Should().Equal("Ada Lovelace", "Alan Turing");
        r.Authors[0].Lines.Should().Equal("Mathematics, Analytical Engines", "UK", "ada@engines.uk");
        r.Authors[1].Lines.Should().Equal("Bletchley Park", "UK");
        r.Keywords.Should().Equal("LaTeX", "publishing");
        r.AcmCcs.Should().Contain(@"\ccsdesc[500]{Software and its engineering~Compilers}").And.Contain("CCSXML");
        r.AcmRights.Should().Contain(@"\setcopyright{acmlicensed}").And.Contain(@"\acmConference[CHI '27]{CHI Conference}{April 2027}{Paris, France}");
        r.AuthorText.Should().Be(@"Ada Lovelace\\Mathematics, Analytical Engines\\UK\\ada@engines.uk \and Alan Turing\\Bletchley Park\\UK");
    }

    [Theory]
    [InlineData("IEEEtran", """\author{\IEEEauthorblockN{Ada Lovelace}\IEEEauthorblockA{Analytical Engines\\London, UK} \and \IEEEauthorblockN{Alan Turing}\IEEEauthorblockA{Bletchley Park}}\maketitle\begin{IEEEkeywords}LaTeX, publishing\end{IEEEkeywords}""")]
    [InlineData("elsarticle", """\begin{frontmatter}\author[a]{Ada Lovelace}\author[b]{Alan Turing}\affiliation[a]{organization={Analytical Engines},city={London},country={UK}}\affiliation[b]{organization={Bletchley Park},country={UK}}\begin{keyword}LaTeX \sep publishing\end{keyword}\end{frontmatter}""")]
    [InlineData("llncs", """\author{Ada Lovelace\inst{1} \and Alan Turing\inst{2}}\institute{Analytical Engines, London \and Bletchley Park}\maketitle\begin{abstract}x\keywords{LaTeX \and publishing}\end{abstract}""")]
    [InlineData("revtex4-2", """\author{Ada Lovelace}\affiliation{Analytical Engines, London}\email{ada@engines.uk}\author{Alan Turing}\affiliation{Bletchley Park}\keywords{LaTeX, publishing}\maketitle""")]
    [InlineData("amsart", """\author{Ada Lovelace}\address{Analytical Engines\\London}\email{ada@engines.uk}\author{Alan Turing}\address{Bletchley Park}\keywords{LaTeX, publishing}\maketitle""")]
    [InlineData("iopjournal", """\author{Ada Lovelace$^1$ and Alan Turing$^2$}\affil{$^1$Analytical Engines, London}\affil{$^2$Bletchley Park}\keywords{LaTeX, publishing}""")]
    [InlineData("siamart251216", """\author{Ada Lovelace\thanks{Analytical Engines, London (\email{ada@engines.uk}).} \and Alan Turing\thanks{Bletchley Park.}}\maketitle\begin{keywords}LaTeX, publishing\end{keywords}""")]
    public void Every_publisher_class_gives_authors_their_affiliations_and_keywords(string cls, string front)
    {
        var r = PublisherFrontMatterReader.Read($"\\documentclass{{{cls}}}\n\\begin{{document}}\n{front}\n\\end{{document}}", cls)!;
        r.Authors.Select(a => a.Name).Should().Equal(["Ada Lovelace", "Alan Turing"], cls);
        r.Authors[0].Lines.Should().Contain(l => l.Contains("Analytical Engines"), cls);
        r.Authors[1].Lines.Should().Contain(l => l.Contains("Bletchley Park"), cls);
        r.Keywords.Should().Equal(["LaTeX", "publishing"], cls);
    }

    [Theory]
    [InlineData("acmart", "sigconf,review,anonymous", "acm-sigconf-review")]
    [InlineData("acmart", "sigconf,screen", "acm-sigconf-final")]
    [InlineData("acmart", "manuscript,review,anonymous", "acm-journal-review")]
    [InlineData("acmart", "acmsmall", "acm-journal-final")]
    [InlineData("IEEEtran", "journal", "ieee-journal")]
    [InlineData("IEEEtran", "conference", "ieee-conference")]
    [InlineData("elsarticle", "review", "elsevier-review")]
    [InlineData("elsarticle", "final,5p,times", "elsevier-final")]
    [InlineData("elsarticle", "preprint,12pt", "elsevier-preprint")]
    [InlineData("llncs", "", "lncs-submission")]
    [InlineData("revtex4-2", "aip,reprint", "aip-final")]
    [InlineData("revtex4-2", "aps,prl,preprint", "aps-review")]
    [InlineData("amsart", "", "ams-journal")]
    [InlineData("iopjournal", "anonymous", "iop-review")]
    [InlineData("siamart220329", "review", "siam-review")]
    [InlineData("article", "11pt", null)]
    public void A_class_and_its_options_name_the_venue(string cls, string options, string? venue) =>
        PublisherProfiles.FromClass(cls, options).Should().Be(venue);

    [Fact]
    public async Task Imported_ACM_front_matter_fills_the_title_and_leaks_nothing_into_the_body()
    {
        var doc = await new LatexParser().ParseTextAsync(Acm, new Lilia.Import.Interfaces.LatexImportOptions { ExtractDocumentTitle = true });
        doc.Metadata.Author.Should().Contain("Analytical Engines").And.Contain("ada@engines.uk");
        doc.Metadata.Keywords.Should().Be("LaTeX, publishing");
        doc.Metadata.AcmCcs.Should().Contain("Compilers");
        doc.Metadata.AcmRights.Should().Contain("acmlicensed");
        doc.Metadata.RawClassOptions.Should().Be("sigconf,review,anonymous");
        var body = JsonSerializer.Serialize(doc.Elements);
        foreach (var leak in new[] { "acmlicensed", "Compilers", "ada@engines.uk", "CCSXML", "CHI Conference" })
            body.Should().NotContain(leak);

        var (_, content) = LatexImportJobExecutor.TitleBlockFor(doc)!.Value;
        var json = JsonSerializer.SerializeToElement(content);
        json.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()).Should().Equal("LaTeX", "publishing");
        json.GetProperty("acmCcs").GetString().Should().Contain(@"\ccsdesc");
        json.GetProperty("acmRights").GetString().Should().Contain(@"\setcopyright");
    }
}

/// <summary>Open in Overleaf: a ten-minute link to the project for Overleaf to fetch.</summary>
[Collection("Integration")]
public class OverleafLinkTests : IntegrationTestBase
{
    private readonly string _userId = $"overleaf-{Guid.NewGuid():N}"[..28];
    public OverleafLinkTests(TestDatabaseFixture fixture) : base(fixture) { }
    public override async Task InitializeAsync() => await SeedUserAsync(_userId);

    [Fact]
    public async Task The_link_opens_in_Overleaf_serves_the_project_once_asked_and_nobody_else_can_make_one()
    {
        var api = CreateClientAs(_userId);
        var doc = await SeedDocumentAsync(_userId, "My Paper");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Hello."}""", 0);

        var res = await api.PostAsync($"/api/documents/{doc.Id}/overleaf", null);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var url = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        url.Should().StartWith("https://www.overleaf.com/docs?snip_uri=").And.Contain("engine=pdflatex");
        var zipUrl = Uri.UnescapeDataString(url.Split("snip_uri=")[1].Split('&')[0]);
        var path = new Uri(zipUrl).AbsolutePath;

        // Overleaf fetches it without signing in to Lilia.
        var fetched = await CreateAnonymousClient().GetAsync(path);
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var zip = new ZipArchive(await fetched.Content.ReadAsStreamAsync()))
            zip.Entries.Should().Contain(e => e.FullName.EndsWith("main.tex"));
        (await CreateAnonymousClient().GetAsync("/api/public/overleaf/" + new string('0', 48) + ".zip")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var stranger = $"overleaf-x-{Guid.NewGuid():N}"[..28];
        await SeedUserAsync(stranger);
        (await CreateClientAs(stranger).PostAsync($"/api/documents/{doc.Id}/overleaf", null)).IsSuccessStatusCode.Should().BeFalse();
    }
}
