using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>The launch bot's #119 (16 Nov 2026): a control space ("Dr.\ Smith") stayed as "\ " in imported text.</summary>
public class ControlSpaceImportTests
{
    [Theory]
    [InlineData(@"Dr.\ Smith and Prof.\ Jones agree.", "Dr. Smith and Prof. Jones agree.")]
    public async Task A_control_space_is_a_space(string body, string expected)
    {
        var doc = await new LatexParser().ParseTextAsync($"\\documentclass{{article}}\\begin{{document}}{body}\\end{{document}}");
        doc.Elements.OfType<ImportParagraph>().Single().Text.Should().Be(expected);
    }

    [Fact]
    public void Maths_keeps_its_control_space() =>
        LatexParser.ControlSpaces(@"Dr.\ Smith: $a\ b$ and \(c\ d\).").Should().Be(@"Dr. Smith: $a\ b$ and \(c\ d\).");
}
