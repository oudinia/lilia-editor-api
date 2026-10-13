using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP-37 (run 3b): a compile error didn't name the undefined command. And \lipsum, common in
/// drafts and templates, stopped the compile once such text went to LaTeX.
/// </summary>
public class ErrorNameTests
{
    [Theory]
    [InlineData("! Undefined control sequence.\nl.15 \\missingcmd\n", "\\missingcmd")]
    [InlineData("! Undefined control sequence.\nl.15 Some \\textbf{x} and \\missingcmd\n", "\\missingcmd")]
    [InlineData("! Undefined control sequence.\n<argument> \\foo \n               bar\nl.15 \\textbf{\\foo bar}\n", "\\foo")]
    public void The_undefined_command_is_named(string log, string command) =>
        LaTeXErrorParser.Parse(log)!.Token.Should().Be(command);

    [Theory]
    [InlineData(@"\lipsum[1-2]", @"\usepackage{lipsum}")]
    [InlineData(@"\blindtext", @"\usepackage{blindtext}")]
    public void Filler_text_loads_its_package(string text, string line) =>
        LaTeXPreamble.MhchemLine([text]).Should().Contain(line);

    [Fact]
    public void An_author_who_loads_lipsum_gets_no_second_one() =>
        LaTeXPreamble.MhchemLine([@"\lipsum"], @"\usepackage{lipsum}").Should().NotContain("lipsum");
}
