using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.AI;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// The notes the server adds to a reply follow the author's language (9 Nov 2026: the launch bot's French session saw
/// them in English), like the compile verdict already did.
/// </summary>
public class AskReplyNotesLanguageTests
{
    [Theory]
    [InlineData("fr", "Application partielle", "Réponse coupée", "C'est fait : 3 bloc(s) mis à jour.")]
    [InlineData("es", "Aplicación parcial", "Respuesta cortada", "Hecho: 3 bloque(s) actualizado(s).")]
    [InlineData("en", "Partial apply", "Cut off", "Done — updated 3 block(s).")]
    [InlineData(null, "Partial apply", "Cut off", "Done — updated 3 block(s).")]
    public void Each_note_is_in_the_authors_language(string? lang, string partial, string cut, string done)
    {
        AskLiliaService.PartialApplyNote(lang).Should().Contain(partial);
        AskLiliaService.CutOffNote(lang).Should().Contain(cut);
        AskLiliaService.WithCutOffNote("Half a chapt", ChatFinishReason.Length, lang).Should().Contain(cut);
        AskLiliaService.DoneNote(lang, 3).Should().Be(done);
    }

    [Fact]
    public void A_kind_change_alone_says_so() =>
        AskLiliaService.DoneNote("fr", null).Should().Be("C'est fait : type et classe du document mis à jour.");
}
