using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The launch bot's #112 (15 Nov 2026): a moderncv source imported with the name and contact details blank,
/// \makecvtitle as text, and \cventry / \cvitem flattened into brace text. It now comes in as a CV: a Personal
/// information block, CV sections and CV entries, named after the person.
/// </summary>
public class ModernCvImportTests
{
    private const string Ada = """
        \documentclass[11pt,a4paper,sans]{moderncv}
        \moderncvstyle{classic}
        \moderncvcolor{blue}
        \name{Ada}{Lovelace}
        \title{Analyst}
        \address{12 St James's Square}{London}{England}
        \phone[mobile]{+44 20 0000 0000}
        \email{ada@example.org}
        \homepage{example.org/ada}
        \social[github]{ada}
        \begin{document}
        \makecvtitle
        \section{Education}
        \cventry{1835--1838}{Mathematics}{University of London}{London}{}{Studied analysis with De Morgan.}
        \section{Skills}
        \cvitem{Languages}{English, French}
        \cvitem{}{Plain text item}
        \end{document}
        """;

    private static Task<ImportDocument> Parse(string latex) => new LatexParser().ParseTextAsync(latex);

    private static string Json(object o) => JsonSerializer.Serialize(o);

    [Fact]
    public async Task The_CV_comes_in_as_CV_blocks_in_order()
    {
        var doc = await Parse(Ada);
        var blocks = LatexImportJobExecutor.MapElements(doc.Elements);

        blocks.Select(b => b.type).Should().StartWith(["personalInfo", "cvSection", "cvEntry", "cvSection"]);
        blocks.Skip(4).Select(b => b.type).Should().OnlyContain(t => t == "paragraph");
    }

    [Fact]
    public async Task The_person_is_named_with_their_headline_and_contact_details()
    {
        var doc = await Parse(Ada);
        var person = doc.Elements.OfType<ImportCvBlock>().Single(b => b.BlockType == "personalInfo").Content;

        person["name"].Should().Be("Ada Lovelace");
        person["headline"].Should().Be("Analyst", "moderncv's \\title is the person's headline");
        person["email"].Should().Be("ada@example.org");
        person["homepage"].Should().Be("example.org/ada");
        person["location"].Should().Be("12 St James's Square, London, England");
        Json(person["phones"]!).Should().Contain("44 20 0000 0000").And.Contain("mobile");
        Json(person["socials"]!).Should().Contain("github").And.Contain("ada");
        doc.Title.Should().Be("Ada Lovelace", "the document is named after the person, not the headline");
        LatexImportJobExecutor.TitleBlockFor(doc).Should().BeNull("the Personal information block says who it is");
    }

    [Fact]
    public async Task A_cventry_s_six_arguments_fill_the_entry()
    {
        var doc = await Parse(Ada);
        var entry = doc.Elements.OfType<ImportCvBlock>().Single(b => b.BlockType == "cvEntry").Content;

        entry["period"].Should().Be("1835–1838");
        entry["role"].Should().Be("Mathematics");
        entry["org"].Should().Be("University of London");
        entry["location"].Should().Be("London");
        entry["highlight"].Should().Be("");
        entry["description"].Should().Be("Studied analysis with De Morgan.");
    }

    [Fact]
    public async Task Nothing_leaks_as_LaTeX_or_brace_text()
    {
        var doc = await Parse(Ada);
        var text = string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));

        text.Should().Contain("Languages").And.Contain("English, French").And.Contain("Plain text item");
        text.Should().NotContain("{").And.NotContain("}").And.NotContain("makecvtitle").And.NotContain("LILIACV")
            .And.NotContain("moderncv");
    }

    [Fact]
    public async Task Sections_are_CV_sections_only_in_a_CV()
    {
        var article = await Parse("""
            \documentclass{article}
            \begin{document}
            \section{Introduction}
            Some text.
            \end{document}
            """);

        article.Elements.OfType<ImportCvBlock>().Should().BeEmpty();
        article.Elements.OfType<ImportHeading>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_CV_without_makecvtitle_still_starts_with_the_person()
    {
        var doc = await Parse(Ada.Replace(@"\makecvtitle", ""));

        doc.Elements.First().Should().BeOfType<ImportCvBlock>().Which.BlockType.Should().Be("personalInfo");
    }

    [Theory]
    [InlineData(@"\cvitem[1em]{Skills}{C\#}", "**Skills** — C\\#")]
    [InlineData(@"\cvlistitem{Reading}", "Reading")]
    public void The_cvitem_family_becomes_text(string latex, string expected)
        => ModernCv.Mark(latex).Trim().Should().Be(expected);

    [Fact]
    public void Arguments_read_as_text()
        => ModernCv.Clean(@"Jan~2020 -- now\\ \textbf{lead}").Should().Be("Jan 2020 – now lead");
}
