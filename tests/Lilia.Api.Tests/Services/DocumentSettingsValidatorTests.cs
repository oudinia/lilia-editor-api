using FluentAssertions;
using Lilia.Api.Services;
using Xunit;
using I = Lilia.Api.Services.DocumentSettingsValidator.Input;

namespace Lilia.Api.Tests.Services;

public class DocumentSettingsValidatorTests
{
    private static DocumentSettingsValidator.Result V(I i) => DocumentSettingsValidator.Validate(i);

    [Fact]
    public void Only_the_supplied_fields_reach_the_update()
    {
        var r = V(new I(PaperSize: "A4", MarginLeft: "2.5CM", Columns: 2));
        r.Ok.Should().BeTrue();
        r.Fields.Should().BeEquivalentTo(new[] { "paperSize", "marginLeft", "columns" });
        r.Update!.PaperSize.Should().Be("a4");
        r.Update.MarginLeft.Should().Be("2.5cm");
        r.Update.Columns.Should().Be(2);
        r.Update.MarginRight.Should().BeNull();
        r.Update.FontSize.Should().BeNull();
        r.Update.HeaderLeft.Should().BeNull();
    }

    [Fact]
    public void Nothing_supplied_is_no_fields()
    {
        V(new I()).Fields.Should().BeEmpty();
    }

    [Theory]
    [InlineData("paperSize", "tabloid", "a4, letter, legal, a5, executive, b5")]
    [InlineData("orientation", "sideways", "portrait, landscape")]
    [InlineData("fontFamily", "Comic Sans", "serif, sans-serif, monospace, charter, times, palatino, bookman")]
    [InlineData("pageNumbering", "greek", "arabic, roman, none")]
    [InlineData("columnSeparator", "dots", "none, rule")]
    public void Enum_values_are_rejected_with_the_valid_choices(string field, string value, string valid)
    {
        var i = field switch
        {
            "paperSize" => new I(PaperSize: value),
            "orientation" => new I(Orientation: value),
            "fontFamily" => new I(FontFamily: value),
            "pageNumbering" => new I(PageNumbering: value),
            _ => new I(ColumnSeparator: value),
        };
        var r = V(i);
        r.Ok.Should().BeFalse();
        r.Update.Should().BeNull();
        r.Errors.Single().Should().Contain($"'{value}'").And.Contain(valid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Columns_out_of_range(int c) =>
        V(new I(Columns: c)).Errors.Single().Should().Contain("1 to 3");

    [Theory]
    [InlineData(9)]
    [InlineData(14)]
    public void Font_size_must_be_a_class_size(int size) =>
        V(new I(FontSize: size)).Errors.Single().Should().Contain("10, 11, 12");

    [Theory]
    [InlineData(0.5)]
    [InlineData(4)]
    public void Line_spacing_out_of_range(double ls) =>
        V(new I(LineSpacing: ls)).Errors.Single().Should().Contain("0.8 to 3");

    [Fact]
    public void Column_gap_is_bounded_in_centimetres() =>
        V(new I(ColumnGap: 9)).Errors.Single().Should().Contain("centimetres");

    [Theory]
    [InlineData("2")]              // no unit
    [InlineData("2 cm")]           // space
    [InlineData("2furlong")]       // unknown unit
    [InlineData("1in]{geometry}\\input{x}")] // LaTeX injection
    [InlineData("-1cm")]
    public void Margins_need_a_number_and_a_known_unit(string bad)
    {
        var r = V(new I(MarginTop: bad));
        r.Ok.Should().BeFalse();
        r.Errors.Single().Should().Contain("marginTop").And.Contain("unit");
    }

    [Fact]
    public void An_absurd_margin_is_refused_and_empty_resets()
    {
        V(new I(MarginTop: "50cm")).Errors.Single().Should().Contain("too large");
        var reset = V(new I(MarginTop: ""));
        reset.Ok.Should().BeTrue();
        reset.Update!.MarginTop.Should().Be("");
    }

    [Theory]
    [InlineData("1.5em", true)]
    [InlineData("none", true)]
    [InlineData("0pt", true)]
    [InlineData("big", false)]
    [InlineData("1em}\\input{x", false)]
    public void Paragraph_indent(string value, bool ok) =>
        V(new I(ParagraphIndent: value)).Ok.Should().Be(ok);

    [Fact]
    public void Header_and_footer_slots_are_single_line_and_bounded()
    {
        V(new I(HeaderLeft: "Lecture 3", FooterCenter: "")).Ok.Should().BeTrue();
        V(new I(HeaderLeft: "a\nb")).Errors.Single().Should().Contain("single line");
        V(new I(FooterRight: new string('x', 201))).Errors.Single().Should().Contain("200");
    }

    [Fact]
    public void Custom_preamble_passes_through_within_limits()
    {
        var pre = "\\newcommand{\\R}{\\mathbb{R}}";
        V(new I(CustomPreamble: pre)).Update!.CustomPreamble.Should().Be(pre);
        V(new I(CustomPreamble: new string('x', 20001))).Ok.Should().BeFalse();
    }

    [Fact]
    public void Every_error_is_reported_and_nothing_is_built()
    {
        var r = V(new I(PaperSize: "x", FontSize: 3));
        r.Errors.Should().HaveCount(2);
        r.Update.Should().BeNull();
    }
}

public class PaperOptionTests
{
    [Theory]
    [InlineData("a4", "a4paper")]
    [InlineData("letter", "letterpaper")]
    [InlineData("LEGAL", "legalpaper")]
    [InlineData("a5", "a5paper")]
    [InlineData("b5", "b5paper")]
    [InlineData("executive", "executivepaper")]
    [InlineData(null, "a4paper")]
    [InlineData("nonsense", "a4paper")]
    public void Every_stored_paper_size_reaches_the_class_options(string? stored, string expected) =>
        Lilia.Engines.LaTeXPreambleBuilder.ClassPaperOption(stored).Should().Be(expected);
}
