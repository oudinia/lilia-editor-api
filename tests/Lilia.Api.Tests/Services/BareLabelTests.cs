using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP-106 (run 3b, LTX-110): an equation labelled "eqnocolon" is written \label{eq:eqnocolon}, so the
/// author's \eqref{eqnocolon} printed "??", and Equation~(eqnocolon) was left as typed.
/// </summary>
public class BareLabelTests
{
    private static Block B(string type, object content, int order) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type, SortOrder = order,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Theory]
    [InlineData("See Equation~(loss).", @"\eqref{loss}")]
    [InlineData("By Theorem~(main), done.", @"\ref{main}")]
    [InlineData("In Fig.~(arch) we see", @"\ref{arch}")]
    public void A_kind_word_with_a_bare_label_is_a_reference(string text, string latex) =>
        CrossRefSyntax.Resolve(text, p => p).Should().Contain(latex);

    [Fact]
    public void Other_words_stay_text() =>
        CrossRefSyntax.Resolve("approx~(three) items", p => p).Should().Be("approx~(three) items");

    [Fact]
    public void A_bare_reference_points_at_the_one_prefixed_label() =>
        CrossRefSyntax.ResolveBareReferences(@"\label{eq:loss} see \eqref{loss} and \ref{fig:a,loss} and \ref{other}")
            .Should().Be(@"\label{eq:loss} see \eqref{eq:loss} and \ref{fig:a,eq:loss} and \ref{other}");

    [Fact]
    public void The_document_resolves_both_forms_and_the_check_finds_nothing_dangling()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article",
            BibliographyEntries = new List<BibliographyEntry>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Blocks = new List<Block>
            {
                B("equation", new { latex = "E = mc^2", equationMode = "display", label = "eqnocolon" }, 0),
                B("paragraph", new { text = @"By \eqref{eqnocolon} and Equation~(eqnocolon)." }, 1),
            },
        };
        foreach (var b in doc.Blocks) b.Document = doc;
        var tex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        tex.Should().Contain(@"\label{eq:eqnocolon}").And.Contain(@"\eqref{eq:eqnocolon}").And.NotContain("Equation~(eqnocolon)");
        new EmittedLatexAudit(tex).DanglingReferences.Should().BeEmpty();
    }
}
