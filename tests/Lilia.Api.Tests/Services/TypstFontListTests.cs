using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The Typst preamble's font stack.
///
/// <para>Every generated document asked for <c>Linux Libertine</c>. Typst does
/// not bundle it and the Dockerfile does not install it — the Dockerfile
/// installs <c>fonts-liberation</c>, a metric clone of Arial and Times whose
/// name differs by three letters. So Typst reported
/// <c>warning: unknown font family: linux libertine</c>, silently substituted
/// the next entry, and <b>exited 0</b>. The PDF came out in New Computer Modern
/// while the code believed it was Libertine, and the source comment credited a
/// Dockerfile line that was never written.</para>
///
/// <para>Nothing caught it because a developer machine hides it: this one
/// offers Typst 202 system families, so a font production lacks still renders
/// locally. The container has four. That asymmetry is the whole reason these
/// assertions are about <i>which</i> names are emitted rather than about
/// whether a compile succeeds — the compile always succeeded.</para>
/// </summary>
public class TypstFontListTests
{
    // Measured with `typst fonts --ignore-system-fonts` on 0.15.0. The entire
    // list — anything else depends on the host.
    private static readonly string[] Bundled =
    [
        "Libertinus Serif", "New Computer Modern", "New Computer Modern Math", "DejaVu Sans Mono",
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("serif")]
    [InlineData("sans")]
    [InlineData("mono")]
    [InlineData("something-nobody-configured")]
    public void Every_list_ends_somewhere_typst_carries_with_it(string? family)
    {
        // The load-bearing property. If the last entry is a host font, the
        // document renders in whatever Typst picks instead — with a warning
        // nobody reads and an exit code of 0.
        var list = TypstExportService.FontListForTest(family);

        TypstExportService.EndsInABundledFont(list)
            .Should().BeTrue($"'{list}' must end at a font the binary bundles");
    }

    [Fact]
    public void The_font_that_was_never_installed_is_gone()
    {
        foreach (var family in new string?[] { null, "serif", "sans", "mono" })
        {
            TypstExportService.FontListForTest(family)
                .Should().NotContain("Linux Libertine");
        }
    }

    [Fact]
    public void Serif_asks_for_the_typeface_that_was_actually_intended()
    {
        // Libertinus Serif is the maintained successor to Linux Libertine, and
        // Typst bundles it — so the intended typeface is now the one used,
        // rather than a fallback that happened to resolve.
        TypstExportService.FontListForTest(null).Should().Contain("Libertinus Serif");
        TypstExportService.FontListForTest("serif").Should().Contain("Libertinus Serif");
    }

    [Fact]
    public void Asking_for_sans_no_longer_returns_a_serif()
    {
        // "sans" mapped to Linux Libertine, a serif. Independently of whether
        // the font existed, the answer was the wrong shape of typeface.
        var list = TypstExportService.FontListForTest("sans");

        list.Should().StartWith("\"DejaVu Sans\"");
        list.Should().NotStartWith("\"Libertinus");
    }

    [Fact]
    public void Mono_leads_with_a_monospace_font()
    {
        TypstExportService.FontListForTest("mono").Should().StartWith("\"DejaVu Sans Mono\"");
    }

    [Fact]
    public void No_list_names_a_font_that_exists_nowhere()
    {
        // "New Computer Modern Mono" was in the monospace fallback chain and is
        // not bundled either — the same mistake, one line down, unnoticed
        // because the entry after it happened to resolve.
        foreach (var family in new string?[] { null, "serif", "sans", "mono" })
        {
            TypstExportService.FontListForTest(family)
                .Should().NotContain("New Computer Modern Mono");
        }
    }

    [Fact]
    public void A_list_ending_at_a_host_font_is_reported_as_unsafe()
    {
        // Guards the guard: if EndsInABundledFont said yes to everything, the
        // test above would pass while proving nothing.
        TypstExportService.EndsInABundledFont("\"Arial\", \"Helvetica\"").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("serif")]
    [InlineData("sans")]
    [InlineData("mono")]
    public void Entries_are_quoted_and_not_repeated(string? family)
    {
        var list = TypstExportService.FontListForTest(family);
        var entries = list.Split(',').Select(e => e.Trim()).ToList();

        entries.Should().OnlyContain(e => e.StartsWith('"') && e.EndsWith('"'));
        entries.Should().OnlyHaveUniqueItems("a repeated fallback is dead weight in every document");
    }

    [Fact]
    public void Bundled_list_is_the_measured_one()
    {
        // If a Typst upgrade changes what ships in the binary, this is the
        // assertion that should fail first, rather than a document quietly
        // changing typeface in production.
        foreach (var font in Bundled)
        {
            TypstExportService.EndsInABundledFont($"\"{font}\"")
                .Should().BeTrue($"{font} is bundled with typst 0.15.0");
        }
    }
}
