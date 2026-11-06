using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>Reading a document's language from its text, and the codes it may hold (19 Oct).</summary>
public class DocumentLanguageDetectTests
{
    [Theory]
    [InlineData("fr", "La méthode que nous avons utilisée pour les données est décrite dans la section suivante, avec les résultats et une discussion sur les limites de cette approche dans le cadre de notre étude.")]
    [InlineData("es", "El método que hemos utilizado para los datos se describe en la sección siguiente, con los resultados y una discusión sobre los límites de este enfoque en el marco de nuestro estudio.")]
    [InlineData("de", "Die Methode, die wir für die Daten verwendet haben, wird im folgenden Abschnitt beschrieben, mit den Ergebnissen und einer Diskussion über die Grenzen dieses Ansatzes in unserer Studie.")]
    [InlineData("it", "Il metodo che abbiamo usato per i dati è descritto nella sezione seguente, con i risultati e una discussione sui limiti di questo approccio nel quadro del nostro studio.")]
    [InlineData("pt", "O método que usamos para os dados é descrito na seção seguinte, com os resultados e uma discussão sobre os limites desta abordagem no quadro do nosso estudo.")]
    [InlineData("en", "The method we used for the data is described in the following section, with the results and a discussion of the limits of this approach within the scope of our study.")]
    [InlineData("ar", "تصف هذه الدراسة القصيرة الطريقة والنتائج التي حصلنا عليها في المختبر خلال العام الماضي.")]
    [InlineData("ja", "この短い研究では、方法と得られた結果について説明します。データは昨年集めました。")]
    [InlineData("zh", "这项简短的研究描述了我们使用的方法和得到的结果。数据是去年收集的。")]
    [InlineData("ko", "이 짧은 연구는 방법과 우리가 얻은 결과를 설명합니다. 데이터는 작년에 모았습니다.")]
    [InlineData("ru", "Это короткое исследование описывает метод и полученные нами результаты за прошлый год.")]
    public void The_text_s_language_is_read(string expected, string text) =>
        DocumentLanguage.Detect(text).Should().Be(expected);

    [Fact]
    public void An_english_document_with_an_image_and_lettered_items_reads_as_English()
    {
        // The launch bot's document (issue 8, 6 Nov 2026): it read as Portuguese, from the hex letters of
        // the image's assetId and "Bullet A", so the PDF printed "5 de novembro de 2026".
        static System.Text.Json.JsonElement J(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement;
        var prose = DocumentLanguage.Prose("Untitled Document", new[]
        {
            ("paragraph", J("""{"text":"Intro title"}""")),
            ("heading", J("""{"text":"First section","level":1}""")),
            ("paragraph", J("""{"text":"Some body text here. ZZTOP typed As shown by @cite{x"}""")),
            ("figure", J("""{"alt":"fig.png","src":"/uploads/u/documents/67d690f2-3d2a-4cf4-b7bf-9dc3acb48787/images/d807e00e-53d0-4947-a4c8-fc49ae0cc742.png","assetId":"d807e00e-53d0-4947-a4c8-fc49ae0cc742","caption":""}""")),
            ("paragraph", J("""{"text":"Item one"}""")),
            ("paragraph", J("""{"text":"Item two"}""")),
            ("list", J("""{"kind":"bullet","items":["Bullet A","Bullet B"],"spacing":"default","labelFormat":"number"}""")),
            ("table", J("""{"caption":"Results","headers":["Column 1","Column 2","Column 3"]}""")),
        });

        prose.Should().NotContain("d807e00e").And.NotContain("default");
        DocumentLanguage.Detect(prose).Should().Be("en");
    }

    [Fact]
    public void Single_letters_are_no_evidence_of_a_language()
    {
        DocumentLanguage.Detect("a e o a e o a e o a e o a e o the item list and more words here to read").Should().Be("en");
    }

    [Theory]
    [InlineData("Bonjour")]
    [InlineData("")]
    [InlineData("x = 1, y = 2, z = 3, w = 4 and so on 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20")]
    public void Too_little_or_unclear_text_reads_as_English(string text) =>
        DocumentLanguage.Detect(text).Should().Be("en");

    [Theory]
    [InlineData("", "auto")]
    [InlineData("AUTO", "auto")]
    [InlineData("fr", "fr")]
    [InlineData("en_gb", "en-GB")]
    [InlineData("fr-CA", "fr")]
    [InlineData("pt-BR", "pt")]
    [InlineData("klingon", null)]
    public void Codes_are_normalised_or_refused(string raw, string? stored) =>
        DocumentLanguage.Normalize(raw).Should().Be(stored);

    [Fact]
    public void English_adds_nothing_to_the_preamble_and_others_turn_shorthands_off()
    {
        DocumentLanguage.Preamble(DocumentLanguage.Specs["en"]).Should().BeEmpty();
        DocumentLanguage.Preamble(DocumentLanguage.Specs["fr"]).Should().Contain(@"\usepackage[shorthands=off,french]{babel}");
        DocumentLanguage.Preamble(DocumentLanguage.Specs["ar"]).Should().Contain("bidi=basic").And.Contain(@"\babelprovide[import,main]{arabic}");
    }
}
