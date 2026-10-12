using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Lilia's own shorthands for pointing at things from text (report of 12 Oct 2026: the compile
/// emitter printed them as typed, so the PDF read "Theorem~(thm:pyth)", "@ref{…}", "@cite{…}").
/// The help and the AI skills teach <c>Kind~(label)</c>; the Cross-references panel inserts
/// <c>@ref{label}</c>; LML and the skills write <c>@cite{key}</c>.
/// </summary>
public class CrossRefSyntaxTests
{
    private readonly RenderService _render = new(null!, new Mock<ILogger<RenderService>>().Object);

    private static Block Paragraph(string text, string type = "paragraph", object? content = null) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content ?? new { text })),
        SortOrder = 0,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private string Render(string text) => _render.RenderBlockToLatex(Paragraph(text));

    [Theory]
    [InlineData("See Equation~(eq:loss).", @"See Equation~\eqref{eq:loss}.")]
    [InlineData("See Figure~(fig:arch).", @"See Figure~\ref{fig:arch}.")]
    [InlineData("See Table~(tab:acc).", @"See Table~\ref{tab:acc}.")]
    [InlineData("See Theorem~(thm:main).", @"See Theorem~\ref{thm:main}.")]
    [InlineData("See Section~(sec:intro).", @"See Section~\ref{sec:intro}.")]
    [InlineData("Voir le théorème~(thm:pyth).", @"Voir le théorème~\ref{thm:pyth}.")]
    public void Kind_paren_label_becomes_a_reference_with_a_tie(string text, string expected)
    {
        var latex = Render(text);
        latex.Should().Contain(expected);
        latex.Should().NotContain("textasciitilde");
    }

    [Fact]
    public void At_ref_from_the_cross_references_panel_becomes_a_reference()
    {
        Render("As shown in @ref{fig:arch} and @ref{eq:loss}.")
            .Should().Contain(@"\ref{fig:arch}").And.Contain(@"\ref{eq:loss}").And.NotContain("@ref");
    }

    [Fact]
    public void At_cite_becomes_a_citation()
    {
        Render("Known since @cite{euclid} and @cite{knuth84, lamport94}.")
            .Should().Contain(@"\cite{euclid}").And.Contain(@"\cite{knuth84,lamport94}").And.NotContain("@cite");
    }

    [Theory]
    [InlineData("about~(3 items)", "about~(3 items)")]
    [InlineData("a~b", "a~b")]
    [InlineData(@"Theorem~\ref{thm:a}", @"Theorem~\ref{thm:a}")]
    [InlineData("Dr.~Smith", "Dr.~Smith")]
    public void A_tilde_between_two_characters_is_a_tie(string text, string expected)
    {
        // MCP-67: LaTeX's non-breaking space, as authors mean it. Only a prefixed label is a reference.
        Render(text).Should().Contain(expected).And.NotContain("textasciitilde");
    }

    [Theory]
    [InlineData("about ~5 minutes")]
    [InlineData("a ~ b")]
    public void A_lone_tilde_is_printed(string text) =>
        Render(text).Should().Contain(@"\textasciitilde{}");

    [Fact]
    public void Raw_latex_references_still_pass_through()
    {
        Render(@"See \ref{thm:a} and \eqref{eq:b}.").Should().Contain(@"\ref{thm:a}").And.Contain(@"\eqref{eq:b}");
    }

    [Fact]
    public void A_kind_paren_reference_to_a_missing_label_is_reported_and_a_present_one_is_not()
    {
        var theorem = Paragraph("", "theorem", new { text = "T.", label = "thm:a", theoremType = "theorem" });
        var uses = Paragraph("See Theorem~(thm:a) and Figure~(fig:missing).");
        var report = ReferenceIndex.Build(new[] { theorem, uses });
        report.Problems.Should().ContainSingle(p => p.Kind == "dangling" && p.Key == "fig:missing");
        report.Problems.Should().NotContain(p => p.Key == "thm:a");
    }
}
