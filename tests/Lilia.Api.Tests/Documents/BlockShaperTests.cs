using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Documents;
using Lilia.Core.Entities;
using Xunit;

namespace Lilia.Api.Tests.Documents;

/// <summary>
/// The admin console's view of a block (block_shapes): every block type described, and nothing an
/// author wrote ever in the shape or the preview.
/// </summary>
public class BlockShaperTests
{
    // Every text-like key the editor's blocks use, each holding a secret the shape must not carry.
    private static JsonElement SecretContent(string tag) => JsonDocument.Parse($$"""
        {
          "text": "SECRET-{{tag}}-text and more words here", "title": "SECRET-{{tag}}-title", "subtitle": "SECRET-{{tag}}-sub",
          "caption": "SECRET-{{tag}}-caption", "source": "\\frac{SECRET-{{tag}}}{x}", "code": "print('SECRET-{{tag}}')\nline two",
          "name": "SECRET-{{tag}}-name", "email": "secret-{{tag}}@example.com", "url": "https://secret-{{tag}}.example.com",
          "items": ["SECRET-{{tag}}-item-1", "SECRET-{{tag}}-item-2"], "headers": ["SECRET-{{tag}}-h1", "h2"],
          "rows": [["SECRET-{{tag}}-r1c1", "r1c2"], ["r2c1", "r2c2"]], "entries": [{ "title": "SECRET-{{tag}}-entry" }],
          "authors": [{ "name": "SECRET-{{tag}}-author" }], "address": "SECRET-{{tag}}-street", "amount": "SECRET-{{tag}}-1200",
          "level": 2, "language": "python", "variant": "note", "kind": "theorem", "columns": 2
        }
        """).RootElement;

    [Fact]
    public void Every_block_type_in_the_editor_has_a_description_of_its_own()
    {
        BlockShaper.DescribedTypes.Should().Contain(BlockTypes.All,
            "a new block type needs its own shape here, or the console would describe it generically");
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void No_block_type_lets_a_word_of_the_author_into_its_shape_or_preview(string type)
    {
        var info = BlockShaper.Shape(type, SecretContent(type), Guid.NewGuid());
        (info.Shape + " " + info.Preview).Should().NotContainEquivalentOf("SECRET")
            .And.NotContain("example.com").And.NotContain("print(").And.NotContain("\\frac");
        info.Words.Should().BeGreaterThan(0, "the sizes are still counted");
    }

    public static IEnumerable<object[]> AllTypes() => BlockTypes.All.Select(t => new object[] { t });

    [Fact]
    public void Prose_becomes_lorem_ipsum_of_the_same_length_and_the_same_every_time()
    {
        var id = Guid.NewGuid();
        var content = JsonDocument.Parse("""{"text":"one two three four five six seven"}""").RootElement;
        var a = BlockShaper.Shape(BlockTypes.Paragraph, content, id);
        a.Shape.Should().Be("Paragraph · 7 words");
        a.Preview!.Split(' ').Should().HaveCount(7);
        BlockShaper.Shape(BlockTypes.Paragraph, content, id).Preview.Should().Be(a.Preview);
    }

    [Fact]
    public void Formulas_code_and_tables_show_only_their_size_and_layout()
    {
        BlockShaper.Shape(BlockTypes.Equation, JsonDocument.Parse("""{"source":"E = mc^2"}""").RootElement, Guid.NewGuid())
            .Should().Match<BlockShapeInfo>(s => s.Shape == "Formula · only the author edits it · 8 characters" && s.Preview == null);
        BlockShaper.Shape(BlockTypes.Code, JsonDocument.Parse("""{"code":"a\nb\nc","language":"python"}""").RootElement, Guid.NewGuid())
            .Shape.Should().Be("Code · python · 3 lines");
        BlockShaper.Shape(BlockTypes.Table, JsonDocument.Parse("""{"headers":["a","b","c"],"rows":[["1","2","3"],["4","5","6"]]}""").RootElement, Guid.NewGuid())
            .Shape.Should().StartWith("Table · 2 × 3");
        BlockShaper.Shape(BlockTypes.Heading, JsonDocument.Parse("""{"text":"Intro","level":2}""").RootElement, Guid.NewGuid())
            .Shape.Should().Be("Heading · level 2 · 1 words");
    }

    [Theory]
    [InlineData("Undefined control sequence.\nl.12 My secret sentence \\badmacro", "Undefined control sequence \\badmacro")]
    [InlineData("Missing $ inserted.\nl.3 x^2 of my thesis", "Missing $ (maths outside maths mode)")]
    [InlineData("LaTeX Error: File `secret-name.sty' not found.", "A file or package is not found")]
    [InlineData("Something else that quotes: my private words", "error (the message quotes the text, so it is hidden)")]
    public void An_error_message_is_reduced_to_a_kind(string message, string kind)
    {
        BlockShaper.ErrorKind(message).Should().Be(kind);
    }
}
