using System.Text.Json;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The document sweep (chunks 1 and 6: D-020, V-020, 16 Nov 2026): SixtySecondsCV's four-argument \cvitem came in as raw
/// text and the template wasn't recognised as a CV. It now comes in as one.
/// </summary>
public class SixtySecondsCvImportTests
{
    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "latex-corpus", "curated", "cv-templates", "sixtysecondscv.tex"));

    private static string J(object? o) => JsonSerializer.Serialize(o);

    [Fact]
    public async Task SixtySecondsCV_comes_in_as_a_CV()
    {
        var doc = await new LatexParser().ParseTextAsync(Fixture());
        var cv = doc.Elements.OfType<ImportCvBlock>().ToList();

        var person = cv.Single(b => b.BlockType == "personalInfo").Content;
        J(person["name"]).Should().Contain("Raging Bull");
        J(person["headline"]).Should().Contain("Data Scientist");
        J(person["email"]).Should().Contain("mail@dot.com");
        J(person["location"]).Should().Contain("Park Ave");

        cv.Where(b => b.BlockType == "cvSection").Select(b => J(b.Content["title"])).Should()
            .Contain(t => t.Contains("Working Experience")).And.Contain(t => t.Contains("Publications"));
        var entries = cv.Where(b => b.BlockType == "cvEntry").Select(b => J(b.Content)).ToList();
        entries.Should().Contain(e => e.Contains("Post-Doc Panda Studies") && e.Contains("Panda Academy") && e.Contains("2009"));
        entries.Should().Contain(e => e.Contains("Back to Cosmos") && e.Contains("Progress in Physics"));
        // The launch bot's #119: "($\varnothing\, 1,0$)" stayed raw LaTeX in the entry's title.
        entries.Should().Contain(e => e.Contains("Master Theses (∅ 1,0)")).And.NotContain(e => e.Contains("varnothing"));

        var text = string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        text.Should().Contain("Sports").And.Contain("Sailing, Golf");
        text.Should().NotContain("cvitem").And.NotContain("LILIACV").And.NotContain("}{");
    }

    [Fact]
    public void Only_its_class_counts() =>
        SixtySecondsCv.Is("\\documentclass{article}\\begin{document}\\cvitem{a}{b}{c}{d}\\end{document}").Should().BeFalse();

    // The launch bot's #119, on the repository's own file: the sidebar's \social{icon}{link}{text} came out as
    // "web: \aiOverleafSquare", and \cvsignature and \watermark were left in the body (\cvsignature stopped the PDF).
    [Fact]
    public async Task The_sidebar_links_are_read_from_their_icon_and_the_signature_goes()
    {
        const string tex = """
            \documentclass[a4paper]{sixtysecondscv}
            \cvname{Raging Bull}
            \addtofrontsidebar{
              \begin{icontable}{2.5em}{1em}
                \social{\aiOverleafSquare}
                  {https://www.overleaf.com/latex/templates/x}
                  {Overleaf Link}
                \social{\faGithub}
                  {https://github.com/LaGuer/SixtySecondsCV}
                  {Github Project Page Link}
              \end{icontable}
            }
            \begin{document}
            \makefrontsidebar
            \cvsection{Work}
            \begin{cvtable}
              \cvitem{2019}{Role}{Org}{Details.}
            \end{cvtable}
            \cvsignature
            \watermark
            \end{document}
            """;
        var doc = await new LatexParser().ParseTextAsync(tex);
        var socials = J(doc.Elements.OfType<ImportCvBlock>().Single(b => b.BlockType == "personalInfo").Content["socials"]);
        socials.Should().Contain("github").And.Contain("https://github.com/LaGuer/SixtySecondsCV").And.Contain("overleaf");
        socials.Should().NotContain("aiOverleafSquare").And.NotContain(@"\\fa");
        string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text)).Should().NotContain("cvsignature").And.NotContain("watermark");
    }
}
