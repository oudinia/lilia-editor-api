using FluentAssertions;
using Lilia.Engines.Publishers;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #244 retest (19 Nov 2026): example-image-a now prints from TeX Live, but the publisher checklist still
/// said "A figure has no image: it prints as an empty frame" for it.
/// </summary>
public class Batch30TexLiveCheckTests
{
    [Fact]
    public void A_figure_with_a_tex_live_image_has_an_image()
    {
        var doc = Doc("elsarticle");
        var blocks = doc.Blocks.ToList();
        blocks.Add(B("figure", new { src = "example-image-a", caption = "An example." }));
        blocks.Add(B("figure", new { src = "", caption = "Empty." }));
        var findings = PublisherCompliance.Check(doc, blocks, "").Findings.Where(f => f.Code == "figure_missing").ToList();
        findings.Should().ContainSingle("only the empty figure has no image");
    }
}
