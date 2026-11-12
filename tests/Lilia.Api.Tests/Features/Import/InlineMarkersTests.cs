using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>Launch-bot #76 (12 Nov 2026): Word's bold, italic and underline reach the block text as the editor's marks.</summary>
public class InlineMarkersTests
{
    private static FormattingSpan S(int start, int length, FormattingType type) => new() { Start = start, Length = length, Type = type };

    [Fact]
    public void Bold_becomes_double_stars()
    {
        const string text = "This note explains the base tariff and its adjustments.";
        InlineMarkers.Apply(text, [S(text.IndexOf("base tariff"), 11, FormattingType.Bold)])
            .Should().Be("This note explains the **base tariff** and its adjustments.");
    }

    [Fact]
    public void A_mark_never_sits_on_a_space_and_split_runs_join()
    {
        // Word often splits one bold phrase into runs, the first ending with its space.
        InlineMarkers.Apply("a base tariff b", [S(2, 5, FormattingType.Bold), S(7, 6, FormattingType.Bold)])
            .Should().Be("a **base tariff** b");
    }

    [Fact]
    public void Nested_marks_close_inside_out()
    {
        InlineMarkers.Apply("very important", [S(0, 14, FormattingType.Bold), S(5, 9, FormattingType.Italic)])
            .Should().Be("**very *important***");
    }

    [Fact]
    public void Other_spans_and_no_spans_leave_the_text_alone()
    {
        InlineMarkers.Apply("H2O", [S(1, 1, FormattingType.Subscript)]).Should().Be("H2O");
        InlineMarkers.Apply("plain", []).Should().Be("plain");
    }
}
