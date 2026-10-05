using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A heading's label reaches the LaTeX the preview, the PDF compile and Ask Lilia
/// use, and deep levels stay at the deepest sectioning command.
///
/// <para>Found while checking Ask Lilia's fixtures (29 Sep): a heading with a
/// <c>label</c> emitted a bare <c>\section{Intro}</c>, so every
/// <c>\ref{sec:intro}</c> in a document imported from LaTeX printed "??". The
/// exporter (LaTeXExportService) always wrote it; this emitter did not. Level 6
/// and up also jumped back to <c>\section</c>.</para>
/// </summary>
public class HeadingLatexLabelTests
{
    private static string Emit(string contentJson)
    {
        var service = new RenderService(null!, new Mock<ILogger<RenderService>>().Object);
        return service.RenderBlockToLatex(new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "heading",
            Content = JsonDocument.Parse(contentJson),
            SortOrder = 0,
        }).Trim();
    }

    [Fact]
    public void A_label_follows_the_sectioning_command() =>
        Emit("""{"text":"Intro","level":1,"label":"sec:intro"}""").Should().Be(@"\section{Intro}\label{sec:intro}");

    [Fact]
    public void The_LML_id_is_the_label_when_there_is_no_label() =>
        Emit("""{"text":"Methods","level":2,"id":"sec:methods"}""").Should().Be(@"\subsection{Methods}\label{sec:methods}");

    [Fact]
    public void A_label_wins_over_an_id() =>
        Emit("""{"text":"A","level":1,"label":"sec:a","id":"sec:b"}""").Should().Be(@"\section{A}\label{sec:a}");

    [Fact]
    public void A_starred_heading_keeps_its_label() =>
        Emit("""{"text":"Intro","level":2,"numbered":false,"label":"sec:x"}""").Should().Be(@"\subsection*{Intro}\label{sec:x}");

    [Fact]
    public void No_label_emits_none() =>
        Emit("""{"text":"Intro","level":1}""").Should().Be(@"\section{Intro}");

    [Theory]
    [InlineData(1, "section")]
    [InlineData(2, "subsection")]
    [InlineData(3, "subsubsection")]
    [InlineData(4, "paragraph")]
    [InlineData(5, "subparagraph")]
    [InlineData(6, "subparagraph")]
    [InlineData(9, "subparagraph")]
    public void Levels_map_to_the_sectioning_commands(int level, string command) =>
        Emit($$"""{"text":"T","level":{{level}}}""").Should().Be($@"\{command}{{T}}");

    // ── review of 5 Oct: a label is LaTeX syntax ─────────────────────────

    [Fact]
    public void Characters_LaTeX_reads_as_syntax_are_replaced_in_a_heading_label() =>
        Emit("""{"text":"Results","level":1,"label":"sec:50% #1 }x"}""").Should().Be(@"\section{Results}\label{sec:50- -1 -x}");

    [Theory]
    [InlineData("sec:intro", "sec:intro")]
    [InlineData("  sec:a b ", "sec:a b")]
    [InlineData(@"a\b{c}~d^e&f$g", "a-b-c--d-e-f-g")]
    [InlineData(null, "")]
    public void Safe_keeps_ordinary_labels_and_replaces_syntax(string? input, string expected) =>
        Lilia.Engines.LabelKey.Safe(input).Should().Be(expected);

    [Fact]
    public void The_reference_key_matches_the_label_after_cleaning()
    {
        Lilia.Engines.LabelKey.Effective("equation", "main%1").Should().Be("eq:main-1");
        Lilia.Engines.LabelKey.Effective("table", "tab:a#b").Should().Be("tab:a-b");
    }
}
