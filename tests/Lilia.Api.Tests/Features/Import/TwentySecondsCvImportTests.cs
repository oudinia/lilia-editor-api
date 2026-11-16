using System.Text.Json;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The document sweep (chunks 1 and 6: D-011, V-011, 16 Nov 2026): Twenty Seconds CV's \twentyitem came in as raw text and
/// the template wasn't recognised as a CV.
/// </summary>
public class TwentySecondsCvImportTests
{
    private static string J(object? o) => JsonSerializer.Serialize(o);

    [Fact]
    public async Task The_six_argument_fork_comes_in_as_a_CV()
    {
        var tex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "latex-corpus", "curated", "cv-templates", "entry-level-resume.tex"));
        var doc = await new LatexParser().ParseTextAsync(tex);
        var cv = doc.Elements.OfType<ImportCvBlock>().ToList();

        var person = cv.Single(b => b.BlockType == "personalInfo").Content;
        J(person["name"]).Should().Contain("HARSH GADGIL");
        J(person["headline"]).Should().Contain("Data Engineer");
        J(person["email"]).Should().Contain("harsh.gadgil@gmail.com");
        J(person["socials"]).Should().Contain("linkedin").And.Contain("github");

        var entries = cv.Where(b => b.BlockType == "cvEntry").Select(b => J(b.Content)).ToList();
        entries.Should().Contain(e => e.Contains("Machine Learning Engineer") && e.Contains("April 2017") && e.Contains("Present"));
        doc.Elements.OfType<ImportListItem>().Should().Contain(i => i.Text.Contains("SMS spam detection"));
        var text = string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        text.Should().NotContain("twentyitem").And.NotContain("LILIACV").And.NotContain("{}  {");
    }

    [Fact]
    public async Task The_original_four_arguments_are_read_too()
    {
        const string tex = """
            \documentclass{twentysecondcv}
            \cvname{Ada Lovelace}
            \cvjobtitle{Analyst}
            \begin{document}
            \makeprofile
            \section{Experience}
            \begin{twenty}
            \twentyitem{1843}{Notes}{London}{Wrote the first program.}
            \end{twenty}
            \end{document}
            """;
        var doc = await new LatexParser().ParseTextAsync(tex);
        var entry = J(doc.Elements.OfType<ImportCvBlock>().Single(b => b.BlockType == "cvEntry").Content);
        entry.Should().Contain("1843").And.Contain("Notes").And.Contain("London");
        string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text)).Should().Contain("Wrote the first program.");
    }
}
