using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Integration.Kb;

/// <summary>
/// The knowledge base in English, French and Spanish, at three levels (the user, 12 Oct:
/// "user documentation at different levels and a knowledge base for AI"). Seeded from the
/// embedded articles into kb_articles, keyed by (slug, lang).
/// </summary>
[Collection("Integration")]
public class KbLanguagesAndLevelsTests : IntegrationTestBase
{
    public KbLanguagesAndLevelsTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<IKbService> SeededAsync()
    {
        var scope = Fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IKbSeeder>().PreloadAsync();
        return scope.ServiceProvider.GetRequiredService<IKbService>();
    }

    [Theory]
    [InlineData("getting-started")]
    [InlineData("compile-and-validate")]
    [InlineData("sharing")]
    [InlineData("sign-in")]
    [InlineData("saving-and-versions")]
    [InlineData("export")]
    [InlineData("import")]
    [InlineData("view-only")]
    [InlineData("ask-lilia")]
    [InlineData("citations")]
    [InlineData("trash")]
    [InlineData("phone")]
    [InlineData("appearance-and-language")]
    [InlineData("document-look")]
    [InlineData("teams")]
    public async Task Each_task_article_has_three_languages_and_three_levels(string slug)
    {
        var kb = await SeededAsync();
        foreach (var lang in new[] { "en", "fr", "es" })
        {
            var a = await kb.GetAsync(slug, lang);
            a.Should().NotBeNull();
            a!.Lang.Should().Be(lang);
            a.Steps.Should().NotBeNullOrWhiteSpace();
            a.Beginner.Should().NotBeNullOrWhiteSpace();
            a.Expert.Should().NotBeNullOrWhiteSpace();
            a.Body.Should().NotContain("{#steps}", "the level markers are not shown");
        }
    }

    [Fact]
    public async Task An_untranslated_article_falls_back_to_English()
    {
        var kb = await SeededAsync();
        var a = await kb.GetAsync("figures", "fr");
        a.Should().NotBeNull();
        a!.Lang.Should().Be("en");
    }

    [Theory]
    [InlineData("comment partager un document avec un collègue", "fr", "sharing")]
    [InlineData("¿cómo comparto un documento?", "es", "sharing")]
    [InlineData("pourquoi mon document ne compile pas", "fr", "compile-and-validate")]
    [InlineData("validar el documento y corregir errores", "es", "compile-and-validate")]
    [InlineData("share a document with a colleague", "en", "sharing")]
    [InlineData("comment restaurer un document supprimé", "fr", "trash")]
    [InlineData("passer en mode sombre", "fr", "appearance-and-language")]
    [InlineData("exportar a word", "es", "export")]
    [InlineData("olvidé mi contraseña", "es", "sign-in")]
    public async Task Search_finds_the_article_in_the_users_language(string query, string lang, string slug)
    {
        var kb = await SeededAsync();
        var hits = await kb.SearchAsync(query, 6, lang);
        hits.Select(h => h.Slug).Should().Contain(slug);
    }

    [Fact]
    public async Task Ask_Lilia_reads_the_level_that_fits_the_author()
    {
        var kb = await SeededAsync();
        var a = (await kb.GetAsync("compile-and-validate", "fr"))!;

        string Extra(object o) => (string)o.GetType().GetProperty("forThisAuthor")!.GetValue(o)!;
        Extra(AskLiliaService.KbForLevel(a, Proficiency.Beginner)).Should().Be(a.Beginner);
        Extra(AskLiliaService.KbForLevel(a, Proficiency.Advanced)).Should().Be(a.Expert);
        Extra(AskLiliaService.KbForLevel(a, Proficiency.Intermediate)).Should().BeEmpty();
    }
}
