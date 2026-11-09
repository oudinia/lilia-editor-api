using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// A new table Ask Lilia adds without headers takes its first row as the header (9 Nov 2026: a 7-column table came
/// in with its header names as the first row, and the editor filled in "Column 1–3").
/// </summary>
public class AskTableHeadersTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void The_first_row_becomes_the_header()
    {
        var c = AskLiliaService.WithTableHeaders("table", J("""{"rows":[["Case","Error A","Time A"],["1","1e-2","0.04"],["2","9e-3","0.09"]],"caption":"C","label":"tab:x"}"""));
        c.GetProperty("headers").EnumerateArray().Select(e => e.GetString()).Should().Equal("Case", "Error A", "Time A");
        c.GetProperty("rows").GetArrayLength().Should().Be(2);
        c.GetProperty("rows")[0][0].GetString().Should().Be("1");
        c.GetProperty("label").GetString().Should().Be("tab:x");
    }

    [Theory]
    [InlineData("""{"headers":["A","B"],"rows":[["1","2"]]}""")]
    [InlineData("""{"hasHeader":false,"rows":[["1","2"],["3","4"]]}""")]
    [InlineData("""{"rows":[]}""")]
    public void A_table_with_headers_none_wanted_or_no_rows_is_unchanged(string json)
    {
        var before = J(json);
        AskLiliaService.WithTableHeaders("table", before).GetRawText().Should().Be(before.GetRawText());
    }

    [Fact]
    public void Other_blocks_are_unchanged()
    {
        var p = J("""{"rows":[["a"]]}""");
        AskLiliaService.WithTableHeaders("paragraph", p).GetRawText().Should().Be(p.GetRawText());
    }
}
