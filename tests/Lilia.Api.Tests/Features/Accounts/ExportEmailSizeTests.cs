using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Features.Accounts;

/// <summary>The bot's Level 5 (12 Nov 2026): a 6.8 KB export's email read "1 MB".</summary>
public class ExportEmailSizeTests
{
    [Theory]
    [InlineData(6_963L, "en", "7 KB")]
    [InlineData(6_963L, "fr", "7 Ko")]
    [InlineData(300L, "en", "1 KB")]
    [InlineData(2_516_582L, "en", "2.4 MB")]
    [InlineData(2_516_582L, "fr", "2,4 Mo")]
    [InlineData(2_516_582L, "es", "2,4 MB")]
    [InlineData(47_185_920L, "en", "45 MB")]
    public void Sizes_read_as_they_are(long bytes, string lang, string expected)
    {
        EmailService.FileSize(bytes, lang).Should().Be(expected);
    }
}
