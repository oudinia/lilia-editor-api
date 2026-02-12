using Lilia.Import.Models;

namespace Lilia.Import.Detection;

/// <summary>
/// Multi-language keyword registry mapping section heading text to SectionType.
/// Covers 14+ languages for academic document section detection.
/// </summary>
public static class SectionKeywordRegistry
{
    private static readonly Dictionary<SectionType, HashSet<string>> _keywords = new()
    {
        [SectionType.Abstract] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Abstract", "Summary", "Executive Summary",
            // French
            "Résumé", "Sommaire",
            // German
            "Zusammenfassung", "Kurzfassung",
            // Spanish
            "Resumen",
            // Portuguese
            "Resumo",
            // Italian
            "Riassunto",
            // Arabic
            "ملخص",
            // Chinese
            "摘要",
            // Japanese
            "要旨", "概要",
            // Russian
            "Аннотация",
            // Korean
            "초록",
            // Turkish
            "Özet",
            // Dutch
            "Samenvatting",
            // Polish
            "Streszczenie"
        },

        [SectionType.Introduction] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Introduction",
            // French
            "Introduction",
            // German
            "Einleitung",
            // Spanish
            "Introducción",
            // Portuguese
            "Introdução",
            // Italian
            "Introduzione",
            // Arabic
            "مقدمة",
            // Chinese
            "引言", "前言",
            // Japanese
            "はじめに", "序論",
            // Russian
            "Введение",
            // Korean
            "서론",
            // Turkish
            "Giriş",
            // Dutch
            "Inleiding",
            // Polish
            "Wstęp"
        },

        [SectionType.Methods] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Methods", "Methodology", "Materials and Methods", "Experimental",
            "Methods and Materials", "Experimental Methods", "Research Methods",
            // French
            "Méthodes", "Méthodologie", "Matériels et Méthodes",
            // German
            "Methoden", "Methodik", "Material und Methoden",
            // Spanish
            "Métodos", "Metodología", "Materiales y Métodos",
            // Portuguese
            "Métodos", "Metodologia",
            // Italian
            "Metodi", "Metodologia",
            // Chinese
            "方法", "研究方法",
            // Japanese
            "方法", "研究方法",
            // Russian
            "Методы", "Методология",
            // Korean
            "방법", "연구방법"
        },

        [SectionType.Results] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Results", "Findings", "Results and Discussion",
            // French
            "Résultats",
            // German
            "Ergebnisse",
            // Spanish
            "Resultados",
            // Portuguese
            "Resultados",
            // Italian
            "Risultati",
            // Chinese
            "结果",
            // Japanese
            "結果",
            // Russian
            "Результаты",
            // Korean
            "결과"
        },

        [SectionType.Discussion] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Discussion", "Analysis", "Discussion and Analysis",
            // French
            "Discussion",
            // German
            "Diskussion",
            // Spanish
            "Discusión",
            // Portuguese
            "Discussão",
            // Italian
            "Discussione",
            // Chinese
            "讨论",
            // Japanese
            "考察",
            // Russian
            "Обсуждение",
            // Korean
            "토론", "논의"
        },

        [SectionType.Conclusion] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Conclusion", "Conclusions", "Concluding Remarks",
            // French
            "Conclusion", "Conclusions",
            // German
            "Fazit", "Schlussfolgerungen",
            // Spanish
            "Conclusión", "Conclusiones",
            // Portuguese
            "Conclusão", "Conclusões",
            // Italian
            "Conclusione", "Conclusioni",
            // Chinese
            "结论",
            // Japanese
            "結論", "まとめ",
            // Russian
            "Заключение",
            // Korean
            "결론"
        },

        [SectionType.References] = new(StringComparer.OrdinalIgnoreCase)
        {
            "References", "Bibliography", "Works Cited", "Literature",
            "Literature Cited", "Reference List",
            // French
            "Références", "Bibliographie",
            // German
            "Literaturverzeichnis", "Literatur", "Quellenverzeichnis",
            // Spanish
            "Referencias", "Bibliografía",
            // Portuguese
            "Referências", "Bibliografia",
            // Italian
            "Riferimenti", "Bibliografia",
            // Arabic
            "المراجع",
            // Chinese
            "参考文献",
            // Japanese
            "参考文献",
            // Russian
            "Литература", "Список литературы",
            // Korean
            "참고문헌",
            // Turkish
            "Kaynakça",
            // Dutch
            "Referenties", "Bibliografie",
            // Polish
            "Bibliografia", "Piśmiennictwo"
        },

        [SectionType.Acknowledgements] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Acknowledgements", "Acknowledgments", "Acknowledgement",
            // French
            "Remerciements",
            // German
            "Danksagung",
            // Spanish
            "Agradecimientos",
            // Portuguese
            "Agradecimentos",
            // Italian
            "Ringraziamenti",
            // Chinese
            "致谢",
            // Japanese
            "謝辞",
            // Russian
            "Благодарности",
            // Korean
            "감사의 글"
        },

        [SectionType.Appendix] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Appendix", "Appendices", "Supplementary Material", "Supplementary",
            "Supporting Information",
            // French
            "Annexe", "Annexes",
            // German
            "Anhang", "Anlage",
            // Spanish
            "Apéndice", "Anexo",
            // Portuguese
            "Apêndice", "Anexo",
            // Italian
            "Appendice", "Allegato",
            // Chinese
            "附录",
            // Japanese
            "付録",
            // Russian
            "Приложение",
            // Korean
            "부록"
        },

        [SectionType.Background] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Background", "Related Work", "Literature Review", "State of the Art",
            "Previous Work", "Prior Work", "Theoretical Framework",
            // French
            "Contexte", "État de l'art", "Travaux connexes",
            // German
            "Hintergrund", "Stand der Forschung", "Stand der Technik",
            // Spanish
            "Antecedentes", "Estado del arte",
            // Chinese
            "背景", "相关工作", "文献综述"
        },

        [SectionType.LiteratureReview] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Literature Review", "Review of Literature",
            // French
            "Revue de littérature",
            // German
            "Literaturüberblick",
            // Spanish
            "Revisión de la literatura"
        },

        [SectionType.TableOfContents] = new(StringComparer.OrdinalIgnoreCase)
        {
            "Table of Contents", "Contents",
            // French
            "Table des matières",
            // German
            "Inhaltsverzeichnis",
            // Spanish
            "Índice", "Tabla de contenidos"
        },

        [SectionType.ListOfFigures] = new(StringComparer.OrdinalIgnoreCase)
        {
            "List of Figures", "Figures",
            // French
            "Liste des figures",
            // German
            "Abbildungsverzeichnis"
        },

        [SectionType.ListOfTables] = new(StringComparer.OrdinalIgnoreCase)
        {
            "List of Tables", "Tables",
            // French
            "Liste des tableaux",
            // German
            "Tabellenverzeichnis"
        }
    };

    /// <summary>
    /// All registered keywords by section type.
    /// </summary>
    public static IReadOnlyDictionary<SectionType, HashSet<string>> Keywords => _keywords;

    /// <summary>
    /// Try to classify a heading text as a known section type.
    /// Returns SectionType.Unknown if no match is found.
    /// </summary>
    public static SectionType Classify(string headingText)
    {
        if (string.IsNullOrWhiteSpace(headingText))
            return SectionType.Unknown;

        var trimmed = headingText.Trim();

        // Strip numbering prefixes like "1. ", "I. ", "1.1 " for matching
        var stripped = StripNumberingPrefix(trimmed);

        foreach (var (sectionType, keywords) in _keywords)
        {
            if (keywords.Contains(stripped) || keywords.Contains(trimmed))
                return sectionType;
        }

        return SectionType.Unknown;
    }

    /// <summary>
    /// Check if a text matches any abstract keyword exactly (case-insensitive).
    /// </summary>
    public static bool IsAbstractKeyword(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return _keywords[SectionType.Abstract].Contains(text.Trim());
    }

    /// <summary>
    /// Check if a text matches any references/bibliography keyword exactly (case-insensitive).
    /// </summary>
    public static bool IsReferencesKeyword(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return _keywords[SectionType.References].Contains(text.Trim());
    }

    private static string StripNumberingPrefix(string text)
    {
        // Strip patterns like "1. ", "1.1 ", "I. ", "II. ", "A. "
        var match = System.Text.RegularExpressions.Regex.Match(text, @"^(?:\d+(?:\.\d+)*\.?\s+|[IVXLC]+\.\s+|[A-Z]\.\s+)(.+)$");
        return match.Success ? match.Groups[1].Value : text;
    }
}
