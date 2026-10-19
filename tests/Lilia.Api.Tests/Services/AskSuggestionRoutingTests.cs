using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The suggestions Ask Lilia offers in the editor (web: AskLiliaPanel's SUGGESTIONS
/// and READER_SUGGESTIONS) each land on the skill they stand for. The user, 11 Oct:
/// the suggestions were "not adapted to Lilia's skills" ("Summarise this document"
/// went to the document architect, which drafts). Change both lists together.
/// </summary>
public class AskSuggestionRoutingTests
{
    private readonly AskLiliaRouter _router = new();

    [Theory]
    [InlineData("Draft an outline: sections with one line each", "lilia-document-architect")]
    [InlineData("Cite a paper from its DOI or arXiv id", "lilia-citations")]
    [InlineData("Why won't this compile? Fix this error", "lilia-compile-doctor")]
    [InlineData("Write the equation for…", "lilia-equation")]
    [InlineData("Turn this data into a booktabs table", "lilia-table")]
    [InlineData("Draw a TikZ diagram of…", "lilia-figure")]
    [InlineData("Polish the selected paragraph", "lilia-polish")]
    [InlineData("Format for IEEE, two-column", "lilia-journal")]
    // A reader (view only): questions, answered by the tutor.
    [InlineData("Explain this document in five lines", "lilia-tutor")]
    [InlineData("Explain the main result in plain words", "lilia-tutor")]
    [InlineData("What is each equation saying?", "lilia-tutor")]
    [InlineData("Explain which sources it relies on", "lilia-tutor")]
    public void Each_suggestion_reaches_its_skill(string suggestion, string skill)
    {
        _router.Route(suggestion).SkillId.Should().Be(skill);
    }

    /// <summary>The same suggestions in French and Spanish (web: mobile and editor locales).</summary>
    [Theory]
    [InlineData("Rédige un plan : des sections d’une ligne chacune", "lilia-document-architect")]
    [InlineData("Cite un article à partir de son DOI ou de son identifiant arXiv", "lilia-citations")]
    [InlineData("Pourquoi ça ne compile pas ? Corrige cette erreur", "lilia-compile-doctor")]
    [InlineData("Écris l’équation de…", "lilia-equation")]
    [InlineData("Transforme ces données en tableau booktabs", "lilia-table")]
    [InlineData("Dessine un diagramme TikZ de…", "lilia-figure")]
    [InlineData("Améliore le paragraphe sélectionné", "lilia-polish")]
    [InlineData("Mets en forme pour IEEE, sur deux colonnes", "lilia-journal")]
    [InlineData("Explique ce document en cinq lignes", "lilia-tutor")]
    [InlineData("Explique le résultat principal simplement", "lilia-tutor")]
    [InlineData("Que dit chaque équation ?", "lilia-tutor")]
    [InlineData("Explique sur quelles sources il s’appuie", "lilia-tutor")]
    [InlineData("Redacta un esquema: secciones de una línea cada una", "lilia-document-architect")]
    [InlineData("Cita un artículo a partir de su DOI o su id de arXiv", "lilia-citations")]
    [InlineData("¿Por qué no compila? Corrige este error", "lilia-compile-doctor")]
    [InlineData("Escribe la ecuación de…", "lilia-equation")]
    [InlineData("Convierte estos datos en una tabla booktabs", "lilia-table")]
    [InlineData("Dibuja un diagrama TikZ de…", "lilia-figure")]
    [InlineData("Mejora el párrafo seleccionado", "lilia-polish")]
    [InlineData("Formato para IEEE, a dos columnas", "lilia-journal")]
    [InlineData("Explica este documento en cinco líneas", "lilia-tutor")]
    [InlineData("Explica el resultado principal en palabras sencillas", "lilia-tutor")]
    [InlineData("¿Qué dice cada ecuación?", "lilia-tutor")]
    [InlineData("Explica en qué fuentes se apoya", "lilia-tutor")]
    public void Each_translated_suggestion_reaches_its_skill(string suggestion, string skill)
    {
        _router.Route(suggestion).SkillId.Should().Be(skill);
    }

    /// <summary>Asking for a fix that compiles reaches the compile doctor and its repair loop (15 Oct).</summary>
    [Theory]
    [InlineData("Make it compile")]
    [InlineData("It fails to compile, fix the errors")]
    [InlineData("My document does not compile")]
    [InlineData("Fais-le compiler, il ne compile plus")]
    [InlineData("Haz que compile")]
    public void Asking_for_a_fix_that_compiles_reaches_the_compile_doctor(string message)
    {
        _router.Route(message).SkillId.Should().Be("lilia-compile-doctor");
    }

    /// <summary>Questions about the app itself reach the tutor and its step-by-step guide.</summary>
    [Theory]
    [InlineData("How do I switch to dark mode?")]
    [InlineData("Where is the language setting?")]
    [InlineData("How do I share this with a colleague?")]
    [InlineData("How do I validate my document?")]
    [InlineData("Which themes do you offer?")]
    [InlineData("Où sont les paramètres du thème ?")]
    [InlineData("Comment faire pour partager ce document ?")]
    [InlineData("¿Dónde está el modo oscuro?")]
    [InlineData("¿Cómo cambio el idioma?")]
    [InlineData("How do I delete my account?")]
    [InlineData("How many documents can I have on the free plan?")]
    [InlineData("Can I export everything I wrote?")]
    [InlineData("Comment supprimer mon compte ?")]
    [InlineData("Combien de documents avec l'offre gratuite ?")]
    [InlineData("¿Cómo restauro mi cuenta?")]
    [InlineData("Quiero exportar todo")]
    public void App_questions_reach_the_tutor(string question)
    {
        _router.Route(question).SkillId.Should().Be("lilia-tutor");
    }

    [Fact]
    public void The_tutor_knows_the_app_step_by_step()
    {
        var guide = AiSkillGuidance.Get("lilia-tutor");
        guide.Should().Contain("Lilia, step by step");
        foreach (var fact in new[] { "Validate now", "Cerulean", "Exposition", "Light style", "Español", "Anyone with the link", "⌘K", "Save a version",
                     // the glossary: what a French or Spanish user sees on screen
                     "Valider maintenant", "Prune", "Paramètres", "Validar ahora", "Ciruela", "Ajustes",
                     // Settings, plans and the account (14–15 Oct)
                     "Export everything", "Restore my account", "Free · 2 of 3 documents", "Connected apps",
                     "Tout exporter", "Restaurer mon compte", "Exportar todo", "Restaurar mi cuenta" })
            guide.Should().Contain(fact);
    }

    [Fact]
    public void Ask_Lilia_replies_in_the_authors_language()
    {
        AskLiliaService.LanguageNote.Should().Contain("French").And.Contain("Spanish").And.Contain("Keep LaTeX");
    }

    /// <summary>Challenge (19 Oct): the suggestions and the ways people ask to be questioned.</summary>
    [Theory]
    [InlineData("Rehearse my defence: ask me what a jury would")]
    [InlineData("Be Reviewer 2 for this paper")]
    [InlineData("Quiz me on this document")]
    [InlineData("Grill me on chapter 3")]
    [InlineData("Répète ma soutenance avec moi : pose-moi les questions du jury")]
    [InlineData("Joue le relecteur 2 pour cet article")]
    [InlineData("Interroge-moi sur ce document")]
    [InlineData("Ensaya mi defensa: hazme las preguntas del tribunal")]
    [InlineData("Haz de revisor 2 para este artículo")]
    [InlineData("Examíname sobre este documento")]
    public void Asking_to_be_questioned_reaches_challenge(string message) =>
        _router.Route(message).SkillId.Should().Be("lilia-challenge");
}
