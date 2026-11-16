using System.Text.Json;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The document sweep (D-013, D-014, 16 Nov 2026): the "resume" template family (Jake's resume and its forks) imported
/// as raw macro text ("\resumeSubheading {Amazon}{May 2017 -- Aug. 2017} {Software…"). It now comes in as a CV.
/// </summary>
public class ResumeTemplateImportTests
{
    private const string Jake = """
        \documentclass[letterpaper,11pt]{article}
        \newcommand{\resumeItem}[1]{\item\small{#1}}
        \newcommand{\resumeSubheading}[4]{\item\begin{tabular*}{0.97\textwidth}[t]{l@{\extracolsep{\fill}}r}\textbf{#1} & #2 \\ \textit{\small#3} & \textit{\small #4} \\ \end{tabular*}}
        \newcommand{\resumeProjectHeading}[2]{\item #1 & #2}
        \newcommand{\resumeSubHeadingListStart}{\begin{itemize}}
        \newcommand{\resumeSubHeadingListEnd}{\end{itemize}}
        \newcommand{\resumeItemListStart}{\begin{itemize}}
        \newcommand{\resumeItemListEnd}{\end{itemize}}
        \begin{document}
        \section{Experience}
          \resumeSubHeadingListStart
            \resumeSubheading
              {Southwestern University}{Georgetown, TX}
              {Undergraduate Research Assistant}{June 2020 -- Present}
              \resumeItemListStart
                \resumeItem{Developed a REST API}
                \resumeItem{Explored methods to generate video game dungeons}
              \resumeItemListEnd
          \resumeSubHeadingListEnd
        \section{Projects}
          \resumeSubHeadingListStart
            \resumeProjectHeading{Gitlytics}{June 2020 -- Present}
          \resumeSubHeadingListEnd
        \end{document}
        """;

    private static string J(object? o) => JsonSerializer.Serialize(o);

    [Fact]
    public void Jakes_order_is_read_by_content()
        => ResumeTemplate.SubheadingAsEntry(["Southwestern University", "Georgetown, TX", "Undergraduate Research Assistant", "June 2020 -- Present"])
            .Should().Equal("June 2020 -- Present", "Undergraduate Research Assistant", "Southwestern University", "Georgetown, TX", "", "");

    [Fact]
    public void A_fork_with_the_dates_second_is_read_the_same()
        => ResumeTemplate.SubheadingAsEntry(["Google Verily", "Aug. 2018 -- Sept. 2019", "Software Engineer", "San Francisco, CA"])
            .Should().Equal("Aug. 2018 -- Sept. 2019", "Software Engineer", "Google Verily", "San Francisco, CA", "", "");

    [Fact]
    public async Task A_Jake_resume_comes_in_as_CV_sections_entries_and_bullets()
    {
        var doc = await new LatexParser().ParseTextAsync(Jake);
        var cv = doc.Elements.OfType<ImportCvBlock>().ToList();

        cv.Select(b => b.BlockType).Should().Equal("cvSection", "cvEntry", "cvSection", "cvEntry");
        var entry = cv[1].Content;
        J(entry["org"]).Should().Contain("Southwestern University");
        J(entry["role"]).Should().Contain("Undergraduate Research Assistant");
        J(entry["location"]).Should().Contain("Georgetown, TX");
        J(cv[3].Content["role"]).Should().Contain("Gitlytics");

        var items = doc.Elements.OfType<ImportListItem>().Select(i => i.Text).ToList();
        items.Should().Contain(i => i.Contains("Developed a REST API"));
        doc.Elements.OfType<ImportCvBlock>().Should().NotContain(b => b.BlockType == "personalInfo");

        var text = string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        text.Should().NotContain("resume").And.NotContain("LILIACV");
    }

    [Fact]
    public async Task An_ordinary_article_is_not_a_resume()
    {
        var doc = await new LatexParser().ParseTextAsync("\\documentclass{article}\\begin{document}\\section{Intro}Text.\\end{document}");
        doc.Elements.OfType<ImportCvBlock>().Should().BeEmpty();
    }
}
