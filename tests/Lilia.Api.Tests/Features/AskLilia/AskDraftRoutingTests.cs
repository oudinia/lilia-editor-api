using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// Asking for a whole document reaches the architect, whatever parts it lists (9 Nov 2026). The cost
/// experiments found "Draft a short research paper … with one table" on the Table skill: "table" outranked
/// "draft", so the paper was drafted under a table's instructions. A request for one part keeps its skill.
/// </summary>
public class AskDraftRoutingTests
{
    private readonly AskLiliaRouter _router = new();

    [Theory]
    [InlineData("Draft a short research paper (about 2 pages) on sleep and memory: abstract, introduction, method, results with one table, discussion and 4 references.")]
    [InlineData("Draft a full thesis chapter of about 4,000 words, with 2 numbered equations and a results table.")]
    [InlineData("Write an essay on climate adaptation with a figure of sea levels.")]
    [InlineData("Write me a report on the survey, with a table of the answers.")]
    [InlineData("Rédige un article court (2 pages) sur l'effet du sommeil sur la mémoire, avec un tableau et quatre références.")]
    [InlineData("Écris un rapport de stage avec un tableau des tâches.")]
    [InlineData("Redacta un artículo de investigación corto con una tabla y cuatro referencias.")]
    [InlineData("Escribe un informe con un gráfico de ventas.")]
    public void A_whole_document_goes_to_the_architect(string message)
    {
        _router.Route(message).SkillId.Should().Be("lilia-document-architect");
    }

    /// <summary>Growing the paper goes to the architect too (9 Nov): with no keyword these fell to Table, Figure or
    /// Polish by meaning, and the table and figure skills cost 10 to 20 times as much on a long document.</summary>
    [Theory]
    [InlineData("Expand the method to about three times its length, with more detail and examples.")]
    [InlineData("Expand the experiments to about three times its length, with more detail and examples.")]
    [InlineData("Expand the background to about three times its length, with more detail and examples.")]
    [InlineData("Add a section on limitations after the discussion.")]
    [InlineData("Elaborate on the second result.")]
    [InlineData("Développe la discussion avec deux exemples.")]
    [InlineData("Ajoute une section sur les limites.")]
    [InlineData("Amplía la introducción con más contexto.")]
    [InlineData("Añade una sección sobre trabajo futuro.")]
    public void Growing_the_paper_goes_to_the_architect(string message)
    {
        _router.Route(message).SkillId.Should().Be("lilia-document-architect");
    }

    [Theory]
    [InlineData("Expand the table with two more rows", "lilia-table")]
    [InlineData("Write a table for my paper comparing the three methods", "lilia-table")]
    [InlineData("Write the equation for the effect size in my paper", "lilia-equation")]
    [InlineData("Add a table comparing GCN, GAT and MPNN by parameters and accuracy.", "lilia-table")]
    [InlineData("Draw a TikZ diagram of a neural network", "lilia-figure")]
    [InlineData("Rewrite the abstract of my paper in 120 words", "lilia-polish")]
    [InlineData("Format this paper for IEEE, two-column", "lilia-journal")]
    [InlineData("Transforme ces données en tableau booktabs", "lilia-table")]
    public void One_part_keeps_its_skill(string message, string skill)
    {
        _router.Route(message).SkillId.Should().Be(skill);
    }
}
