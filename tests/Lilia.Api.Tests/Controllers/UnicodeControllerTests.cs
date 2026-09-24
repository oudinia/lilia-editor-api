using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Lilia.Api.Tests.Controllers;

/// <summary>
/// POST /api/unicode/check — what a paste will do when it meets pdflatex.
///
/// <para>The classification itself is the service's, tested on real Postgres in
/// UnicodeShimBuildShimTests. What is pinned here is what the endpoint adds:
/// counting, and refusing to call a text clean when it could not check it.</para>
/// </summary>
public class UnicodeControllerTests
{
    private readonly Mock<IUnicodeShimService> _shim = new();
    private readonly UnicodeController _sut;

    public UnicodeControllerTests()
    {
        _sut = new UnicodeController(_shim.Object);
        _shim.Setup(s => s.MappedCount).Returns(165);
        _shim.Setup(s => s.Classify(It.IsAny<string>())).Returns(new UnicodeClassification(
            new SortedDictionary<int, string> { [0x03B3] = @"\ensuremath{\gamma}" },
            [0x2135]));
    }

    private static UnicodeCheckResponse Body(IActionResult r) =>
        (UnicodeCheckResponse)((OkObjectResult)r).Value!;

    [Fact]
    public void Separates_what_fails_from_what_the_shim_rescues()
    {
        var body = Body(_sut.Check(new UnicodeCheckRequest("γ and ℵ")));

        body.Checked.Should().BeTrue();
        body.WontCompile.Should().ContainSingle(c => c.Char == "ℵ" && c.Replacement == null);
        body.Shimmed.Should().ContainSingle(c => c.Char == "γ" && c.Replacement == @"\ensuremath{\gamma}");
    }

    [Fact]
    public void Counts_occurrences_not_kinds()
    {
        // The chip's copy is "2 pasted characters won't compile" — the author
        // pasted two characters, not one kind twice.
        var body = Body(_sut.Check(new UnicodeCheckRequest("ℵ, then ℵ again, and γ")));

        body.WontCompile.Single().Count.Should().Be(2);
        body.Shimmed.Single().Count.Should().Be(1);
    }

    [Fact]
    public void A_surrogate_pair_counts_once()
    {
        _shim.Setup(s => s.Classify(It.IsAny<string>())).Returns(new UnicodeClassification(
            new SortedDictionary<int, string>(), [0x1F642]));

        var body = Body(_sut.Check(new UnicodeCheckRequest("🙂")));

        body.WontCompile.Single().Count.Should().Be(1);
        body.WontCompile.Single().Char.Should().Be("🙂");
    }

    [Fact]
    public void An_unloaded_catalog_is_unchecked_not_clean()
    {
        // With no map the service cannot tell mapped from unmapped and reports
        // nothing. Returning checked:true with two empty lists would tell the
        // editor a paste of ℵ is fine.
        _shim.Setup(s => s.MappedCount).Returns(0);

        var body = Body(_sut.Check(new UnicodeCheckRequest("ℵ")));

        body.Checked.Should().BeFalse();
        _shim.Verify(s => s.Classify(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Refuses_something_too_large_to_be_a_paste()
    {
        var huge = new string('x', UnicodeController.MaxTextLength + 1);

        _sut.Check(new UnicodeCheckRequest(huge)).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Plain_text_is_checked_and_clean()
    {
        _shim.Setup(s => s.Classify(It.IsAny<string>())).Returns(new UnicodeClassification(
            new SortedDictionary<int, string>(), []));

        var body = Body(_sut.Check(new UnicodeCheckRequest("Nothing unusual.")));

        body.Checked.Should().BeTrue();
        body.WontCompile.Should().BeEmpty();
        body.Shimmed.Should().BeEmpty();
    }
}
