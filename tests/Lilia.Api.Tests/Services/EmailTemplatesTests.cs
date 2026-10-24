using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Services.Email;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>The 17 Oct email designs, filled: every variable replaced, in each language.</summary>
public class EmailTemplatesTests
{
    private static Dictionary<string, string?> Values(string? firstName = "Nadia") => new()
    {
        ["action_url"] = "https://editor.liliaeditor.com/auth/callback?token=a&b",
        ["recipient_email"] = "nadia@example.com",
        ["first_name"] = firstName,
        ["publisher_line"] = null,
    };

    [Theory]
    [InlineData("welcome"), InlineData("verify"), InlineData("signin"), InlineData("reset")]
    public void Every_language_fills_every_variable(string key)
    {
        foreach (var lang in EmailTemplates.Languages)
        {
            var email = EmailTemplates.Render(key, lang, Values());
            email.Subject.Should().NotBeNullOrWhiteSpace();
            email.Html.Should().NotContain("{{", $"{lang}.{key}.html");
            email.Text.Should().NotContain("{{", $"{lang}.{key}.txt");
            email.Html.Should().Contain("token=a&amp;b", "values are HTML-escaped");
            email.Text.Should().Contain("token=a&b", "the text part gets them as they are");
        }
    }

    [Fact]
    public void The_two_step_code_is_in_the_subject_and_the_body_in_every_language()
    {
        foreach (var lang in EmailTemplates.Languages)
        {
            var email = EmailTemplates.Render("two_step_code", lang, new Dictionary<string, string?>
            {
                ["code"] = "042917", ["recipient_email"] = "nadia@lilia.test", ["publisher_line"] = null,
            });
            email.Subject.Should().Contain("042917", lang);
            email.Html.Should().Contain("042917").And.Contain("nadia@lilia.test").And.NotContain("{{", lang);
            email.Text.Should().Contain("042917").And.NotContain("{{", lang);
        }
    }

    [Fact]
    public void The_welcome_greets_by_name_and_reads_well_without_one()
    {
        EmailTemplates.Render("welcome", "fr", Values()).Text.Should().Contain("Bienvenue sur Lilia, Nadia");
        var nameless = EmailTemplates.Render("welcome", "en", Values(firstName: null));
        nameless.Text.Should().Contain("Welcome to Lilia\n").And.NotContain("Lilia, ");
        nameless.Subject.Should().Be("Welcome to Lilia");
    }

    [Fact]
    public void Languages_fall_back_to_English()
    {
        EmailTemplates.Language("fr-FR").Should().Be("fr");
        EmailTemplates.Language("ES").Should().Be("es");
        EmailTemplates.Language("de").Should().Be("en");
        EmailTemplates.Language(null).Should().Be("en");
        EmailTemplates.Render("verify", "de", Values()).Subject.Should().Be("Confirm your email for Lilia");
    }

    [Fact]
    public void The_test_copy_gets_its_own_links()
    {
        var email = EmailTemplates.Render("welcome", "en", Values(), "http://localhost:3003");
        email.Html.Should().NotContain(EmailTemplates.DesignBaseUrl + "/help").And.Contain("http://localhost:3003/help");
    }

    [Fact]
    public void An_empty_publisher_line_leaves_no_empty_paragraph()
    {
        var withLine = EmailTemplates.Render("welcome", "en", new Dictionary<string, string?>(Values()) { ["publisher_line"] = "Lilia EI, Paris" });
        withLine.Html.Should().Contain("Lilia EI, Paris");
        EmailTemplates.Render("welcome", "en", Values()).Text.TrimEnd().Should().EndWith("/help");
    }

    [Theory]
    [InlineData("Nadia Benali", "Nadia"), InlineData("  Omar ", "Omar"), InlineData("", null), InlineData(null, null)]
    public void First_names(string? name, string? first) => EmailService.FirstName(name).Should().Be(first);
}
