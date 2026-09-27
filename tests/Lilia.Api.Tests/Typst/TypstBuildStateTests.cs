using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Xunit;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Typst;

/// <summary>
/// A build's per-thread state (whether the document has references, its staged
/// image paths) ends with the build. Thread-pool threads are reused, so state
/// left behind leaked into whatever rendered next on the same thread: found on
/// 27 Sep as TypstExportFixtureTests' "bibliography: no directive without
/// entries" failing only in a full run, after a test that built a document
/// with references.
/// </summary>
public class TypstBuildStateTests
{
    private static Block Bibliography() => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        Type = "bibliography",
        Content = JsonDocument.Parse("{}"),
        SortOrder = 0,
    };

    [Fact]
    public void A_document_with_references_leaves_nothing_for_the_next_render_on_the_thread()
    {
        var service = new TypstExportService();
        var doc = Doc();
        doc.BibliographyEntries =
        [
            new BibliographyEntry
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, CiteKey = "euclid", EntryType = "book",
                Data = JsonDocument.Parse(JsonSerializer.Serialize(new { author = "Euclid", title = "Elements", year = "300" })),
            },
        ];
        service.BuildTypstDocument(doc, [Bibliography()]).Should().Contain("#bibliography(", "this document has references");

        // Same thread, next render: a block on its own has no references to point at.
        service.RenderBlockForTest(Bibliography()).Should().NotContain("#bibliography(");
    }
}
