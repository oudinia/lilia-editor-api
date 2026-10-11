using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The test copy's emails list their links on localhost too (the user, 11 Oct:
/// "both open document using localhost and using the domain").
/// </summary>
public class EmailAlsoLinksTests
{
    private const string Base = "https://test.liliaeditor.com";
    private const string Also = "http://localhost:3003";

    [Fact]
    public void Html_keeps_the_domain_link_and_adds_the_same_on_localhost()
    {
        var html = "<html><body><a href=\"https://test.liliaeditor.com/invites/abc\">Open document</a></body></html>";
        var result = EmailService.WithAlsoLinksHtml(html, Base, Also);
        result.Should().Contain("href=\"https://test.liliaeditor.com/invites/abc\"");
        result.Should().Contain("href=\"http://localhost:3003/invites/abc\"");
        result.Should().Contain("Same links on localhost:3003");
        result.IndexOf("localhost:3003/invites").Should().BeLessThan(result.IndexOf("</body>"));
    }

    [Fact]
    public void Links_elsewhere_are_left_alone_and_no_links_means_no_block()
    {
        var html = "<p><a href=\"https://stytch.com/x\">x</a></p>";
        EmailService.WithAlsoLinksHtml(html, Base, Also).Should().Be(html);
    }

    [Fact]
    public void Text_gets_the_same_list()
    {
        var text = "Open: https://test.liliaeditor.com/d/42?share=open\nThanks";
        EmailService.WithAlsoLinksText(text, Base, Also).Should().EndWith("Same links on localhost:3003:\nhttp://localhost:3003/d/42?share=open");
    }
}
