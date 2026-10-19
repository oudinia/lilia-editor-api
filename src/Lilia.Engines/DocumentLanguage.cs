using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.Entities;

namespace Lilia.Engines;

/// <summary>
/// How one language is typeset (19 Oct). <c>Babel</c>: babel's name for it. <c>Engine</c>: the least
/// engine that sets it (Cyrillic and Greek need fontspec's Unicode faces; Arabic and Hebrew need
/// LuaLaTeX, which orders mixed right-to-left and left-to-right text by itself; Chinese, Japanese and
/// Korean need XeLaTeX's xeCJK). <c>Options</c>: babel options that keep Lilia's look (lists, maths,
/// section numbers) where the language would change it. <c>Provide</c>: loaded from babel's .ini
/// files rather than an option. <c>Fonts</c>: after fontspec, the faces the script needs.
/// <c>Cleveref</c>: cleveref's name for it, when cleveref knows it.
/// </summary>
public sealed record LanguageSpec(
    string Code, string Babel, LatexEngine Engine,
    string? Options = null, string? Setup = null, bool Provide = false, string? Fonts = null,
    string? Cleveref = null, bool Rtl = false, bool Cjk = false);

/// <summary>
/// The document's language in its PDF (19 Oct; the user: "support more languages, keep the UI with
/// the chosen ones"). Until now every PDF was English: a French document printed "Contents",
/// "Figure 1" and "References" and hyphenated as English, and Arabic did not run right to left.
///
/// <para><c>Document.Language</c> is a code from <see cref="Specs"/>, or <c>auto</c> (the default):
/// the language is then read from the text (<see cref="Detect"/>), English when unsure. English adds
/// nothing to the preamble, so an English document compiles exactly as before. A document whose own
/// preamble or class already loads babel or polyglossia keeps its own choice.</para>
///
/// <para>Babel's shorthands are off: no character becomes active, so nothing an author types changes
/// meaning (French ":" or Spanish "&lt;" in TikZ, for instance). Each line here was compiled with
/// pdflatex, xelatex or lualatex before it was written (sample documents in RoutingMeasure's sibling,
/// DocumentLanguageTests).</para>
/// </summary>
public static class DocumentLanguage
{
    public const string Auto = "auto";

    public static readonly IReadOnlyDictionary<string, LanguageSpec> Specs = new[]
    {
        new LanguageSpec("en", "english", LatexEngine.Pdflatex, Cleveref: "english"),
        new LanguageSpec("en-GB", "british", LatexEngine.Pdflatex, Cleveref: "english"),
        // Tableau, as French writers say (babel-french prints "Table"); captions not in small capitals, like every other language here.
        new LanguageSpec("fr", "french", LatexEngine.Pdflatex,
            Setup: @"\frenchsetup{StandardLists=true,SmallCapsFigTabCaptions=false}" + "\n" + @"\addto\captionsfrench{\def\tablename{Tableau}}",
            Cleveref: "french"),
        new LanguageSpec("de", "ngerman", LatexEngine.Pdflatex, Cleveref: "ngerman"),
        new LanguageSpec("es", "spanish", LatexEngine.Pdflatex, Options: "es-noshorthands,es-nodecimaldot,es-tabla,es-nosectiondot,es-nolists", Cleveref: "spanish"),
        new LanguageSpec("it", "italian", LatexEngine.Pdflatex, Cleveref: "italian"),
        new LanguageSpec("pt", "portuguese", LatexEngine.Pdflatex),
        new LanguageSpec("nl", "dutch", LatexEngine.Pdflatex, Cleveref: "dutch"),
        new LanguageSpec("pl", "polish", LatexEngine.Pdflatex),
        new LanguageSpec("sv", "swedish", LatexEngine.Pdflatex, Cleveref: "swedish"),
        new LanguageSpec("tr", "turkish", LatexEngine.Pdflatex),
        new LanguageSpec("ru", "russian", LatexEngine.Lualatex, Cleveref: "russian"),
        // From babel's .ini: the classic option, on LuaLaTeX with Lilia's packages, printed a lunate epsilon (ϵ) for ε.
        new LanguageSpec("el", "greek", LatexEngine.Lualatex, Provide: true),
        new LanguageSpec("ar", "arabic", LatexEngine.Lualatex, Provide: true, Rtl: true,
            Fonts: @"\babelfont[arabic]{rm}[BoldFont=Amiri-Bold.ttf]{Amiri-Regular.ttf}"),
        new LanguageSpec("he", "hebrew", LatexEngine.Lualatex, Provide: true, Rtl: true,
            Fonts: @"\babelfont[hebrew]{rm}[BoldFont=FreeSerifBold.otf,ItalicFont=FreeSerifItalic.otf]{FreeSerif.otf}"),
        new LanguageSpec("zh", "chinese", LatexEngine.Xelatex, Provide: true, Cjk: true),
        new LanguageSpec("ja", "japanese", LatexEngine.Xelatex, Provide: true, Cjk: true,
            Fonts: @"\setCJKmainfont{ipaexm.ttf}" + "\n" + @"\setCJKsansfont{ipaexg.ttf}"),
        new LanguageSpec("ko", "korean", LatexEngine.Xelatex, Provide: true, Cjk: true,
            Fonts: @"\xeCJKsetup{CJKspace=true}" + "\n" + @"\setCJKmainfont{UnBatang.ttf}"),
    }.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>The codes a document may hold, <c>auto</c> first.</summary>
    public static IReadOnlyList<string> Codes { get; } = new[] { Auto }.Concat(Specs.Keys).ToList();

    /// <summary>A valid stored value for <paramref name="raw"/> (case, region spelling), or null when it is not one.</summary>
    public static string? Normalize(string? raw)
    {
        var s = (raw ?? "").Trim().Replace('_', '-');
        if (s.Length == 0 || s.Equals(Auto, StringComparison.OrdinalIgnoreCase)) return Auto;
        if (Specs.TryGetValue(s, out var spec)) return spec.Code;
        var primary = s.Split('-')[0];
        return Specs.TryGetValue(primary, out var p) ? p.Code : null;
    }

    /// <summary>The language the document is typeset in: its setting, or what its text reads as.</summary>
    public static LanguageSpec For(Document doc)
    {
        var code = Normalize(doc.Language) ?? Auto;
        if (code != Auto) return Specs[code];
        return Specs[Detect(doc)];
    }

    /// <summary>
    /// Whether the document chose its language itself, in what it brings: its class options, its
    /// imported packages or its custom preamble load babel or polyglossia. Then Lilia adds nothing.
    /// </summary>
    public static bool OwnChoice(Document doc, string? importedPackages) =>
        LoadsLanguagePackage(importedPackages) || LoadsLanguagePackage(doc.CustomPreamble) || LoadsLanguagePackage(doc.LatexPackages);

    private static readonly Regex LanguagePackage = new(
        @"\\usepackage\s*(?:\[[^\]]*\])?\s*\{[^}]*(?<![A-Za-z-])(?:babel|polyglossia)(?![A-Za-z-])[^}]*\}|\\setmainlanguage|\\setdefaultlanguage",
        RegexOptions.Compiled);

    private static bool LoadsLanguagePackage(string? s) => !string.IsNullOrEmpty(s) && LanguagePackage.IsMatch(s);

    /// <summary>
    /// The preamble lines that set the language, before Lilia's packages (so hyperref and cleveref
    /// see it). Empty for English: an English document compiles exactly as before.
    /// </summary>
    public static string Preamble(LanguageSpec spec)
    {
        if (spec.Code == "en") return "";
        var sb = new StringBuilder("% Document language: ").Append(spec.Code).Append(" (DocumentLanguage)\n");
        if (spec.Provide)
        {
            sb.Append(spec.Rtl ? @"\usepackage[bidi=basic]{babel}" : @"\usepackage{babel}").Append('\n');
            sb.Append(@"\babelprovide[import,main]{").Append(spec.Babel).Append("}\n");
            // Latin letters in a right-to-left document are English, in the Latin font: set in the Arabic or
            // Hebrew face, their commas and full stops took that script's shapes (، for ,) and 17.8 read 8،17.
            if (spec.Rtl) sb.Append(@"\babelprovide[import,onchar=ids fonts]{english}").Append('\n');
        }
        else
        {
            var options = "shorthands=off," + (spec.Options is { } o ? o + "," : "") + spec.Babel;
            sb.Append(@"\usepackage[").Append(options).Append("]{babel}\n");
        }
        if (spec.Setup is { } setup) sb.Append(setup).Append('\n');
        if (spec.Cleveref is { } c) sb.Append(@"\PassOptionsToPackage{").Append(c).Append("}{cleveref}\n");
        return sb.ToString();
    }

    /// <summary>
    /// The names Lilia's theorem environments print in each language (Théorème, Teorema, مبرهنة…):
    /// Lilia declares them itself, so babel does not translate them. English, and anything missing,
    /// keeps Lilia's English names. The proof's name is babel's own (\proofname).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Dictionary<string, string>> TheoremNamesByLanguage = new Dictionary<string, Dictionary<string, string>>
    {
        ["fr"] = new Dictionary<string, string> { ["theorem"] = "Théorème", ["lemma"] = "Lemme", ["proposition"] = "Proposition", ["corollary"] = "Corollaire", ["definition"] = "Définition", ["example"] = "Exemple", ["remark"] = "Remarque", ["claim"] = "Affirmation", ["assumption"] = "Hypothèse", ["axiom"] = "Axiome", ["conjecture"] = "Conjecture", ["hypothesis"] = "Hypothèse", ["exercise"] = "Exercice", ["problem"] = "Problème", ["question"] = "Question", ["observation"] = "Observation", ["fact"] = "Fait", ["notation"] = "Notation", ["property"] = "Propriété" },
        ["es"] = new Dictionary<string, string> { ["theorem"] = "Teorema", ["lemma"] = "Lema", ["proposition"] = "Proposición", ["corollary"] = "Corolario", ["definition"] = "Definición", ["example"] = "Ejemplo", ["remark"] = "Observación", ["claim"] = "Afirmación", ["assumption"] = "Supuesto", ["axiom"] = "Axioma", ["conjecture"] = "Conjetura", ["hypothesis"] = "Hipótesis", ["exercise"] = "Ejercicio", ["problem"] = "Problema", ["question"] = "Pregunta", ["observation"] = "Observación", ["fact"] = "Hecho", ["notation"] = "Notación", ["property"] = "Propiedad" },
        ["de"] = new Dictionary<string, string> { ["theorem"] = "Satz", ["lemma"] = "Lemma", ["proposition"] = "Proposition", ["corollary"] = "Korollar", ["definition"] = "Definition", ["example"] = "Beispiel", ["remark"] = "Bemerkung", ["claim"] = "Behauptung", ["assumption"] = "Annahme", ["axiom"] = "Axiom", ["conjecture"] = "Vermutung", ["hypothesis"] = "Hypothese", ["exercise"] = "Übung", ["problem"] = "Problem", ["question"] = "Frage", ["observation"] = "Beobachtung", ["fact"] = "Fakt", ["notation"] = "Notation", ["property"] = "Eigenschaft" },
        ["it"] = new Dictionary<string, string> { ["theorem"] = "Teorema", ["lemma"] = "Lemma", ["proposition"] = "Proposizione", ["corollary"] = "Corollario", ["definition"] = "Definizione", ["example"] = "Esempio", ["remark"] = "Osservazione", ["claim"] = "Affermazione", ["assumption"] = "Ipotesi", ["axiom"] = "Assioma", ["conjecture"] = "Congettura", ["hypothesis"] = "Ipotesi", ["exercise"] = "Esercizio", ["problem"] = "Problema", ["question"] = "Domanda", ["observation"] = "Osservazione", ["fact"] = "Fatto", ["notation"] = "Notazione", ["property"] = "Proprietà" },
        ["pt"] = new Dictionary<string, string> { ["theorem"] = "Teorema", ["lemma"] = "Lema", ["proposition"] = "Proposição", ["corollary"] = "Corolário", ["definition"] = "Definição", ["example"] = "Exemplo", ["remark"] = "Observação", ["claim"] = "Afirmação", ["assumption"] = "Suposição", ["axiom"] = "Axioma", ["conjecture"] = "Conjectura", ["hypothesis"] = "Hipótese", ["exercise"] = "Exercício", ["problem"] = "Problema", ["question"] = "Questão", ["observation"] = "Observação", ["fact"] = "Facto", ["notation"] = "Notação", ["property"] = "Propriedade" },
        ["nl"] = new Dictionary<string, string> { ["theorem"] = "Stelling", ["lemma"] = "Lemma", ["proposition"] = "Propositie", ["corollary"] = "Gevolg", ["definition"] = "Definitie", ["example"] = "Voorbeeld", ["remark"] = "Opmerking", ["claim"] = "Bewering", ["assumption"] = "Aanname", ["axiom"] = "Axioma", ["conjecture"] = "Vermoeden", ["hypothesis"] = "Hypothese", ["exercise"] = "Oefening", ["problem"] = "Probleem", ["question"] = "Vraag", ["observation"] = "Observatie", ["fact"] = "Feit", ["notation"] = "Notatie", ["property"] = "Eigenschap" },
        ["pl"] = new Dictionary<string, string> { ["theorem"] = "Twierdzenie", ["lemma"] = "Lemat", ["proposition"] = "Stwierdzenie", ["corollary"] = "Wniosek", ["definition"] = "Definicja", ["example"] = "Przykład", ["remark"] = "Uwaga", ["claim"] = "Teza", ["assumption"] = "Założenie", ["axiom"] = "Aksjomat", ["conjecture"] = "Hipoteza", ["hypothesis"] = "Hipoteza", ["exercise"] = "Ćwiczenie", ["problem"] = "Problem", ["question"] = "Pytanie", ["observation"] = "Obserwacja", ["fact"] = "Fakt", ["notation"] = "Oznaczenie", ["property"] = "Własność" },
        ["sv"] = new Dictionary<string, string> { ["theorem"] = "Sats", ["lemma"] = "Lemma", ["proposition"] = "Proposition", ["corollary"] = "Korollarium", ["definition"] = "Definition", ["example"] = "Exempel", ["remark"] = "Anmärkning", ["claim"] = "Påstående", ["assumption"] = "Antagande", ["axiom"] = "Axiom", ["conjecture"] = "Förmodan", ["hypothesis"] = "Hypotes", ["exercise"] = "Övning", ["problem"] = "Problem", ["question"] = "Fråga", ["observation"] = "Observation", ["fact"] = "Faktum", ["notation"] = "Notation", ["property"] = "Egenskap" },
        ["tr"] = new Dictionary<string, string> { ["theorem"] = "Teorem", ["lemma"] = "Önsav", ["proposition"] = "Önerme", ["corollary"] = "Sonuç", ["definition"] = "Tanım", ["example"] = "Örnek", ["remark"] = "Not", ["claim"] = "İddia", ["assumption"] = "Varsayım", ["axiom"] = "Aksiyom", ["conjecture"] = "Sanı", ["hypothesis"] = "Hipotez", ["exercise"] = "Alıştırma", ["problem"] = "Problem", ["question"] = "Soru", ["observation"] = "Gözlem", ["fact"] = "Olgu", ["notation"] = "Gösterim", ["property"] = "Özellik" },
        ["ru"] = new Dictionary<string, string> { ["theorem"] = "Теорема", ["lemma"] = "Лемма", ["proposition"] = "Предложение", ["corollary"] = "Следствие", ["definition"] = "Определение", ["example"] = "Пример", ["remark"] = "Замечание", ["claim"] = "Утверждение", ["assumption"] = "Предположение", ["axiom"] = "Аксиома", ["conjecture"] = "Гипотеза", ["hypothesis"] = "Гипотеза", ["exercise"] = "Упражнение", ["problem"] = "Задача", ["question"] = "Вопрос", ["observation"] = "Наблюдение", ["fact"] = "Факт", ["notation"] = "Обозначение", ["property"] = "Свойство" },
        ["el"] = new Dictionary<string, string> { ["theorem"] = "Θεώρημα", ["lemma"] = "Λήμμα", ["proposition"] = "Πρόταση", ["corollary"] = "Πόρισμα", ["definition"] = "Ορισμός", ["example"] = "Παράδειγμα", ["remark"] = "Παρατήρηση", ["claim"] = "Ισχυρισμός", ["assumption"] = "Υπόθεση", ["axiom"] = "Αξίωμα", ["conjecture"] = "Εικασία", ["hypothesis"] = "Υπόθεση", ["exercise"] = "Άσκηση", ["problem"] = "Πρόβλημα", ["question"] = "Ερώτηση", ["observation"] = "Παρατήρηση", ["fact"] = "Γεγονός", ["notation"] = "Συμβολισμός", ["property"] = "Ιδιότητα" },
        ["ar"] = new Dictionary<string, string> { ["theorem"] = "مبرهنة", ["lemma"] = "تمهيدية", ["proposition"] = "قضية", ["corollary"] = "نتيجة", ["definition"] = "تعريف", ["example"] = "مثال", ["remark"] = "ملاحظة", ["claim"] = "ادعاء", ["assumption"] = "افتراض", ["axiom"] = "مسلّمة", ["conjecture"] = "حدسية", ["hypothesis"] = "فرضية", ["exercise"] = "تمرين", ["problem"] = "مسألة", ["question"] = "سؤال", ["observation"] = "ملاحظة", ["fact"] = "حقيقة", ["notation"] = "ترميز", ["property"] = "خاصية" },
        ["he"] = new Dictionary<string, string> { ["theorem"] = "משפט", ["lemma"] = "למה", ["proposition"] = "טענה", ["corollary"] = "מסקנה", ["definition"] = "הגדרה", ["example"] = "דוגמה", ["remark"] = "הערה", ["claim"] = "טענה", ["assumption"] = "הנחה", ["axiom"] = "אקסיומה", ["conjecture"] = "השערה", ["hypothesis"] = "השערה", ["exercise"] = "תרגיל", ["problem"] = "בעיה", ["question"] = "שאלה", ["observation"] = "תצפית", ["fact"] = "עובדה", ["notation"] = "סימון", ["property"] = "תכונה" },
        ["zh"] = new Dictionary<string, string> { ["theorem"] = "定理", ["lemma"] = "引理", ["proposition"] = "命题", ["corollary"] = "推论", ["definition"] = "定义", ["example"] = "例", ["remark"] = "注", ["claim"] = "断言", ["assumption"] = "假设", ["axiom"] = "公理", ["conjecture"] = "猜想", ["hypothesis"] = "假说", ["exercise"] = "练习", ["problem"] = "问题", ["question"] = "提问", ["observation"] = "观察", ["fact"] = "事实", ["notation"] = "记号", ["property"] = "性质" },
        ["ja"] = new Dictionary<string, string> { ["theorem"] = "定理", ["lemma"] = "補題", ["proposition"] = "命題", ["corollary"] = "系", ["definition"] = "定義", ["example"] = "例", ["remark"] = "注意", ["claim"] = "主張", ["assumption"] = "仮定", ["axiom"] = "公理", ["conjecture"] = "予想", ["hypothesis"] = "仮説", ["exercise"] = "演習", ["problem"] = "問題", ["question"] = "質問", ["observation"] = "観察", ["fact"] = "事実", ["notation"] = "記法", ["property"] = "性質" },
        ["ko"] = new Dictionary<string, string> { ["theorem"] = "정리", ["lemma"] = "보조정리", ["proposition"] = "명제", ["corollary"] = "따름정리", ["definition"] = "정의", ["example"] = "예", ["remark"] = "비고", ["claim"] = "주장", ["assumption"] = "가정", ["axiom"] = "공리", ["conjecture"] = "추측", ["hypothesis"] = "가설", ["exercise"] = "연습문제", ["problem"] = "문제", ["question"] = "질문", ["observation"] = "관찰", ["fact"] = "사실", ["notation"] = "표기법", ["property"] = "성질" },
    };

    /// <summary>The theorem names for <paramref name="spec"/>, or null to keep Lilia's English ones.</summary>
    public static IReadOnlyDictionary<string, string>? TheoremNames(LanguageSpec? spec) =>
        spec is not null && TheoremNamesByLanguage.TryGetValue(spec.Code, out var names) ? names : null;

    /// <summary>The faces the script needs, after fontspec and xeCJK are loaded.</summary>
    public static string Fonts(LanguageSpec spec) => spec.Fonts is { } f ? f + "\n" : "";

    /// <summary>The engine the language needs at least: compiling with less would lose its letters.</summary>
    public static LatexEngine AtLeast(LatexEngine engine, LanguageSpec spec) => spec.Engine > engine ? spec.Engine : engine;

    /// <summary>What a document's language asks of its compile.</summary>
    /// <param name="Preamble">Before Lilia's packages.</param>
    /// <param name="Fonts">After the engine addendum.</param>
    public sealed record Plan(LanguageSpec? Spec, LatexEngine Engine, bool Cjk, string Preamble, string Fonts);

    /// <summary>
    /// The language's part of a compile: its lines, and the engine and CJK setup it needs on top of
    /// <paramref name="engine"/> and <paramref name="hasCjk"/>. Nothing when the document chose its
    /// language itself (<see cref="OwnChoice"/>).
    /// </summary>
    public static Plan PlanFor(Document doc, string? importedPackages, LatexEngine engine, bool hasCjk)
    {
        if (OwnChoice(doc, importedPackages)) return new Plan(null, engine, hasCjk, "", "");
        var spec = For(doc);
        return new Plan(spec, AtLeast(engine, spec), hasCjk || spec.Cjk, Preamble(spec), Fonts(spec));
    }

    // ── Detection ──────────────────────────────────────────────────────────

    /// <summary>Content fields that hold code or identifiers, not prose.</summary>
    private static readonly HashSet<string> NotProse = new(StringComparer.OrdinalIgnoreCase)
    {
        "latex", "source", "code", "src", "label", "language", "url", "href", "citeKey", "key", "id",
        "kind", "type", "mode", "equationMode", "lml", "alt", "style", "theme",
    };

    private const int MaxChars = 20_000;

    /// <summary>The document's prose: its title and the text of its blocks, without code or maths.</summary>
    public static string Prose(Document doc) =>
        Prose(doc.Title, (doc.Blocks ?? new List<Block>()).OrderBy(b => b.SortOrder).Select(b => (b.Type, b.Content.RootElement)));

    /// <summary>The prose of a title and blocks given as type and content.</summary>
    public static string Prose(string? title, IEnumerable<(string Type, JsonElement Content)> blocks)
    {
        var sb = new StringBuilder(title ?? "").Append('\n');
        foreach (var (type, content) in blocks)
        {
            if (type is "code" or "equation" or "embed") continue;
            Collect(content, sb);
            if (sb.Length > MaxChars) break;
        }
        // Inline maths and commands are not language.
        return Regex.Replace(sb.ToString(), @"\$[^$]*\$|\\[A-Za-z]+\*?(\[[^\]]*\])?", " ");
    }

    private static void Collect(JsonElement e, StringBuilder sb)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: sb.Append(e.GetString()).Append('\n'); break;
            case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Collect(x, sb); break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    if (!NotProse.Contains(p.Name)) Collect(p.Value, sb);
                break;
        }
    }

    public static string Detect(Document doc) => Detect(Prose(doc));

    // The most frequent short words of each Latin-script language: distinctive between them.
    private static readonly Dictionary<string, HashSet<string>> Stopwords = new()
    {
        ["en"] = Words("the of and to in is that for it with as was on are be this by from or which an not have"),
        ["fr"] = Words("le la les des du de et est un une que qui dans pour pas sur au aux ce cette sont avec par il elle nous"),
        ["es"] = Words("el la los las de del y que en un una es por con para no se su al lo como más pero sus"),
        ["de"] = Words("der die das und ist nicht zu den von mit sich des auf für im dem ein eine auch es an werden"),
        ["it"] = Words("il lo la gli le di e che un una per non è con del della dei sono nel alla si da"),
        ["pt"] = Words("o a os as de e que do da em um uma para com não por se dos das no na mais ao"),
        ["nl"] = Words("de het een en van is dat op te in niet zijn voor met die er aan ook als bij"),
        ["pl"] = Words("i w na nie z do się jest że to o jak ale po co od przez dla jego tak"),
        ["sv"] = Words("och att det som en på är av för med till den inte har de om ett var jag"),
        ["tr"] = Words("ve bir bu da de için ile olarak çok daha gibi olan en ne var ama kadar"),
    };

    private static HashSet<string> Words(string s) => new(s.Split(' '), StringComparer.Ordinal);

    /// <summary>
    /// The language <paramref name="text"/> is written in, as a code of <see cref="Specs"/>; "en" when
    /// there is too little text or no clear winner. Scripts first (Arabic, Hebrew, kana, Hangul, Han,
    /// Cyrillic, Greek), then the share of each Latin language's most frequent words.
    /// </summary>
    public static string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "en";
        if (text.Length > MaxChars) text = text[..MaxChars];
        int letters = 0, arabic = 0, hebrew = 0, kana = 0, hangul = 0, han = 0, cyrillic = 0, greek = 0;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch)) continue;
            letters++;
            switch (ch)
            {
                case >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F' or >= '\uFB50' and <= '\uFEFF': arabic++; break;
                case >= '\u0590' and <= '\u05FF': hebrew++; break;
                case >= '\u3040' and <= '\u30FF': kana++; break;
                case >= '\uAC00' and <= '\uD7AF' or >= '\u1100' and <= '\u11FF': hangul++; break;
                case >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF': han++; break;
                case >= '\u0400' and <= '\u04FF': cyrillic++; break;
                case >= '\u0370' and <= '\u03FF' or >= '\u1F00' and <= '\u1FFF': greek++; break;
            }
        }
        if (letters < 10) return "en";
        bool Most(int n) => n > letters * 0.3;
        if (Most(arabic)) return "ar";
        if (Most(hebrew)) return "he";
        if (kana > 0 && Most(kana + han)) return "ja";
        if (Most(hangul)) return "ko";
        if (Most(han)) return "zh";
        if (Most(cyrillic)) return "ru";
        if (Most(greek)) return "el";
        if (letters < 40) return "en";

        var words = Regex.Matches(text.ToLowerInvariant(), @"\p{L}+").Select(m => m.Value).ToList();
        if (words.Count < 12) return "en";
        var scores = Stopwords.ToDictionary(kv => kv.Key, kv => words.Count(kv.Value.Contains));
        var ranked = scores.OrderByDescending(kv => kv.Value).ToList();
        var (best, top) = (ranked[0].Key, ranked[0].Value);
        var second = ranked[1].Value;
        // A clear winner: a real share of the words, and well ahead of the next language.
        if (top >= 3 && top >= words.Count * 0.08 && top >= second * 1.5) return best;
        return "en";
    }
}
