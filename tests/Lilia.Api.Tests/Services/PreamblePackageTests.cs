using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Which packages the emitted preamble loads.
///
/// <para>Adding one is cheap to do and easy to get wrong in two ways that no
/// compile catches: loading it in export but not in validation, so a block
/// validates differently from how it exports; and forgetting to declare it as
/// already-loaded, so an imported document that asks for it triggers
/// "Option clash for package".</para>
///
/// <para>The packages themselves were chosen by measurement — see the note on
/// <see cref="LaTeXPreamble"/> — and these tests pin the wiring, not the
/// choice.</para>
/// </summary>
public class PreamblePackageTests
{
    [Fact]
    public void Bm_is_loaded_for_export()
    {
        // \bm has the widest corpus reach (30 TeX.SE posts) of the commands our
        // preamble could not compile, and adding it broke none of the 374
        // corpus samples.
        LaTeXPreamble.Packages.Should().Contain(@"\usepackage{bm}");
    }

    [Fact]
    public void Bm_is_loaded_for_validation_too()
    {
        // The trap: validation compiles with its own package list. If the two
        // drift, a block validates and then fails on export — or worse,
        // validates as broken while exporting fine, and the author is told
        // their document is wrong when it is not.
        LaTeXPreamble.ValidationPackages.Should().Contain(@"\usepackage{bm}");
    }

    [Fact]
    public void Physics_is_not_loaded()
    {
        // Deliberate. It recovers \ket, \abs and \norm, and redefines \qty,
        // which collides with siunitx. Measured: two samples newly passing,
        // two previously-working samples newly broken. A net-zero trade that
        // damages unit-heavy documents.
        LaTeXPreamble.Packages.Should().NotContain(@"\usepackage{physics}");
        LaTeXPreamble.ValidationPackages.Should().NotContain(@"\usepackage{physics}");
    }

    [Fact]
    public void Siunitx_still_owns_qty()
    {
        // The reason physics is refused. If siunitx ever leaves the preamble
        // this test should be revisited rather than deleted — the conflict is
        // between the two, not a property of physics alone.
        LaTeXPreamble.Packages.Should().Contain(@"\usepackage{siunitx}");
    }

    [Theory]
    [InlineData("amsmath")]
    [InlineData("mathtools")]
    [InlineData("siunitx")]
    [InlineData("bm")]
    public void Every_package_we_load_is_declared_as_already_loaded(string package)
    {
        // An imported document that asks for a package we already load must be
        // skipped, or LaTeX aborts with "Option clash for package X". The list
        // exists for that, and it is the half most easily forgotten when adding
        // a package: export keeps working locally and breaks only on documents
        // that happen to import the same one.
        LaTeXExportService.IsDefaultPreamblePackageForTest(package)
            .Should().BeTrue($"{package} is in the preamble, so an imported \\usepackage{{{package}}} must be skipped");
    }

    [Fact]
    public void A_package_we_do_not_load_is_not_skipped()
    {
        // Guards the guard: if the check said yes to everything, the test above
        // would pass while proving nothing, and imported packages would be
        // silently dropped.
        LaTeXExportService.IsDefaultPreamblePackageForTest("tikz-cd").Should().BeFalse();
    }
}
