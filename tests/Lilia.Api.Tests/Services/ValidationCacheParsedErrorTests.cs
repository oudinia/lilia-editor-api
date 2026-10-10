using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A cache hit returned no parsed error, so the second check of a broken document
/// lost the line, and with it the block, its error stops in (10 Oct 2026: the
/// phone's Check document showed "Whole document" from the second check on).
/// </summary>
public class ValidationCacheParsedErrorTests
{
    [Fact]
    public async Task The_second_check_keeps_the_line_of_the_error()
    {
        // Unique content, so this test's first run is never a hit from another test.
        var latex = $"\\documentclass{{article}}\n\\begin{{document}}\n% {Guid.NewGuid()}\nText.\n\n\\badmacro x\n\\end{{document}}\n";
        var service = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);

        var first = await service.ValidateAsync(latex, "pdflatex");
        var second = await service.ValidateAsync(latex, "pdflatex");

        first.Valid.Should().BeFalse();
        first.ParsedError!.LineNumber.Should().Be(6);
        second.ParsedError.Should().NotBeNull("a cached failure is still a failure, with its line");
        second.ParsedError!.LineNumber.Should().Be(6);
    }
}
