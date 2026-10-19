using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Services;

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
