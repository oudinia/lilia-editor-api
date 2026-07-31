using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// P1.4 — moving equation content from <c>{ latex }</c> to
/// <c>{ notation, source, ast }</c>.
///
/// <para>The property that matters is not that the new shape works. It is that
/// <b>both shapes render identically</b>, on every emitter, so the rename is
/// invisible to anyone holding an already-authored document. That is
/// <see cref="Both_shapes_render_identically_on_every_emitter"/>; the rest of
/// this file is the unit-level detail underneath it.</para>
///
/// <para>Note what is NOT tested: that <c>ast: null</c> gets written. It
/// deliberately is not written — block content is schemaless JSON, so absent
/// and null are indistinguishable and pre-writing nulls reserves nothing.</para>
/// </summary>
public class EquationSourceTests
{
    private static readonly RenderService Render = BuildRenderService();
    private static readonly TypstRenderService TypstRender = BuildTypstRenderService();

    private static RenderService BuildRenderService()
    {
        var opts = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RenderService(new LiliaDbContext(opts), NullLogger<RenderService>.Instance);
    }

    private static TypstRenderService BuildTypstRenderService()
    {
        var opts = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new TypstRenderService(new LiliaDbContext(opts), NullLogger<TypstRenderService>.Instance);
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private static Block EquationBlock(string contentJson) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        Type = "equation",
        SortOrder = 0,
        Content = JsonDocument.Parse(contentJson),
    };

    private const string Formula = @"\mathcal{L} = -\sum_i y_i \log \hat{y}_i";

    /// <summary>
    /// JSON-escapes the formula. Interpolating it raw produces invalid JSON —
    /// `\m` is not a legal escape — which fails as a parse error rather than as
    /// the assertion the test is actually making.
    /// </summary>
    private static string JsonString(string value) => JsonSerializer.Serialize(value);

    // ── The property that matters ─────────────────────────────────────

    [Fact]
    public void Both_shapes_render_identically_on_every_emitter()
    {
        // Same equation, one stored the old way and one the new way. If any
        // emitter disagrees, some already-authored document renders differently
        // after the rename — which is the only way this change can hurt anyone.
        var legacy = EquationBlock($$"""{"latex": {{JsonString(Formula)}}, "mode": "display"}""");
        var renamed = EquationBlock(
            $$"""{"notation":"latex","source": {{JsonString(Formula)}}, "mode": "display"}""");

        Render.RenderBlockToLatex(renamed).Should().Be(Render.RenderBlockToLatex(legacy));
        Render.RenderBlockToHtml(renamed).Should().Be(Render.RenderBlockToHtml(legacy));
        Render.RenderBlockToMarkdown(renamed).Should().Be(Render.RenderBlockToMarkdown(legacy));
        Render.RenderBlockToLml(renamed).Should().Be(Render.RenderBlockToLml(legacy));
        TypstRender.RenderBlockToTypst(renamed).Should().Be(TypstRender.RenderBlockToTypst(legacy));
    }

    [Fact]
    public void The_formula_actually_reaches_the_output()
    {
        // Guards the test above from passing vacuously: two emitters both
        // returning "" would compare equal and prove nothing.
        var renamed = EquationBlock($$"""{"notation":"latex","source": {{JsonString(Formula)}}}""");
        Render.RenderBlockToLatex(renamed).Should().Contain(@"\mathcal{L}");
        Render.RenderBlockToLml(renamed).Should().Contain(@"\mathcal{L}");
    }

    // ── Reading ──────────────────────────────────────────────────────

    [Fact]
    public void Source_wins_over_legacy_latex()
    {
        EquationContent.ReadSource(Json("""{"source":"new","latex":"old"}"""))
            .Should().Be("new");
    }

    [Fact]
    public void Legacy_latex_is_used_when_there_is_no_source()
    {
        EquationContent.ReadSource(Json("""{"latex":"old"}""")).Should().Be("old");
    }

    [Fact]
    public void An_empty_source_falls_through_to_latex()
    {
        // A half-migrated row can carry source:"" beside a real latex. Treating
        // "" as a value would render it as an empty equation — content silently
        // gone, which is the failure this whole plan exists to remove.
        EquationContent.ReadSource(Json("""{"source":"","latex":"old"}""")).Should().Be("old");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"mode":"display"}""")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Missing_or_non_object_content_reads_as_empty(string raw)
    {
        EquationContent.ReadSource(Json(raw)).Should().BeEmpty();
    }

    [Fact]
    public void Notation_defaults_to_latex_when_absent()
    {
        EquationContent.ReadNotation(Json("""{"latex":"x"}""")).Should().Be("latex");
        EquationContent.ReadNotation(Json("""{"notation":"mathml","source":"x"}""")).Should().Be("mathml");
    }

    [Theory]
    [InlineData("""{"source":"x"}""")]
    [InlineData("""{"source":"x","ast":null}""")]
    public void No_ast_is_reported_when_none_is_stored(string raw)
    {
        EquationContent.TryReadAst(Json(raw), out _).Should().BeFalse();
    }

    [Fact]
    public void An_ast_is_reported_once_something_writes_one()
    {
        EquationContent.TryReadAst(Json("""{"source":"x","ast":{"kind":"add"}}"""), out var ast)
            .Should().BeTrue();
        ast.GetProperty("kind").GetString().Should().Be("add");
    }

    // ── Writing ──────────────────────────────────────────────────────

    [Fact]
    public void Normalising_a_legacy_block_adds_source_and_notation()
    {
        using var result = BlockContentNormaliser.Normalise("equation", Json("""{"latex":"E=mc^2"}"""));
        var root = result.RootElement;

        root.GetProperty("source").GetString().Should().Be("E=mc^2");
        root.GetProperty("notation").GetString().Should().Be("latex");
        // Kept in step rather than dropped: readers outside this codebase still
        // expect it. Removing it is a later step.
        root.GetProperty("latex").GetString().Should().Be("E=mc^2");
    }

    [Fact]
    public void Normalising_mirrors_source_back_onto_latex()
    {
        using var result = BlockContentNormaliser.Normalise("equation", Json("""{"source":"E=mc^2"}"""));
        result.RootElement.GetProperty("latex").GetString().Should().Be("E=mc^2");
    }

    [Fact]
    public void Normalising_never_writes_an_ast()
    {
        using var result = BlockContentNormaliser.Normalise("equation", Json("""{"latex":"x"}"""));
        result.RootElement.TryGetProperty("ast", out _).Should().BeFalse(
            "absent and null are indistinguishable in schemaless JSON, so writing "
            + "ast:null reserves nothing that adding the field later would not");
    }

    [Fact]
    public void Normalising_preserves_every_other_property()
    {
        using var result = BlockContentNormaliser.Normalise(
            "equation",
            Json("""{"latex":"x","mode":"align","label":"eq:main","numbered":false}"""));
        var root = result.RootElement;

        root.GetProperty("mode").GetString().Should().Be("align");
        root.GetProperty("label").GetString().Should().Be("eq:main");
        root.GetProperty("numbered").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void An_explicit_notation_is_not_overwritten()
    {
        using var result = BlockContentNormaliser.Normalise(
            "equation", Json("""{"source":"x","notation":"mathml"}"""));
        result.RootElement.GetProperty("notation").GetString().Should().Be("mathml");
    }

    [Theory]
    [InlineData("paragraph", """{"text":"hello"}""")]
    [InlineData("heading", """{"text":"hi","level":1}""")]
    public void Non_equation_blocks_pass_through_untouched(string type, string raw)
    {
        using var result = BlockContentNormaliser.Normalise(type, Json(raw));
        result.RootElement.GetRawText().Should().Be(Json(raw).GetRawText());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"mode":"display"}""")]
    public void An_empty_equation_is_left_alone(string raw)
    {
        // A freshly inserted equation has no source yet. Inventing properties
        // for it would put noise in every new block for no benefit.
        using var result = BlockContentNormaliser.Normalise("equation", Json(raw));
        result.RootElement.TryGetProperty("source", out _).Should().BeFalse();
    }

    [Fact]
    public void Equation_type_is_matched_case_insensitively()
    {
        using var result = BlockContentNormaliser.Normalise("Equation", Json("""{"latex":"x"}"""));
        result.RootElement.TryGetProperty("source", out _).Should().BeTrue();
    }

    [Fact]
    public void Normalising_is_idempotent()
    {
        using var once = BlockContentNormaliser.Normalise("equation", Json("""{"latex":"x"}"""));
        using var twice = BlockContentNormaliser.Normalise("equation", once.RootElement);
        twice.RootElement.GetRawText().Should().Be(once.RootElement.GetRawText());
    }

    [Fact]
    public void Legacy_shape_is_recognised_only_before_normalisation()
    {
        EquationContent.IsLegacyShape(Json("""{"latex":"x"}""")).Should().BeTrue();
        using var normalised = BlockContentNormaliser.Normalise("equation", Json("""{"latex":"x"}"""));
        EquationContent.IsLegacyShape(normalised.RootElement).Should().BeFalse();
    }
}
