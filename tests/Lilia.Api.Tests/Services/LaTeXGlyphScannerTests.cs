using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Fixtures are REAL log excerpts from LuaHBTeX 1.24.0, not hand-written
/// samples. Hand-written ones would have omitted the line wrapping that breaks
/// the naive parser, which is exactly the bug worth covering.
/// </summary>
public class LaTeXGlyphScannerTests
{
    // Verbatim from a compile of "Hello. 中文测试。" with no CJK font declared.
    // Note the wrap: TeX splits at ~79 BYTES mid-token, so the font name
    // continues on the next physical line with no continuation marker.
    private const string RealCjkLog = """
        Package fontspec Info: Font family 'LatinModernRoman(0)' created.
        Missing character: There is no 中 (U+4E2D) in font "name:Latin Modern Roman:mod
        e=node;script=latn;language=dflt;+tlig;"!
        Missing character: There is no 文 (U+6587) in font "name:Latin Modern Roman:mod
        e=node;script=latn;language=dflt;+tlig;"!
        Missing character: There is no 中 (U+4E2D) in font "name:Latin Modern Roman:mod
        e=node;script=latn;language=dflt;+tlig;"!
        Output written on document.pdf (1 page, 3400 bytes).
        """;

    [Fact]
    public void Finds_glyphs_dropped_by_a_compile_that_exited_zero()
    {
        var dropped = LaTeXGlyphScanner.Scan(RealCjkLog);

        dropped.Should().HaveCount(2, "the log lists three occurrences of two distinct characters");
        dropped.Select(g => g.CodePoint).Should().Equal("4E2D", "6587");
        dropped.Select(g => g.Character).Should().Equal("中", "文");
    }

    [Fact]
    public void Recovers_the_font_name_split_across_TeX_line_wrapping()
    {
        // The trap: TeX hard-wraps at ~79 bytes mid-token, so a pattern that
        // expects the closing quote matches NOTHING on a real log while passing
        // happily on any sample written by hand.
        RealCjkLog.Should().NotContain("Latin Modern Roman:mode=node",
            "the raw log really is wrapped — otherwise this test proves nothing");

        var font = LaTeXGlyphScanner.Scan(RealCjkLog)[0].Font;

        font.Should().NotBeNull();
        font.Should().Contain("Latin Modern Roman");
        font.Should().Contain("mode=node", "the name is only complete once the wrap is rejoined");
    }

    [Fact]
    public void Returns_nothing_when_the_font_covers_every_character()
    {
        LaTeXGlyphScanner.Scan("""
            This is LuaHBTeX, Version 1.24.0
            Output written on document.pdf (1 page, 90441 bytes).
            """).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Handles_a_missing_log_without_throwing(string? log) =>
        LaTeXGlyphScanner.Scan(log).Should().BeEmpty();

    [Fact]
    public void Describe_names_the_characters_and_the_font_at_fault()
    {
        var text = LaTeXGlyphScanner.Describe(LaTeXGlyphScanner.Scan(RealCjkLog));

        text.Should().NotBeNull();
        text.Should().Contain("2 characters were dropped");
        text.Should().Contain("中");
        text.Should().Contain("Latin Modern Roman");
    }

    [Fact]
    public void Describe_is_silent_when_nothing_was_dropped() =>
        LaTeXGlyphScanner.Describe([]).Should().BeNull();

    /// <summary>
    /// The reason this is scanned separately rather than folded into the
    /// existing warning filter: TeX emits one line per occurrence, so a real
    /// paragraph produces hundreds and a Take(10) would fill entirely with them.
    /// </summary>
    [Fact]
    public void Collapses_repeated_occurrences_of_the_same_character()
    {
        var manyRepeats = string.Concat(Enumerable.Repeat(
            "Missing character: There is no 中 (U+4E2D) in font \"X\"!\n", 300));

        LaTeXGlyphScanner.Scan(manyRepeats).Should().ContainSingle();
    }
}
