using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// Typst isn't pinned (14 Nov 2026): start-up says which version runs the previews, and warns when it isn't the one
/// Lilia's output was checked with, so a quiet upgrade shows in the log instead of in someone's PDF.
/// </summary>
public class TypstVersionCheckTests
{
    [Theory]
    [InlineData("typst 0.15.1 (9dfd3a08)\n", "0.15.1")]
    [InlineData("typst 0.14.0", "0.14.0")]
    [InlineData("typst 0.16.0-rc1 (abc)", "0.16.0-rc1")]
    [InlineData("error: no such option", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_version_is_read_from_the_binary_s_answer(string? output, string? version)
        => TypstCompileService.ParseVersion(output).Should().Be(version);

    [Fact]
    public void The_checked_version_is_reported_as_ready()
    {
        var (line, warn) = TypstCompileService.BootLine("/usr/local/bin/typst", "0.15.1", "0.15.1");
        warn.Should().BeFalse();
        line.Should().Be("[BOOT] Typst 0.15.1 ready: /usr/local/bin/typst");
    }

    [Fact]
    public void Another_version_warns_and_says_what_to_do()
    {
        var (line, warn) = TypstCompileService.BootLine("/x/typst", "0.16.0", "0.15.1");
        warn.Should().BeTrue();
        line.Should().Contain("0.16.0").And.Contain("NOT the checked 0.15.1").And.Contain("ProcedureVersion");
    }

    [Fact]
    public void No_binary_warns_that_previews_fall_back_to_LaTeX()
    {
        var (line, warn) = TypstCompileService.BootLine(null, null);
        warn.Should().BeTrue();
        line.Should().Contain("NOT FOUND").And.Contain("TYPST_BINARY");
    }

    [Fact]
    public void A_binary_that_gives_no_version_warns()
        => TypstCompileService.BootLine("/x/typst", null).Warn.Should().BeTrue();

    [Fact]
    public async Task The_installed_binary_is_described()
    {
        var (path, version) = await TypstCompileService.DescribeBinaryAsync();
        if (path is null) return; // no Typst on this machine: nothing to describe
        version.Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }
}
