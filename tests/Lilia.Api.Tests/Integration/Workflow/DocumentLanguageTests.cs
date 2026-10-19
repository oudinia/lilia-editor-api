using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Integration.Workflow;

/// <summary>
/// The document's language in its PDF (19 Oct; the user: "support more languages, keep the UI with the
/// chosen ones"). Every language prints its own headings (the abstract's here), with the engine its
/// script needs; auto reads the language from the text; a document that loads babel itself keeps it.
/// Real TeX, real PDFs, read back with pdftotext.
/// </summary>
[Collection("Integration")]
[Trait("Category", "RequiresTeX")]
public class DocumentLanguageTests : IntegrationTestBase
{
    private readonly string _userId = $"language-{Guid.NewGuid():N}"[..28];
    private HttpClient _client = null!;

    public DocumentLanguageTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        _client = CreateClientAs(_userId);
    }

    public static TheoryData<string, string, string, string> Languages => new()
    {
        { "en", "This short study describes the method and the results we obtained.", "Abstract", "pdflatex" },
        { "en-GB", "This short study describes the colour of the behaviour we observed.", "Abstract", "pdflatex" },
        { "fr", "Cette courte étude décrit la méthode et les résultats que nous avons obtenus.", "Résumé", "pdflatex" },
        { "de", "Diese kurze Studie beschreibt die Methode und die Ergebnisse, die wir erhalten haben.", "Zusammenfassung", "pdflatex" },
        { "es", "Este breve estudio describe el método y los resultados que obtuvimos.", "Resumen", "pdflatex" },
        { "it", "Questo breve studio descrive il metodo e i risultati che abbiamo ottenuto.", "Sommario", "pdflatex" },
        { "pt", "Este breve estudo descreve o método e os resultados que obtivemos.", "Resumo", "pdflatex" },
        { "nl", "Deze korte studie beschrijft de methode en de resultaten die we hebben verkregen.", "Samenvatting", "pdflatex" },
        { "pl", "To krótkie badanie opisuje metodę i wyniki, które uzyskaliśmy.", "Streszczenie", "pdflatex" },
        { "sv", "Denna korta studie beskriver metoden och resultaten som vi fick.", "Sammanfattning", "pdflatex" },
        { "tr", "Bu kısa çalışma yöntemi ve elde ettiğimiz sonuçları açıklamaktadır.", "Özet", "pdflatex" },
        { "ru", "Это короткое исследование описывает метод и полученные нами результаты.", "Аннотация", "lualatex" },
        { "el", "Αυτή η σύντομη μελέτη περιγράφει τη μέθοδο και τα αποτελέσματα που λάβαμε.", "Περίληψη", "lualatex" },
        { "ar", "تصف هذه الدراسة القصيرة الطريقة والنتائج التي حصلنا عليها.", "ملخص", "lualatex" },
        { "he", "מחקר קצר זה מתאר את השיטה ואת התוצאות שקיבלנו.", "תקציר", "lualatex" },
        { "zh", "这项简短的研究描述了我们使用的方法和得到的结果。", "摘要", "xelatex" },
        { "ja", "この短い研究では、方法と得られた結果について説明します。", "概要", "xelatex" },
        { "ko", "이 짧은 연구는 방법과 우리가 얻은 결과를 설명합니다.", "요", "xelatex" },
    };

    private async Task<Guid> DocumentAsync(string language, string text, string? customPreamble = null)
    {
        var res = await _client.PostAsJsonAsync("/api/documents", new { title = $"Language {language}", language });
        res.EnsureSuccessStatusCode();
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        if (customPreamble is not null)
            (await _client.PutAsJsonAsync($"/api/documents/{id}", new { customPreamble })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/documents/{id}/blocks", new { type = "abstract", content = new { text }, sortOrder = 0 })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/documents/{id}/blocks", new { type = "paragraph", content = new { text = text + " " + text }, sortOrder = 1 })).EnsureSuccessStatusCode();
        return id;
    }

    private async Task<(string Text, string Engine)> PdfAsync(Guid id)
    {
        var res = await _client.GetAsync($"/api/documents/{id}/export/pdf");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var engine = res.Headers.TryGetValues("X-Render-Engine", out var e) ? e.First() : "";
        return (PdfText(await res.Content.ReadAsByteArrayAsync()), engine);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task Each_language_prints_its_own_headings_with_the_engine_its_script_needs(string code, string text, string abstractName, string engine)
    {
        var id = await DocumentAsync(code, text);
        var (pdf, used) = await PdfAsync(id);
        pdf.Should().Contain(abstractName, $"{code}'s abstract heading");
        if (code is "en" or "en-GB") used.Should().BeOneOf("typst", "pdflatex", "English needs nothing: Typst may print it, as before");
        else used.Should().Be(engine);
        if (code != "en" && code != "en-GB") pdf.Should().NotContain("Abstract");
    }

    [Fact]
    public async Task Auto_reads_the_language_from_the_text_and_says_which()
    {
        var french = await DocumentAsync("auto", "Cette courte étude décrit la méthode et les résultats que nous avons obtenus pour les élèves de la région.");
        (await PdfAsync(french)).Text.Should().Contain("Résumé");
        var doc = await (await _client.GetAsync($"/api/documents/{french}")).Content.ReadFromJsonAsync<JsonElement>();
        doc.GetProperty("language").GetString().Should().Be("auto");
        doc.GetProperty("detectedLanguage").GetString().Should().Be("fr");

        var english = await DocumentAsync("auto", "This short study describes the method and the results we obtained for the students of the region.");
        var (text, engine) = await PdfAsync(english);
        text.Should().Contain("Abstract");
        engine.Should().BeOneOf("typst", "pdflatex", "English adds nothing: the document compiles as before");
    }

    [Fact]
    public async Task Theorems_and_tables_are_named_in_the_language_too()
    {
        // The user's review (19 Oct): "Theorem 1" stayed English, and French tables read "Table".
        var id = await DocumentAsync("fr", "Cette courte étude décrit la méthode et les résultats.");
        (await _client.PostAsJsonAsync($"/api/documents/{id}/blocks", new { type = "theorem", content = new { theoremType = "theorem", text = "La moyenne est comprise entre les extrêmes." }, sortOrder = 2 })).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/documents/{id}/blocks", new { type = "table", content = new { caption = "Mesures", headers = new[] { "A", "B" }, rows = new[] { new[] { "1", "2" } } }, sortOrder = 3 })).EnsureSuccessStatusCode();
        var pdf = (await PdfAsync(id)).Text;
        pdf.Should().Contain("Théorème 1").And.Contain("Tableau 1").And.NotContain("Theorem");
    }

    [Fact]
    public async Task English_inside_an_Arabic_document_keeps_its_own_punctuation_and_numbers()
    {
        // In the Arabic face, "Knuth, The TeXbook" printed Arabic commas (،) and 17.8 read 8،17.
        var id = await DocumentAsync("ar", "استخدمنا برنامج Python لتحليل البيانات، والمتوسط 17.8 درجة. انظر Knuth, The TeXbook.");
        var pdf = (await PdfAsync(id)).Text;
        pdf.Should().Contain("Knuth, The TeXbook").And.Contain("17.8").And.NotContain("Knuth،");
    }

    [Fact]
    public async Task Numbers_in_an_Arabic_table_read_left_to_right()
    {
        // The review's second Arabic sample: decimal-aligned columns printed 17.8 as 8.17.
        var id = await DocumentAsync("ar", "قسنا درجة حرارة الماء في ثلاثة أنهار كل أسبوع لمدة عام كامل.");
        (await _client.PostAsJsonAsync($"/api/documents/{id}/blocks", new
        {
            type = "table",
            content = new { caption = "النتائج", headers = new[] { "النهر", "الشتاء", "الصيف" }, rows = new[] { new[] { "الشمال", "4.1", "17.8" }, new[] { "الجنوب", "6.3", "21.2" } } },
            sortOrder = 2,
        })).EnsureSuccessStatusCode();
        var pdf = (await PdfAsync(id)).Text;
        pdf.Should().Contain("17.8").And.Contain("21.2").And.NotContain("8.17").And.NotContain("2.21");
        pdf.Should().NotContain("ءاتشلا", "the column's head stays right to left (it printed backwards with the first fix)");
    }

    [Fact]
    public async Task A_new_document_reads_its_language_from_the_text_unless_told()
    {
        var res = await _client.PostAsJsonAsync("/api/documents", new { title = "No language given" });
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("language").GetString()
            .Should().Be("auto", "the create request's default was \"en\", which made every new document English");
    }

    [Fact]
    public async Task A_document_that_loads_babel_itself_keeps_its_own_choice()
    {
        var id = await DocumentAsync("fr", "Diese kurze Studie beschreibt die Methode und die Ergebnisse.", @"\usepackage[ngerman]{babel}");
        (await PdfAsync(id)).Text.Should().Contain("Zusammenfassung", "its own babel, not a second one that would clash");
    }

    [Fact]
    public async Task A_language_Lilia_does_not_know_is_refused_by_the_settings_tool_and_ignored_by_the_editor()
    {
        var id = await DocumentAsync("fr", "Cette étude.");
        (await _client.PutAsJsonAsync($"/api/documents/{id}", new { language = "klingon" })).EnsureSuccessStatusCode();
        (await (await _client.GetAsync($"/api/documents/{id}")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("language").GetString().Should().Be("fr");
        var check = Lilia.Api.Services.DocumentSettingsValidator.Validate(new(Language: "klingon"));
        check.Ok.Should().BeFalse();
        check.Errors.Single().Should().Contain("auto").And.Contain("ja");
        Lilia.Api.Services.DocumentSettingsValidator.Validate(new(Language: "fr-CA")).Update!.Language.Should().Be("fr");
    }

    private static string PdfText(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lilia-lang-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            var p = Process.Start(new ProcessStartInfo("pdftotext", $"\"{path}\" -") { RedirectStandardOutput = true })!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return text;
        }
        finally { File.Delete(path); }
    }
}
