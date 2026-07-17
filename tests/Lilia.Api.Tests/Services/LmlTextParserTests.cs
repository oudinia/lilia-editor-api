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
    public void NormalizeProse_CollapsesSoftLineWraps()
    {
        var raw = "Einstein's theory of relativity, comprising Special Relativity (1905) and General\nRelativity (1915), fundamentally reshaped our understanding of space, time, and\ngravity.";
        var got = LmlTextParser.NormalizeProse(raw);
        Assert.DoesNotContain("\n", got);
        Assert.Contains("General Relativity (1915)", got);
        Assert.Contains("space, time, and gravity", got);
    }

    [Fact]
    public void Parse_AbstractSoftWrapsBecomeSpaces()
    {
        var src = """
            @abstract
              Einstein's theory of relativity, comprising Special Relativity (1905) and General
              Relativity (1915), fundamentally reshaped our understanding of space.
            """;
        var result = _parser.Parse(src);
        Assert.Single(result.Blocks);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Blocks[0].Content);
        Assert.DoesNotContain("\\n", json);
        Assert.Contains("General Relativity (1915)", json);
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

    [Fact]
    public void Parse_CvBlocks_PersonalInfoSectionEntry()
    {
        var src = """
            @personalInfo[name="Ada Lovelace", email="ada@example.com", location="London", homepage="https://example.com"]
              Mathematician · Analyst of the Analytical Engine

            @cvSection[title="Experience"]

            @cvEntry[period="1842 – 1843", role="Collaborator", org="Charles Babbage", location="London"]
              Extended notes on the Analytical Engine.

            @cvSection[title="Skills"]

            @list
              - Mathematics
              - Scientific writing
            """;

        var result = _parser.Parse(src);
        Assert.Empty(result.Warnings);
        Assert.Equal(5, result.Blocks.Count);
        Assert.Equal("personalInfo", result.Blocks[0].Type);
        Assert.Equal("cvSection", result.Blocks[1].Type);
        Assert.Equal("cvEntry", result.Blocks[2].Type);
        Assert.Equal("cvSection", result.Blocks[3].Type);
        Assert.Equal("list", result.Blocks[4].Type);
        Assert.Equal("Ada Lovelace", result.Title);

        var info = System.Text.Json.JsonSerializer.Serialize(result.Blocks[0].Content);
        Assert.Contains("Ada Lovelace", info);
        Assert.Contains("ada@example.com", info);
        Assert.Contains("Mathematician", info);
        Assert.Contains("London", info);

        var section = System.Text.Json.JsonSerializer.Serialize(result.Blocks[1].Content);
        Assert.Contains("Experience", section);

        var entry = System.Text.Json.JsonSerializer.Serialize(result.Blocks[2].Content);
        Assert.Contains("Collaborator", entry);
        Assert.Contains("Charles Babbage", entry);
        Assert.Contains("1842", entry);
        Assert.Contains("Analytical Engine", entry);
    }

    [Fact]
    public void Parse_CvAliases_NormalizedToCanonicalTypes()
    {
        var src = """
            @personal-info[name="Test User", email="t@e.com"]
              Headline here

            @cv_section[title="Education"]

            @cv-entry[period="2020", role="B.Sc.", org="Uni"]
              Thesis work.
            """;

        var result = _parser.Parse(src);
        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.Blocks.Count);
        Assert.Equal("personalInfo", result.Blocks[0].Type);
        Assert.Equal("cvSection", result.Blocks[1].Type);
        Assert.Equal("cvEntry", result.Blocks[2].Type);
    }

    [Fact]
    public void Parse_UnknownType_StillFallsBackToParagraph()
    {
        var src = """
            @notARealBlock
              body text
            """;
        var result = _parser.Parse(src);
        Assert.Single(result.Blocks);
        Assert.Equal("paragraph", result.Blocks[0].Type);
        Assert.NotEmpty(result.Warnings);
    }
}
