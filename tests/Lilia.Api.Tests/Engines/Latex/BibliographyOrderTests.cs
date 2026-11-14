using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The check's references in the PDF's order (MCP-119, retested 14 Nov): cited Zygmund then Fejér, the
/// check listed Zygmund [1] and the PDF (BibTeX plain, sorted by author) Fejér [1].
/// </summary>
public class BibliographyOrderTests
{
    private static BibliographyEntry E(string key, string author, string year, string title = "T") => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), CiteKey = key, EntryType = "article",
        Data = JsonDocument.Parse(JsonSerializer.Serialize(new { author, year, title })),
    };

    [Fact]
    public void Sorting_styles_order_by_author_as_bibtex_does()
    {
        var sorted = BibliographyOrder.Sorted([
            E("zygmund1959", "Zygmund, Antoni", "1959"),
            E("fejer1904", "Fej\\'er, Lip\\'ot", "1904"),
            E("conneau2020", "Alexis Conneau and Kartikay Khandelwal", "2020"),
            E("houlsby2019", "Neil Houlsby and Andrei Giurgiu", "2019"),
        ]);
        sorted.Select(e => e.CiteKey).Should().Equal("conneau2020", "fejer1904", "houlsby2019", "zygmund1959");
    }

    [Fact]
    public void Same_author_goes_by_year_then_title()
    {
        BibliographyOrder.Sorted([E("b", "Knuth, Donald", "1984", "The TeXbook"), E("a", "Knuth, Donald", "1968", "Fundamental Algorithms")])
            .Select(e => e.CiteKey).Should().Equal("a", "b");
    }

    [Theory]
    [InlineData("unsrt", true)]
    [InlineData("IEEEtran", true)]
    [InlineData("plain", false)]
    [InlineData(null, false)]
    public void Citation_order_styles_are_left_alone(string? style, bool keeps) =>
        BibliographyOrder.KeepsCitationOrder(style).Should().Be(keeps);
}
