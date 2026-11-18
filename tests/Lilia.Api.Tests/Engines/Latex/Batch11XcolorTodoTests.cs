using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's conference-paper import (18 Nov 2026, #204): a preserved todonotes loaded xcolor before Lilia's own
/// xcolor with its options, so the compile stopped ("Option clash for package xcolor"), and a \todo note printed in
/// the body.
/// </summary>
public class Batch11XcolorTodoTests
{
    private static Document Doc(string packages) => new()
    {
        Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 10,
        LatexDocumentClass = "article", LatexPackages = packages, BibliographyEntries = new List<BibliographyEntry>(),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        Blocks = [new Block { Id = Guid.NewGuid(), Type = "paragraph", Content = JsonDocument.Parse("""{"text":"Body."}""") }],
    };

    [Fact]
    public void Xcolor_options_come_before_a_preserved_package_that_loads_xcolor()
    {
        var tex = new RenderService(null!, NullLogger<RenderService>.Instance)
            .RenderToLatex(Doc(JsonSerializer.Serialize(new[] { new { name = "todonotes" } })));
        var pass = tex.IndexOf(@"\PassOptionsToPackage{dvipsnames,svgnames,table}{xcolor}", StringComparison.Ordinal);
        var todonotes = tex.IndexOf(@"\IfFileExists{todonotes.sty}", StringComparison.Ordinal);
        pass.Should().BeGreaterThan(0);
        todonotes.Should().BeGreaterThan(pass, "the options must be known before todonotes loads xcolor");
    }

    [Theory]
    [InlineData(@"Body text.\todo{a note} More.", "Body text. More.")]
    [InlineData(@"Body text.\todo[inline,color=red]{fix {this}} More.", "Body text. More.")]
    [InlineData(@"\listoftodos Body. \missingfigure{plot here}", " Body. ")]
    public void Review_notes_are_dropped_on_import(string source, string expected)
        => LatexParser.StripReviewNotes(source).Should().Be(expected);

    [Fact]
    public void A_paper_that_defines_todo_keeps_it()
    {
        const string source = @"\newcommand{\todo}[1]{[TODO: #1]} Body.\todo{add the data}";
        LatexParser.StripReviewNotes(source).Should().Be(source);
    }
}
