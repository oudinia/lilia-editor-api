using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The skip-list is now derived from the preamble instead of hand-maintained.
///
/// <para>This is the regression guard for that change. Derivation can only see
/// packages the preamble constants actually mention, so anything that was on
/// the old hand-written list for a different reason — emitted conditionally by
/// the builder, or refused without being loaded — has to be accounted for
/// explicitly. Losing one is invisible until an imported document happens to
/// name it and aborts with "Option clash for package".</para>
/// </summary>
public class PreamblePackageDerivationTests
{
    /// <summary>
    /// Every name the hand-written list carried before the refactor, verbatim.
    /// Nothing may drop off it silently.
    /// </summary>
    private static readonly string[] PreviouslySkipped =
    [
        "inputenc", "fontenc", "textcomp", "lmodern",
        "amsmath", "amssymb", "amsfonts", "amsthm", "mathtools", "mathrsfs", "cancel", "siunitx", "bm",
        "microtype", "setspace", "parskip",
        "graphicx", "float", "caption", "subcaption", "xcolor",
        "booktabs", "multirow", "tabularx", "longtable", "array",
        "enumitem", "listings",
        "algorithm", "algorithmic",
        "tcolorbox", "hyperref", "cleveref", "csquotes",
        "geometry", "babel",
        "newtxtext", "newtxmath", "mathptmx", "txfonts", "pxfonts",
        "mathpazo", "fourier", "libertine", "palatino", "utopia",
        "charter", "cmbright", "kpfonts", "eulervm",
        "wasysym", "mathabx", "stix", "stix2", "times",
        "fontspec", "unicode-math", "polyglossia",
        "newspaper", "yfonts",
    ];

    /// <summary>
    /// Two entries the old list carried that were <b>wrong</b>, and are now
    /// deliberately allowed through.
    /// </summary>
    /// <remarks>
    /// <para>Both were filed under "loaded by our default preamble". Neither is
    /// loaded — the preamble says so in as many words:</para>
    ///
    /// <para><i>"csquotes is NOT bundled… Since we don't bundle babel either,
    /// users who need csquotes must load both in the correct order
    /// themselves."</i></para>
    ///
    /// <para>So the import path was dropping the very packages the comment tells
    /// users to bring, and doing it silently. A French document importing
    /// <c>babel</c> lost its hyphenation, quote style and date formats with
    /// nothing said — the failure shape this whole plan is about, produced by a
    /// hand-maintained list drifting from what the code does.</para>
    ///
    /// <para>Verified safe by compiling: our preamble plus
    /// <c>\usepackage[french]{babel}</c> and <c>\usepackage{csquotes}</c>
    /// produces a PDF.</para>
    /// </remarks>
    private static readonly string[] WronglySkippedBefore = ["babel", "csquotes"];

    [Fact]
    public void Nothing_the_old_list_refused_is_now_allowed_through()
    {
        // The whole risk of the refactor in one assertion — minus the two the
        // refactor proved should never have been on the list.
        var missing = PreviouslySkipped
            .Except(WronglySkippedBefore, StringComparer.OrdinalIgnoreCase)
            .Where(p => !LaTeXExportService.IsDefaultPreamblePackageForTest(p))
            .ToList();

        missing.Should().BeEmpty(
            "these were skipped before and an imported \\usepackage for any of them would now clash");
    }

    [Fact]
    public void Babel_and_csquotes_reach_the_document_again()
    {
        // The preamble tells users to load these themselves; the import path
        // was removing them. Allowing them through is the fix, not a
        // regression, so it is asserted rather than merely permitted.
        foreach (var package in WronglySkippedBefore)
        {
            LaTeXExportService.IsDefaultPreamblePackageForTest(package)
                .Should().BeFalse($"we do not load {package}, so an imported one must survive");
        }
    }

    [Fact]
    public void Geometry_is_still_refused_although_it_is_not_in_a_constant()
    {
        // Emitted conditionally by the builder when margins are set, so
        // derivation cannot see it — and a document that sets margins and
        // imports geometry would get it twice.
        LaTeXExportService.IsDefaultPreamblePackageForTest("geometry").Should().BeTrue();
    }

    // ── The derivation itself ─────────────────────────────────────────

    [Theory]
    [InlineData("amsmath")]   // one of several names in a single \usepackage
    [InlineData("amsthm")]    // the last name in that same group
    [InlineData("xcolor")]    // carries bracketed options
    [InlineData("bm")]        // added most recently
    [InlineData("hyperref")]
    public void Loaded_packages_are_read_out_of_the_preamble(string package)
    {
        LaTeXPreamble.LoadedPackageNames.Should().Contain(package);
    }

    [Fact]
    public void Options_are_not_mistaken_for_package_names()
    {
        // \usepackage[dvipsnames,svgnames,table]{xcolor} must yield xcolor and
        // nothing else. Reading the options as names would add three packages
        // that do not exist, and skip an imported \usepackage{table}.
        LaTeXPreamble.LoadedPackageNames.Should().NotContain("dvipsnames");
        LaTeXPreamble.LoadedPackageNames.Should().NotContain("svgnames");
        LaTeXPreamble.LoadedPackageNames.Should().NotContain("utf8");
        LaTeXPreamble.LoadedPackageNames.Should().NotContain("T1");
    }

    [Fact]
    public void Fontspec_is_included_although_only_the_unicode_engines_load_it()
    {
        // It comes from EngineAddendum rather than the constants, so a
        // derivation reading only Packages would miss it.
        LaTeXPreamble.LoadedPackageNames.Should().Contain("fontspec");
    }

    [Fact]
    public void The_derived_set_is_not_absurdly_large_or_empty()
    {
        // Guards the parser rather than the list: a regex that matched nothing
        // would make every assertion above fail loudly, but one that matched
        // too much could quietly swallow imported packages.
        LaTeXPreamble.LoadedPackageNames.Count.Should().BeInRange(25, 60);
    }

    [Fact]
    public void A_package_we_neither_load_nor_refuse_passes_through()
    {
        // The point of the skip-list is to be narrow. If it said yes to
        // everything, imported packages would be silently dropped and
        // documents would render missing whatever they asked for.
        LaTeXExportService.IsDefaultPreamblePackageForTest("tikz-cd").Should().BeFalse();
        LaTeXExportService.IsDefaultPreamblePackageForTest("mhchem").Should().BeFalse();
    }

    // ── Export and validation must agree ──────────────────────────────

    [Fact]
    public void Every_package_loaded_for_export_is_loaded_for_validation()
    {
        // Drift here means a block validates differently from how it exports.
        // The worse direction is validating as broken while exporting fine,
        // which tells an author their document is wrong when it is not.
        var exportOnly = PackageNamesIn(LaTeXPreamble.Packages)
            .Except(PackageNamesIn(LaTeXPreamble.ValidationPackages), StringComparer.OrdinalIgnoreCase)
            .ToList();

        exportOnly.Should().BeEmpty("validation compiles blocks with its own preamble and must not lag behind");
    }

    private static IEnumerable<string> PackageNamesIn(string preamble) =>
        preamble.Split('\n')
            .Where(line => !line.TrimStart().StartsWith('%'))
            .SelectMany(line => System.Text.RegularExpressions.Regex
                .Matches(line, @"\\usepackage\s*(?:\[[^\]]*\])?\s*\{([^}]*)\}")
                .Select(m => m.Groups[1].Value))
            .SelectMany(names => names.Split(','))
            .Select(n => n.Trim())
            .Where(n => n.Length > 0);
}
