using System.Text.Json;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The document sweep (chunks 1 and 6: V-009, V-016, 16 Nov 2026): AltaCV and maltacv came in as raw macro text
/// ("\personalinfo{ …", "English{5}", a wheel chart's "6/8em/accent!30/…") and weren't recognised as CVs. They now come in
/// as a CV: Personal information, sections and entries.
/// </summary>
public class AltaCvImportTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "latex-corpus", "curated", "cv-templates", name));

    private static string J(object? o) => JsonSerializer.Serialize(o);

    private static string AllText(ImportDocument doc) =>
        string.Join("\n", doc.Elements.Select(e => e switch
        {
            ImportParagraph p => p.Text,
            ImportListItem i => i.Text,
            ImportCvBlock c => J(c.Content),
            _ => "",
        }));

    [Fact]
    public async Task AltaCV_comes_in_as_a_CV_with_its_header_sections_and_entries()
    {
        var doc = await new LatexParser().ParseTextAsync(Fixture("altacv.tex"));
        var cv = doc.Elements.OfType<ImportCvBlock>().ToList();

        var person = cv.Single(b => b.BlockType == "personalInfo").Content;
        J(person["name"]).Should().Contain("Your Name Here");
        J(person["headline"]).Should().Contain("Your Position or Tagline Here");
        J(person["email"]).Should().Contain("your_name@email.com");
        J(person["socials"]).Should().Contain("linkedin").And.Contain("github");

        cv.Where(b => b.BlockType == "cvSection").Select(b => J(b.Content["title"])).Should()
            .Contain(t => t.Contains("Experience")).And.Contain(t => t.Contains("Education")).And.Contain(t => t.Contains("Referees"));
        var entries = cv.Where(b => b.BlockType == "cvEntry").Select(b => J(b.Content)).ToList();
        entries.Should().Contain(e => e.Contains("Job Title 1") && e.Contains("Company 1") && e.Contains("Ongoing"));
        entries.Should().Contain(e => e.Contains("Fantastic Achievement") && e.Contains("some details"));
        entries.Should().Contain(e => e.Contains("Spanish") && e.Contains("4/5"));
        entries.Should().Contain(e => e.Contains("Alpha Beta") && e.Contains("a.beta@university.edu"));

        var text = AllText(doc);
        text.Should().NotContain("personalinfo").And.NotContain("accent!").And.NotContain("LILIACV").And.NotContain("{5}");
        text.Should().Contain("Sleep");                      // the wheel chart's slices, as a list
        text.Should().Contain("Hard-working · Eye for detail"); // the tags, as one line
        // The launch bot's #119: control spaces ("Ph.D.\ in", "Prof.\ Alpha") stayed; the mailing address was dropped.
        text.Should().NotContain(@".\ ");
        J(person["extra"]).Should().Contain("Street");
    }

    [Fact]
    public async Task maltacv_comes_in_as_a_CV_too()
    {
        var doc = await new LatexParser().ParseTextAsync(Fixture("maltacv.tex"));
        var cv = doc.Elements.OfType<ImportCvBlock>().ToList();
        cv.Should().Contain(b => b.BlockType == "personalInfo");
        var entries = cv.Where(b => b.BlockType == "cvEntry").Select(b => J(b.Content)).ToList();
        entries.Should().Contain(e => e.Contains("M.Sc. Snack Science") && e.Contains("University of Nowhere"));
        entries.Should().Contain(e => e.Contains("Summer Internship") && e.Contains("Pisa, Italy"));
        AllText(doc).Should().Contain("Pizza · Chocolate").And.NotContain("cvlistitem").And.NotContain("LILIACV");
    }

    [Fact]
    public async Task An_ordinary_article_is_not_AltaCV()
    {
        AltaCv.Is("\\documentclass{article}\\begin{document}\\section{A}\\end{document}").Should().BeFalse();
        var doc = await new LatexParser().ParseTextAsync("\\documentclass{article}\\begin{document}\\section{Intro}Text.\\end{document}");
        doc.Elements.OfType<ImportCvBlock>().Should().BeEmpty();
    }

    [Fact]
    public void A_commented_out_class_line_does_not_count() =>
        AltaCv.Is("% \\documentclass{altacv}\n\\documentclass{article}\n\\begin{document}x\\end{document}").Should().BeFalse();
}
