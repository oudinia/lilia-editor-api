using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A section keeps its <c>\label</c> and its star, and the paragraph after it.
///
/// <para>Found by the LaTeX feature-coverage e2e (refs.*, structure.section-starred).
/// <c>\section{Setup}\label{sec:setup}</c> left the label in the text stream;
/// the paragraph splitter skipped every paragraph that <i>started</i> with
/// <c>\label</c>, so the section's whole first paragraph vanished and each
/// <c>\ref{sec:setup}</c> dangled. <c>\section*</c> imported numbered.</para>
/// </summary>
public class LatexImportSectionLabelTests
{
    private static async Task<List<(string Type, JsonElement Content)>> Import(string body)
    {
        var doc = await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n");
        return LatexImportJobExecutor.MapElements(doc.Elements)
            .Select(b => (b.type, JsonSerializer.SerializeToElement(b.content)))
            .ToList();
    }

    [Fact]
    public async Task The_label_goes_on_the_heading_and_the_paragraph_survives()
    {
        var blocks = await Import("\\section{Setup}\\label{sec:setup}\nText.\nAs in Section~\\ref{sec:setup}.");

        blocks.Select(b => b.Type).Should().Equal("heading", "paragraph");
        blocks[0].Content.GetProperty("text").GetString().Should().Be("Setup");
        blocks[0].Content.GetProperty("label").GetString().Should().Be("sec:setup");
        blocks[1].Content.GetProperty("text").GetString().Should().StartWith("Text.");
    }

    [Fact]
    public async Task A_label_on_the_next_line_is_still_the_headings()
    {
        var blocks = await Import("\\subsection{Method}\n  \\label{sec:method}\n\nWe sampled soil.");

        blocks[0].Content.GetProperty("label").GetString().Should().Be("sec:method");
        blocks[1].Content.GetProperty("text").GetString().Should().Be("We sampled soil.");
    }

    [Fact]
    public async Task A_paragraph_that_opens_with_a_citation_is_kept()
    {
        var blocks = await Import("\\cite{knuth} showed it first.");

        blocks.Should().ContainSingle();
        blocks[0].Content.GetProperty("text").GetString().Should().Be("\\cite{knuth} showed it first.");
    }

    [Fact]
    public async Task A_starred_section_is_unnumbered_and_a_plain_one_says_nothing()
    {
        var blocks = await Import("\\section*{Acknowledgements}\nThanks.\n\n\\section{Results}\nSome.");

        blocks[0].Content.GetProperty("numbered").GetBoolean().Should().BeFalse();
        blocks[2].Content.TryGetProperty("numbered", out _).Should().BeFalse();
        blocks[2].Content.TryGetProperty("label", out _).Should().BeFalse();
    }
}
