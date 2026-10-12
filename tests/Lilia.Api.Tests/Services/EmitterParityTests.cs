using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The check emitter (RenderService: get_lilia_latex, check_document, preview) and the export
/// emitter (LaTeXExportService: .tex, zip, LaTeX PDF) must print the same content. MCP-09 and
/// MCP-39 (cloud session's handoff, 12 Oct) were found reading the code: commands typed in prose
/// printed literally in an export, and a second abstract vanished from it.
/// </summary>
public class EmitterParityTests
{
    private readonly RenderService _render = new(null!, NullLogger<RenderService>.Instance);

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Document Doc(params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article",
            Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList(),
            BibliographyEntries = new List<BibliographyEntry>(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        foreach (var b in doc.Blocks) b.Document = doc;
        return doc;
    }

    private static string Export(Document doc) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions());

    // ── MCP-09 ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData(@"An \emph{important} point.", @"\emph{important}")]
    [InlineData(@"Bold \textbf{word} here.", @"\textbf{word}")]
    [InlineData(@"Water is \ce{H2O}.", @"\ce{H2O}")]
    [InlineData(@"Speed \SI{3}{\metre\per\second}.", @"\SI{3}{\metre\per\second}")]
    [InlineData(@"Italic \textit{x} and \underline{y}.", @"\textit{x}")]
    public void A_command_typed_in_prose_prints_the_same_in_both(string text, string expected)
    {
        var doc = Doc(B("paragraph", new { text }));
        _render.RenderToLatex(doc).Should().Contain(expected);
        Export(doc).Should().Contain(expected);
    }

    // ── MCP-39 ──────────────────────────────────────────────────────────
    [Fact]
    public void Every_abstract_is_exported()
    {
        var doc = Doc(B("abstract", new { text = "First abstract." }), B("paragraph", new { text = "Body." }), B("abstract", new { text = "Zweite Zusammenfassung." }));
        _render.RenderToLatex(doc).Should().Contain("First abstract.").And.Contain("Zweite Zusammenfassung.");
        Export(doc).Should().Contain("First abstract.").And.Contain("Zweite Zusammenfassung.");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_export_of_typed_latex_compiles()
    {
        var doc = Doc(
            B("abstract", new { text = "First abstract." }),
            B("paragraph", new { text = @"An \emph{important} point, \textbf{bold}, \SI{3}{\metre\per\second} and \qty{5}{\kilo\gram}; 50\% done." }),
            B("abstract", new { text = "Second abstract." }));
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfAsync(Export(doc), "pdflatex", timeout: 180);
        pdf.Should().NotBeEmpty();
    }
}
