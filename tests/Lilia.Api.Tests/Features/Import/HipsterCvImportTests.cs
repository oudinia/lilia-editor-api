using System.Text.Json;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The document sweep (D-015, V-015, 16 Nov 2026): the simple hipster CV's macros came in as brace text
/// ("headercolour{Jack}{Sparrow}{Captain}{white}", "cvgreen{white}{About me}") and the template wasn't recognised as a CV.
/// It now comes in as one.
/// </summary>
public class HipsterCvImportTests
{
    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "latex-corpus", "curated", "cv-templates", "hipster-cv.tex"));

    private static string J(object? o) => JsonSerializer.Serialize(o);

    [Fact]
    public async Task The_hipster_CV_comes_in_as_a_CV()
    {
        var doc = await new LatexParser().ParseTextAsync(Fixture());
        var cv = doc.Elements.OfType<ImportCvBlock>().ToList();

        var person = cv.Single(b => b.BlockType == "personalInfo").Content;
        J(person["name"]).Should().Contain("Jack Sparrow");
        J(person["headline"]).Should().Contain("Captain");
        J(person["email"]).Should().Contain("jack@sparrow.org");
        J(person["socials"]).Should().Contain("twitter").And.Contain("@sparrow").And.Contain("github");

        cv.Where(b => b.BlockType == "cvSection").Select(b => J(b.Content["title"])).Should()
            .Contain(t => t.Contains("About me")).And.Contain(t => t.Contains("Curriculum")).And.Contain(t => t.Contains("Degrees"));
        var entries = cv.Where(b => b.BlockType == "cvEntry").Select(b => J(b.Content)).ToList();
        entries.Should().Contain(e => e.Contains("Captain of the Black Pearl") && e.Contains("2018") && e.Contains("East Indies"));
        entries.Should().Contain(e => e.Contains("Freelance Pirate") && e.Contains("Tortuga"));
        entries.Should().Contain(e => e.Contains("Bucaneering") && e.Contains("1715") && e.Contains("London"));
        entries.Should().NotContain(e => e.Contains("color") || e.Contains("disney.png") || e.Contains("lorem"));

        var text = string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        text.Should().Contain("nationality: English").And.Contain("Privateering");
        foreach (var leak in new[] { "headercolour", "cvgreen{", "infobubble", "barrule", "pictofraction", "roundpic", "switchcolumn", "LILIACV", "subsection*", "lorem" })
            text.Should().NotContain(leak);
    }

    [Fact]
    public void Only_its_class_counts() =>
        HipsterCv.Is("\\documentclass{article}\\begin{document}\\bg{a}{b}{c}\\end{document}").Should().BeFalse();
}
