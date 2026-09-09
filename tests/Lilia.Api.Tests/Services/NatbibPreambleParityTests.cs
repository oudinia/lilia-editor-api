using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Export and validation must agree about natbib.
///
/// <para>They did not. The exporter loaded natbib when it saw <c>\citep</c>;
/// <see cref="LaTeXPreamble.Packages"/> — used by validation and preview —
/// never mentioned it. On 2026-09-08 one document exported a valid 94 KB PDF
/// and failed validation with <c>! Undefined control sequence</c> at the same
/// moment, on the same content. The author was told their document was broken,
/// pasted the error into Ask Lilia, and had its <c>\citep</c>/<c>\citet</c>
/// citations rewritten to plain "(see references)" text to make the false
/// alarm go away.</para>
///
/// <para>These tests pin the rule itself rather than either copy of it.</para>
/// </summary>
public class NatbibPreambleParityTests
{
    [Theory]
    [InlineData(@"Pythagoras \citep{euclid} proved it.")]
    [InlineData(@"As \citet{euclid} shows, …")]
    [InlineData(@"\citeauthor{euclid} disagrees.")]
    [InlineData(@"\citeyear{euclid}")]
    [InlineData(@"\citep*{euclid}")]
    public void NatbibCommandsAreDetected(string latex) =>
        LaTeXPreamble.UsesNatbib(new[] { latex }).Should().BeTrue();

    [Theory]
    [InlineData(@"Pythagoras \cite{euclid} proved it.")]
    [InlineData("No citations here at all.")]
    [InlineData("")]
    [InlineData(null)]
    public void PlainCiteAndProseDoNot(string? latex) =>
        LaTeXPreamble.UsesNatbib(new[] { latex }).Should().BeFalse(
            "loading natbib for a legacy \\cite-only document silently turns its " +
            "numeric bibliography into author-year");

    [Fact]
    public void ItLooksAcrossEveryBlock() =>
        LaTeXPreamble.UsesNatbib(new[] { "An abstract.", null, @"…\citet{euclid}…" })
            .Should().BeTrue();

    [Fact]
    public void TheNatbibLineActuallyLoadsNatbib() =>
        LaTeXPreamble.Natbib.Should().Contain(@"\usepackage{natbib}");

    /// <summary>
    /// The regression itself: the shared preamble does not carry natbib, which
    /// is exactly why the conditional has to exist and be applied by everyone
    /// who builds a preamble.
    /// </summary>
    [Fact]
    public void TheSharedPackagesBlockStillDoesNotCarryNatbib()
    {
        LaTeXPreamble.Packages.Should().NotContain("natbib");
        LaTeXPreamble.ValidationPackages.Should().NotContain("natbib");
    }
}
