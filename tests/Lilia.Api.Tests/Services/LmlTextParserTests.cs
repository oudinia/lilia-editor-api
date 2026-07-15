using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

public class LmlTextParserTests
{
    private readonly LmlTextParser _parser = new();

    [Fact]
    public void LooksLikeTextLml_DetectsAtBlocks()
    {
        Assert.True(_parser.LooksLikeTextLml("@paragraph\n  Hello"));
        Assert.False(_parser.LooksLikeTextLml("{\"document\":{\"blocks\":[]}}"));
        Assert.False(_parser.LooksLikeTextLml("plain prose only"));
    }

    [Fact]
    public void Parse_SimpleHeadingAndParagraph()
    {
        var src = """
            @heading[level=1]
              Introduction

            @paragraph
              Hello world.
            """;

        var result = _parser.Parse(src);
        Assert.Equal(2, result.Blocks.Count);
        Assert.Equal("heading", result.Blocks[0].Type);
        Assert.Equal("paragraph", result.Blocks[1].Type);
        Assert.Equal("Introduction", result.Title);

        var headingJson = System.Text.Json.JsonSerializer.Serialize(result.Blocks[0].Content);
        Assert.Contains("Introduction", headingJson);
        Assert.Contains("\"level\":1", headingJson);
    }

    [Fact]
    public void Parse_TheoremWithPositionalTypeAndQuotedTitle()
    {
        var src = """
            @theorem[theorem, title="Pythagorean Theorem", label="thm:pythagoras"]
              Let $\triangle ABC$ be a right-angled triangle.
            """;

        var result = _parser.Parse(src);
        Assert.Single(result.Blocks);
        Assert.Equal("theorem", result.Blocks[0].Type);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Blocks[0].Content);
        Assert.Contains("Pythagorean Theorem", json);
        Assert.Contains("thm:pythagoras", json);
        Assert.Contains("theorem", json);
        Assert.Contains("triangle", json);
    }

    [Fact]
    public void Parse_EquationDisplayMode()
    {
        var src = """
            @equation[mode=display, label="eq:norm"]
              \|\mathbf{v}\|^2 = \sum_{i=1}^{n} v_i^2.
            """;

        var result = _parser.Parse(src);
        Assert.Single(result.Blocks);
        Assert.Equal("equation", result.Blocks[0].Type);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Blocks[0].Content);
        Assert.Contains("eq:norm", json);
        Assert.Contains("display", json);
        Assert.Contains("mathbf", json);
    }

    [Fact]
    public void Parse_TableWithCaption()
    {
        var src = """
            @table[caption="Selected Pythagorean triples", label="tab:triples"]
              | a | b | c |
              |---|---|---|
              | 3 | 4 | 5 |
              | 5 | 12 | 13 |
            """;

        var result = _parser.Parse(src);
        Assert.Single(result.Blocks);
        Assert.Equal("table", result.Blocks[0].Type);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Blocks[0].Content);
        Assert.Contains("Selected Pythagorean triples", json);
        Assert.Contains("\"3\"", json);
        Assert.Contains("\"13\"", json);
    }

    [Fact]
    public void Parse_PythagoreanArticle_ProducesManyBlocks()
    {
        var src = """
            @abstract
              The Pythagorean theorem is one of the most celebrated results.

            @heading[level=1, id=intro]
              Introduction

            @paragraph
              Few results in mathematics are as instantly recognisable.

            @heading[level=1, id=statement]
              Statement of the Theorem

            @theorem[theorem, title="Pythagorean Theorem", label="thm:pythagoras"]
              Let $\triangle ABC$ be a right-angled triangle with legs $a$, $b$ and
              hypotenuse $c$. Then
              \[
                a^2 + b^2 = c^2.
              \]

            @heading[level=1, id=proof]
              Proof

            @theorem[proof]
              Construct a large square of side $a + b$.

            @equation[mode=display, label="eq:norm"]
              \|\mathbf{v}\|^2 = v_1^2 + v_2^2.

            @table[caption="Selected Pythagorean triples", label="tab:triples"]
              | a | b | c | m | n |
              |---|---|---|---|---|
              | 3 | 4 | 5 | 2 | 1 |
              | 5 | 12 | 13 | 3 | 2 |

            @heading[level=1]
              References

            @bibliography
              @cite[euclid-elements] Euclid — Elements, Book I.
              @cite[placeholder2] Author, A. — A Modern Geometry Textbook.
            """;

        var result = _parser.Parse(src);
        Assert.True(result.Blocks.Count >= 10, $"Expected >= 10 blocks, got {result.Blocks.Count}");
        Assert.Equal("abstract", result.Blocks[0].Type);
        Assert.Contains(result.Blocks, b => b.Type == "heading");
        Assert.Contains(result.Blocks, b => b.Type == "theorem");
        Assert.Contains(result.Blocks, b => b.Type == "equation");
        Assert.Contains(result.Blocks, b => b.Type == "table");
        Assert.Contains(result.Blocks, b => b.Type == "bibliography");
        Assert.Equal("Introduction", result.Title);
    }

    [Fact]
    public void ParseAttributes_PositionalAndQuoted()
    {
        var attrs = LmlTextParser.ParseAttributes(
            """[theorem, title="Pythagorean Theorem", label="thm:pythagoras"]""");

        Assert.Equal("theorem", attrs["_positional0"]);
        Assert.Equal("Pythagorean Theorem", attrs["title"]);
        Assert.Equal("thm:pythagoras", attrs["label"]);
    }
}
