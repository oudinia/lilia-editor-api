using System.Diagnostics;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Services.Semantic;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// How well Ask Lilia finds the right skill (19 Oct): the keyword router, meaning alone, and the
/// two together (<see cref="SemanticAskRouter.Decide"/>), on 132 messages written the way people
/// write, in English, French and Spanish, and never used as examples. Runs when the model is
/// installed (ops/models.sh); prints the table, and holds the combination to what it measured.
/// </summary>
public class RoutingMeasureTests
{
    private readonly ITestOutputHelper _out;
    public RoutingMeasureTests(ITestOutputHelper output) => _out = output;

    internal static readonly (string Message, string Skill, string Lang)[] Set =
    {
        // citations
        ("can you add the attention is all you need paper to my refs", "lilia-citations", "en"),
        ("I only have the arXiv link 2106.09685, make it a proper reference", "lilia-citations", "en"),
        ("the year is wrong in one of my sources", "lilia-citations", "en"),
        ("put a citation to Smith 2020 after this sentence", "lilia-citations", "en"),
        ("j'ai un DOI, tu peux me faire l'entrée ?", "lilia-citations", "fr"),
        ("ajoute Dupont et al. 2019 dans mes sources", "lilia-citations", "fr"),
        ("une de mes références n'a pas d'année", "lilia-citations", "fr"),
        ("mets une citation de Hinton ici", "lilia-citations", "fr"),
        ("tengo el ISBN del libro, añádelo a mis fuentes", "lilia-citations", "es"),
        ("añade a García 2018 en las referencias", "lilia-citations", "es"),
        ("una de mis fuentes tiene mal el autor", "lilia-citations", "es"),
        ("pon una cita a ese artículo de Nature", "lilia-citations", "es"),
        // compile doctor
        ("why is my pdf blank", "lilia-compile-doctor", "en"),
        ("it says runaway argument, help", "lilia-compile-doctor", "en"),
        ("the preview shows a red message since I pasted some latex", "lilia-compile-doctor", "en"),
        ("something broke after my last edit and nothing renders", "lilia-compile-doctor", "en"),
        ("le pdf ne s'affiche plus du tout", "lilia-compile-doctor", "fr"),
        ("j'ai un message « undefined control sequence »", "lilia-compile-doctor", "fr"),
        ("depuis que j'ai collé du code ça plante", "lilia-compile-doctor", "fr"),
        ("pourquoi l'aperçu est vide ?", "lilia-compile-doctor", "fr"),
        ("el pdf sale en blanco", "lilia-compile-doctor", "es"),
        ("me aparece un mensaje rojo en la vista previa", "lilia-compile-doctor", "es"),
        ("desde que pegué ese código no funciona nada", "lilia-compile-doctor", "es"),
        ("dice missing $ inserted, qué hago", "lilia-compile-doctor", "es"),
        // equation
        ("how do I write the gradient of f with respect to x", "lilia-equation", "en"),
        ("I need the expression for kinetic energy", "lilia-equation", "en"),
        ("write sum from i equals 1 to n of i squared", "lilia-equation", "en"),
        ("the cross entropy loss please", "lilia-equation", "en"),
        ("comment on écrit une limite quand x tend vers zéro", "lilia-equation", "fr"),
        ("j'ai besoin de l'expression de l'énergie cinétique", "lilia-equation", "fr"),
        ("écris la somme des carrés de 1 à n", "lilia-equation", "fr"),
        ("la loi de Bayes en LaTeX stp", "lilia-equation", "fr"),
        ("cómo escribo el gradiente de una función", "lilia-equation", "es"),
        ("necesito la expresión de la energía cinética", "lilia-equation", "es"),
        ("escribe la suma de los cuadrados de 1 a n", "lilia-equation", "es"),
        ("el teorema de Pitágoras con símbolos", "lilia-equation", "es"),
        // table
        ("I need a grid comparing four models on three datasets", "lilia-table", "en"),
        ("format these numbers as rows and columns", "lilia-table", "en"),
        ("my excel sheet should go in the paper", "lilia-table", "en"),
        ("add a column with the standard deviation", "lilia-table", "en"),
        ("j'ai besoin d'une grille qui compare quatre modèles", "lilia-table", "fr"),
        ("présente ces valeurs en lignes et colonnes", "lilia-table", "fr"),
        ("ajoute une colonne avec l'écart type", "lilia-table", "fr"),
        ("mon fichier excel doit aller dans l'article", "lilia-table", "fr"),
        ("necesito una cuadrícula que compare cuatro modelos", "lilia-table", "es"),
        ("ordena estos números en filas y columnas", "lilia-table", "es"),
        ("agrega una columna con la desviación estándar", "lilia-table", "es"),
        ("mi hoja de excel tiene que ir en el artículo", "lilia-table", "es"),
        // figure
        ("show the architecture as boxes and arrows", "lilia-figure", "en"),
        ("a histogram of the ages would help here", "lilia-figure", "en"),
        ("illustrate a binary tree", "lilia-figure", "en"),
        ("visualise the training loss over epochs", "lilia-figure", "en"),
        ("montre l'architecture avec des boîtes et des flèches", "lilia-figure", "fr"),
        ("un histogramme des âges serait utile", "lilia-figure", "fr"),
        ("illustre un arbre binaire", "lilia-figure", "fr"),
        ("visualise la perte au fil des époques", "lilia-figure", "fr"),
        ("muestra la arquitectura con cajas y flechas", "lilia-figure", "es"),
        ("un histograma de las edades ayudaría", "lilia-figure", "es"),
        ("ilustra un árbol binario", "lilia-figure", "es"),
        ("visualiza la pérdida durante el entrenamiento", "lilia-figure", "es"),
        // tools
        ("I have my thesis in Google Docs, can I bring it over", "lilia-tools", "en"),
        ("turn this pasted html into proper blocks", "lilia-tools", "en"),
        ("move my old .tex project in here", "lilia-tools", "en"),
        ("change this markdown into lilia format", "lilia-tools", "en"),
        ("j'ai ma thèse sur Google Docs, je peux la ramener ici ?", "lilia-tools", "fr"),
        ("transforme ce html collé en blocs", "lilia-tools", "fr"),
        ("je veux faire venir mon ancien projet .tex", "lilia-tools", "fr"),
        ("passe ce markdown au format Lilia", "lilia-tools", "fr"),
        ("tengo mi tesis en Google Docs, puedo traerla", "lilia-tools", "es"),
        ("transforma este html pegado en bloques", "lilia-tools", "es"),
        ("quiero traer mi antiguo proyecto .tex", "lilia-tools", "es"),
        ("pasa este markdown al formato de Lilia", "lilia-tools", "es"),
        // polish
        ("this sounds too casual for a paper", "lilia-polish", "en"),
        ("cut this paragraph down to half", "lilia-polish", "en"),
        ("can you make my conclusion flow better", "lilia-polish", "en"),
        ("there are typos everywhere in section 2", "lilia-polish", "en"),
        ("ça fait trop familier pour un article", "lilia-polish", "fr"),
        ("réduis ce paragraphe de moitié", "lilia-polish", "fr"),
        ("fais que ma conclusion se lise mieux", "lilia-polish", "fr"),
        ("il y a des fautes partout dans la section 2", "lilia-polish", "fr"),
        ("suena demasiado informal para un artículo", "lilia-polish", "es"),
        ("reduce este párrafo a la mitad", "lilia-polish", "es"),
        ("haz que mi conclusión fluya mejor", "lilia-polish", "es"),
        ("hay faltas de ortografía en la sección 2", "lilia-polish", "es"),
        // coverage
        ("will chemfig work here", "lilia-coverage", "en"),
        ("my advisor uses beamer themes, are those ok", "lilia-coverage", "en"),
        ("is xelatex with custom fonts possible", "lilia-coverage", "en"),
        ("can lilia deal with algorithm2e", "lilia-coverage", "en"),
        ("est-ce que chemfig marche ici ?", "lilia-coverage", "fr"),
        ("mon directeur utilise des thèmes beamer, ça passe ?", "lilia-coverage", "fr"),
        ("xelatex avec mes propres polices, c'est possible ?", "lilia-coverage", "fr"),
        ("lilia gère algorithm2e ?", "lilia-coverage", "fr"),
        ("¿funciona chemfig aquí?", "lilia-coverage", "es"),
        ("mi director usa temas de beamer, ¿sirven?", "lilia-coverage", "es"),
        ("¿se puede xelatex con fuentes propias?", "lilia-coverage", "es"),
        ("¿lilia acepta algorithm2e?", "lilia-coverage", "es"),
        // journal
        ("we're sending this to CVPR next week", "lilia-journal", "en"),
        ("the conference needs their official style", "lilia-journal", "en"),
        ("make it look like an APS paper", "lilia-journal", "en"),
        ("prepare the camera ready version", "lilia-journal", "en"),
        ("on l'envoie à une conférence la semaine prochaine, il faut leur style", "lilia-journal", "fr"),
        ("donne-lui l'apparence d'un article de l'APS", "lilia-journal", "fr"),
        ("prépare la version finale pour la publication", "lilia-journal", "fr"),
        ("la revue demande son modèle officiel", "lilia-journal", "fr"),
        ("lo enviamos a un congreso la semana que viene, con su estilo", "lilia-journal", "es"),
        ("que parezca un artículo de la APS", "lilia-journal", "es"),
        ("prepara la versión final para publicar", "lilia-journal", "es"),
        ("la revista pide su plantilla oficial", "lilia-journal", "es"),
        // tutor
        ("I can't find where to rename my document", "lilia-tutor", "en"),
        ("what happens if I click make a copy", "lilia-tutor", "en"),
        ("is there a way to go back to yesterday's version", "lilia-tutor", "en"),
        ("why can my colleague only read and not edit", "lilia-tutor", "en"),
        ("je ne trouve pas où renommer mon document", "lilia-tutor", "fr"),
        ("que se passe-t-il si je clique sur faire une copie", "lilia-tutor", "fr"),
        ("je peux revenir à la version d'hier ?", "lilia-tutor", "fr"),
        ("pourquoi mon collègue peut seulement lire", "lilia-tutor", "fr"),
        ("no encuentro dónde cambiar el nombre del documento", "lilia-tutor", "es"),
        ("qué pasa si hago clic en hacer una copia", "lilia-tutor", "es"),
        ("¿puedo volver a la versión de ayer?", "lilia-tutor", "es"),
        ("por qué mi colega solo puede leer", "lilia-tutor", "es"),
        // architect
        ("help me get started on my dissertation about coral reefs", "lilia-document-architect", "en"),
        ("I need a grant proposal skeleton", "lilia-document-architect", "en"),
        ("make me a homework sheet with five exercises on limits", "lilia-document-architect", "en"),
        ("put together a poster for the symposium", "lilia-document-architect", "en"),
        ("aide-moi à commencer mon mémoire sur les récifs coralliens", "lilia-document-architect", "fr"),
        ("j'ai besoin du squelette d'une demande de financement", "lilia-document-architect", "fr"),
        ("fais-moi une feuille d'exercices sur les limites", "lilia-document-architect", "fr"),
        ("monte un poster pour le colloque", "lilia-document-architect", "fr"),
        ("ayúdame a empezar mi tesina sobre arrecifes de coral", "lilia-document-architect", "es"),
        ("necesito el esqueleto de una solicitud de financiación", "lilia-document-architect", "es"),
        ("hazme una hoja de ejercicios sobre límites", "lilia-document-architect", "es"),
        ("arma un póster para el simposio", "lilia-document-architect", "es"),
    };

    /// <summary>
    /// A second set, written after the examples were widened with the first set's misses in view, on
    /// other topics and in other words: the first set is no longer unseen, this one is.
    /// </summary>
    internal static readonly (string Message, string Skill, string Lang)[] Fresh =
    {
        ("I want to reference the Lancet study on sleep we discussed", "lilia-citations", "en"),
        ("the page numbers in my sources are missing", "lilia-citations", "en"),
        ("turn this url into a reference: nature.com/articles/s41586-020-2649-2", "lilia-citations", "en"),
        ("which style are my references in, I need APA", "lilia-citations", "en"),
        ("je veux citer le rapport du GIEC de 2021", "lilia-citations", "fr"),
        ("les numéros de pages manquent dans mes sources", "lilia-citations", "fr"),
        ("transforme ce lien en référence propre", "lilia-citations", "fr"),
        ("mes références doivent être au format APA", "lilia-citations", "fr"),
        ("quiero citar el informe de la OMS de 2023", "lilia-citations", "es"),
        ("faltan los números de página en mis fuentes", "lilia-citations", "es"),
        ("convierte este enlace en una referencia", "lilia-citations", "es"),
        ("mis referencias tienen que ir en formato APA", "lilia-citations", "es"),
        ("the export keeps failing with an error about a file not found", "lilia-compile-doctor", "en"),
        ("TeX complains about an extra alignment tab", "lilia-compile-doctor", "en"),
        ("half my document disappeared from the PDF", "lilia-compile-doctor", "en"),
        ("there's a warning that something is too wide", "lilia-compile-doctor", "en"),
        ("l'export échoue avec une erreur de fichier introuvable", "lilia-compile-doctor", "fr"),
        ("TeX se plaint d'une tabulation en trop", "lilia-compile-doctor", "fr"),
        ("la moitié du document a disparu du PDF", "lilia-compile-doctor", "fr"),
        ("j'ai un avertissement, quelque chose dépasse la marge", "lilia-compile-doctor", "fr"),
        ("la exportación falla con un error de archivo no encontrado", "lilia-compile-doctor", "es"),
        ("TeX se queja de un tabulador de alineación de más", "lilia-compile-doctor", "es"),
        ("la mitad del documento desapareció del PDF", "lilia-compile-doctor", "es"),
        ("hay un aviso de que algo es demasiado ancho", "lilia-compile-doctor", "es"),
        ("the Fourier transform of a Gaussian, typeset nicely", "lilia-equation", "en"),
        ("write Maxwell's equations in differential form", "lilia-equation", "en"),
        ("I want x hat with a subscript t", "lilia-equation", "en"),
        ("give me the binomial coefficient n choose k", "lilia-equation", "en"),
        ("la transformée de Fourier d'une gaussienne, bien écrite", "lilia-equation", "fr"),
        ("écris les équations de Maxwell", "lilia-equation", "fr"),
        ("je veux x chapeau avec un indice t", "lilia-equation", "fr"),
        ("le coefficient binomial k parmi n", "lilia-equation", "fr"),
        ("la transformada de Fourier de una gaussiana", "lilia-equation", "es"),
        ("escribe las ecuaciones de Maxwell", "lilia-equation", "es"),
        ("quiero x con sombrero y subíndice t", "lilia-equation", "es"),
        ("el coeficiente binomial n sobre k", "lilia-equation", "es"),
        ("list the participants with age, sex and group in a table", "lilia-table", "en"),
        ("make the header row bold and add a caption", "lilia-table", "en"),
        ("split this long table across two pages", "lilia-table", "en"),
        ("convert this tab separated data into something I can put in the paper", "lilia-table", "en"),
        ("liste les participants avec âge, sexe et groupe dans un tableau", "lilia-table", "fr"),
        ("mets la ligne d'en-tête en gras et ajoute une légende au tableau", "lilia-table", "fr"),
        ("coupe ce long tableau sur deux pages", "lilia-table", "fr"),
        ("ces données séparées par des tabulations, mets-les en forme", "lilia-table", "fr"),
        ("lista los participantes con edad, sexo y grupo en una tabla", "lilia-table", "es"),
        ("pon la fila de encabezado en negrita y añade un título a la tabla", "lilia-table", "es"),
        ("divide esta tabla larga en dos páginas", "lilia-table", "es"),
        ("estos datos separados por tabuladores, dales formato", "lilia-table", "es"),
        ("a scatter plot of height against weight", "lilia-figure", "en"),
        ("draw a commutative diagram for these maps", "lilia-figure", "en"),
        ("a timeline of the project milestones as a picture", "lilia-figure", "en"),
        ("the venn diagram of sets A, B and C", "lilia-figure", "en"),
        ("un nuage de points de la taille selon le poids", "lilia-figure", "fr"),
        ("dessine un diagramme commutatif", "lilia-figure", "fr"),
        ("une frise chronologique du projet en image", "lilia-figure", "fr"),
        ("le diagramme de Venn de A, B et C", "lilia-figure", "fr"),
        ("un diagrama de dispersión de altura frente a peso", "lilia-figure", "es"),
        ("dibuja un diagrama conmutativo", "lilia-figure", "es"),
        ("una línea de tiempo del proyecto en imagen", "lilia-figure", "es"),
        ("el diagrama de Venn de A, B y C", "lilia-figure", "es"),
        ("I exported my notes from Notion, can Lilia read them", "lilia-tools", "en"),
        ("open this zip of my Overleaf project", "lilia-tools", "en"),
        ("bring in the bib file I have on my laptop", "lilia-tools", "en"),
        ("my coauthor sent a docx with tracked changes, get it in", "lilia-tools", "en"),
        ("j'ai exporté mes notes de Notion, Lilia peut les lire ?", "lilia-tools", "fr"),
        ("ouvre ce zip de mon projet Overleaf", "lilia-tools", "fr"),
        ("fais entrer le fichier bib que j'ai sur mon ordinateur", "lilia-tools", "fr"),
        ("mon coauteur a envoyé un docx, fais-le entrer", "lilia-tools", "fr"),
        ("exporté mis notas de Notion, ¿Lilia puede leerlas?", "lilia-tools", "es"),
        ("abre este zip de mi proyecto de Overleaf", "lilia-tools", "es"),
        ("trae el archivo bib que tengo en mi portátil", "lilia-tools", "es"),
        ("mi coautor envió un docx, mételo", "lilia-tools", "es"),
        ("my supervisor says the discussion rambles", "lilia-polish", "en"),
        ("avoid repeating the word significant so often", "lilia-polish", "en"),
        ("put this paragraph in the passive voice", "lilia-polish", "en"),
        ("translate my abstract into proper academic English", "lilia-polish", "en"),
        ("mon directeur dit que la discussion part dans tous les sens", "lilia-polish", "fr"),
        ("évite de répéter le mot important si souvent", "lilia-polish", "fr"),
        ("mets ce paragraphe à la voix passive", "lilia-polish", "fr"),
        ("rends mon résumé plus soutenu", "lilia-polish", "fr"),
        ("mi tutor dice que la discusión se va por las ramas", "lilia-polish", "es"),
        ("evita repetir tanto la palabra importante", "lilia-polish", "es"),
        ("pon este párrafo en voz pasiva", "lilia-polish", "es"),
        ("haz mi resumen más académico", "lilia-polish", "es"),
        ("does lilia know the physics package for bra-ket notation", "lilia-coverage", "en"),
        ("can I load my own .sty file", "lilia-coverage", "en"),
        ("is lualatex available or only pdflatex", "lilia-coverage", "en"),
        ("will forest trees from the forest package render", "lilia-coverage", "en"),
        ("lilia connaît le paquet physics pour la notation bra-ket ?", "lilia-coverage", "fr"),
        ("je peux charger mon propre fichier .sty ?", "lilia-coverage", "fr"),
        ("lualatex est disponible ou seulement pdflatex ?", "lilia-coverage", "fr"),
        ("le paquet forest est-il pris en compte ?", "lilia-coverage", "fr"),
        ("¿lilia conoce el paquete physics para la notación bra-ket?", "lilia-coverage", "es"),
        ("¿puedo cargar mi propio archivo .sty?", "lilia-coverage", "es"),
        ("¿está lualatex o solo pdflatex?", "lilia-coverage", "es"),
        ("¿el paquete forest funciona?", "lilia-coverage", "es"),
        ("the editor asked for anonymous review formatting", "lilia-journal", "en"),
        ("switch the paper to the AAAI layout", "lilia-journal", "en"),
        ("my thesis must follow the university's template", "lilia-journal", "en"),
        ("the workshop has an eight page limit in their format", "lilia-journal", "en"),
        ("l'éditeur demande une version anonyme pour la relecture", "lilia-journal", "fr"),
        ("passe l'article à la mise en page AAAI", "lilia-journal", "fr"),
        ("ma thèse doit suivre le modèle de l'université", "lilia-journal", "fr"),
        ("l'atelier impose huit pages dans son format", "lilia-journal", "fr"),
        ("el editor pide una versión anónima para la revisión", "lilia-journal", "es"),
        ("cambia el artículo al formato de AAAI", "lilia-journal", "es"),
        ("mi tesis debe seguir la plantilla de la universidad", "lilia-journal", "es"),
        ("el taller tiene un límite de ocho páginas en su formato", "lilia-journal", "es"),
        ("how do I get the PDF onto my phone", "lilia-tutor", "en"),
        ("what's the difference between the free and pro plan", "lilia-tutor", "en"),
        ("can I work offline", "lilia-tutor", "en"),
        ("where are the comments people left", "lilia-tutor", "en"),
        ("comment avoir le PDF sur mon téléphone", "lilia-tutor", "fr"),
        ("quelle différence entre l'offre gratuite et pro", "lilia-tutor", "fr"),
        ("je peux travailler hors ligne ?", "lilia-tutor", "fr"),
        ("où sont les commentaires laissés par les autres", "lilia-tutor", "fr"),
        ("cómo paso el PDF a mi móvil", "lilia-tutor", "es"),
        ("qué diferencia hay entre el plan gratuito y el pro", "lilia-tutor", "es"),
        ("¿puedo trabajar sin conexión?", "lilia-tutor", "es"),
        ("dónde están los comentarios que dejaron", "lilia-tutor", "es"),
        ("write me a cover letter for a phd position", "lilia-document-architect", "en"),
        ("a lecture handout on graph theory for second years", "lilia-document-architect", "en"),
        ("I need a literature review on transformers to start from", "lilia-document-architect", "en"),
        ("set up a weekly lab notebook", "lilia-document-architect", "en"),
        ("écris-moi une lettre de motivation pour un doctorat", "lilia-document-architect", "fr"),
        ("un polycopié de cours sur la théorie des graphes", "lilia-document-architect", "fr"),
        ("il me faut une revue de littérature sur les transformers pour démarrer", "lilia-document-architect", "fr"),
        ("prépare un cahier de laboratoire hebdomadaire", "lilia-document-architect", "fr"),
        ("escríbeme una carta de motivación para un doctorado", "lilia-document-architect", "es"),
        ("unos apuntes de clase sobre teoría de grafos", "lilia-document-architect", "es"),
        ("necesito una revisión bibliográfica sobre transformers para empezar", "lilia-document-architect", "es"),
        ("prepara un cuaderno de laboratorio semanal", "lilia-document-architect", "es"),
    };

    internal static string ModelDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local/share/lilia/models", E5Embedder.ModelName);

    [Fact]
    public void The_measuring_set_never_repeats_an_example()
    {
        var examples = SkillExamples.All.SelectMany(kv => kv.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Set.Select(s => s.Message).Where(examples.Contains).Should().BeEmpty();
        Set.GroupBy(s => s.Skill).Should().HaveCount(11).And.OnlyContain(g => g.Count() == 12);
        Fresh.Select(s => s.Message).Where(examples.Contains).Should().BeEmpty();
        Fresh.GroupBy(s => s.Skill).Should().HaveCount(11).And.OnlyContain(g => g.Count() == 12);
    }

    [Fact]
    public void Meaning_finds_the_skill_better_than_keywords_alone()
    {
        Measure("first set (examples widened from its misses)", Set, assertBetter: false);
        Measure("fresh set (unseen)", Fresh, assertBetter: true);
    }

    private void Measure(string title, (string Message, string Skill, string Lang)[] Set, bool assertBetter)
    {
        _out.WriteLine($"\n### {title}");
        using var embedder = new E5Embedder(ModelDir, NullLogger<E5Embedder>.Instance);
        if (!embedder.Ready) return;   // the model isn't installed here (ops/models.sh)
        var keywords = new AskLiliaRouter();
        var classifier = new ExampleClassifier(embedder, SkillExamples.All);
        classifier.Classify("warm up");

        var clock = Stopwatch.StartNew();
        var rows = Set.Select(s =>
        {
            var words = keywords.Route(s.Message);
            var meaning = classifier.Classify(s.Message)!;
            var both = SemanticAskRouter.Decide(words, meaning, keywords);
            return (s, words, meaning, both);
        }).ToList();
        var msPer = clock.Elapsed.TotalMilliseconds / Set.Length;

        string Pct(Func<(string Message, string Skill, string Lang), AskRoute?, Classified?, AskRoute?, bool> ok, string? lang = null)
        {
            var some = rows.Where(r => lang is null || r.s.Lang == lang).ToList();
            return $"{100.0 * some.Count(r => ok(r.s, r.words, r.meaning, r.both)) / some.Count:0}%";
        }
        _out.WriteLine("| | All | en | fr | es |\n|---|---|---|---|---|");
        foreach (var (name, ok) in new (string, Func<(string Message, string Skill, string Lang), AskRoute?, Classified?, AskRoute?, bool>)[]
        {
            ("Keywords (today)", (s, w, m, b) => w!.SkillId == s.Skill),
            ("Meaning alone", (s, w, m, b) => m!.Label == s.Skill),
            ("Both (SemanticAskRouter)", (s, w, m, b) => b!.SkillId == s.Skill),
        })
            _out.WriteLine($"| {name} | {Pct(ok)} | {Pct(ok, "en")} | {Pct(ok, "fr")} | {Pct(ok, "es")} |");
        _out.WriteLine($"\n{msPer:0.0} ms a message");
        foreach (var r in rows.Where(r => assertBetter && r.both.SkillId != r.s.Skill).Take(40))
            _out.WriteLine($"miss: {r.s.Message} → {r.both.SkillId} (want {r.s.Skill}; words {r.words.SkillId} {r.words.Confidence:0.00}, meaning {r.meaning.Label} {r.meaning.Score:0.00}/{r.meaning.Margin:0.000})");

        // Variants of the classifier itself: centring, and how many nearest examples count.
        foreach (var (center, k) in new[] { (false, 1), (false, 2), (true, 1), (true, 2), (true, 3) })
        {
            var c = new ExampleClassifier(embedder, SkillExamples.All, k, center);
            _out.WriteLine($"variant center={center} k={k}: meaning alone {100.0 * Set.Count(s => c.Classify(s.Message)!.Label == s.Skill) / Set.Length:0.0}%");
        }
        _out.WriteLine("scores now: " + string.Join(", ", rows.Take(5).Select(r => $"{r.meaning.Score:0.00}/{r.meaning.Margin:0.00}")));

        // Threshold sweep, for choosing MinScore and OverrideMargin.
        foreach (var min in new[] { 0f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f })
            foreach (var margin in new[] { 0f, 0.02f, 0.05f, 0.1f })
            {
                var right = rows.Count(r =>
                {
                    var m = r.meaning;
                    AskRoute pick = r.words;
                    if (m.Score >= min && m.Label != r.words.SkillId && (r.words.Confidence < 0.5 || (r.words.Confidence <= 0.65 && m.Margin >= margin)))
                        pick = new AskRoute(m.Label, m.Label, m.Score, "");
                    return pick.SkillId == r.s.Skill;
                });
                _out.WriteLine($"sweep min={min:0.00} margin={margin:0.00}: {100.0 * right / rows.Count:0.0}%");
            }

        var keywordsRight = rows.Count(r => r.words.SkillId == r.s.Skill);
        var bothRight = rows.Count(r => r.both.SkillId == r.s.Skill);
        if (assertBetter)
        {
            bothRight.Should().BeGreaterThan(keywordsRight, "meaning must add something over the keywords");
            // Measured 19 Oct: 73% on the fresh set (keywords alone: 35%). A change that loses ground fails here.
            (100.0 * bothRight / Set.Length).Should().BeGreaterThanOrEqualTo(70, "the fresh set was 73% when this was written");
        }
    }
}
