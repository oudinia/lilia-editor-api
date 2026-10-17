using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// The LaTeX export page's Document Class: <c>?classOverride=</c> chooses the class for that export
/// over the document's own. <c>documentClass</c> was the only parameter and it is a fallback for a
/// document with no class, so on any document that has one (every new document) the select did
/// nothing (e2e export spec, 15 Oct 2026).
/// </summary>
public class ClassOverrideTests
{
    private static readonly LaTeXExportService Export = new(null!, null!, NullLogger<LaTeXExportService>.Instance);

    private static Document Doc(string? cls) => new()
    {
        Id = Guid.NewGuid(), Title = "Coastal Erosion", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls,
    };

    private static List<Block> Blocks() =>
    [
        new() { Id = Guid.NewGuid(), Type = "heading", SortOrder = 0, Content = JsonDocument.Parse("""{"text":"Results","level":1}""") },
        new() { Id = Guid.NewGuid(), Type = "paragraph", SortOrder = 1, Content = JsonDocument.Parse("""{"text":"Waves wear the cliffs back."}""") },
    ];

    private static string Tex(Document doc, LaTeXExportOptions options) => Export.BuildSingleFileLatex(doc, Blocks(), [], options);

    [Fact]
    public void Without_an_override_the_document_keeps_its_class()
    {
        Tex(Doc("article"), new LaTeXExportOptions { DocumentClass = "report" })
            .Should().MatchRegex(@"\\documentclass(\[[^\]]*\])?\{article\}");
    }

    [Fact]
    public void The_override_wins_over_the_document_class()
    {
        var tex = Tex(Doc("article"), new LaTeXExportOptions { ClassOverride = "report" });
        tex.Should().MatchRegex(@"\\documentclass(\[[^\]]*\])?\{report\}");
        tex.Should().NotMatchRegex(@"\\documentclass(\[[^\]]*\])?\{article\}");
    }

    [Fact]
    public void A_book_maps_top_level_headings_to_chapters()
    {
        Tex(Doc("article"), new LaTeXExportOptions { ClassOverride = "book" }).Should().Contain("\\chapter{Results}");
        Tex(Doc("article"), new LaTeXExportOptions()).Should().Contain("\\section{Results}");
    }

    [Fact]
    public void A_class_Lilia_does_not_ship_is_ignored()
    {
        Tex(Doc("article"), new LaTeXExportOptions { ClassOverride = "not-a-class" })
            .Should().MatchRegex(@"\\documentclass(\[[^\]]*\])?\{article\}");
    }
}
