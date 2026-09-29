using FluentAssertions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Services;

public class TitleAuthorsTests
{
    internal const string Example =
        @"Jane Doe\thanks{I am thankful for the funding provided by Institution.}\\U. of South \and John Doe\thanks{E-mail: jdoe@unorth.edu}\\U. of North";

    [Fact]
    public void The_two_author_example_is_split_with_lines_notes_and_symbols()
    {
        var a = TitleAuthors.Parse(Example);

        a.Should().HaveCount(2);
        a[0].Name.Should().Be("Jane Doe");
        a[0].Lines.Should().Equal("U. of South");
        a[0].Notes.Should().Equal("I am thankful for the funding provided by Institution.");
        a[0].NoteSymbols.Should().Equal("*");
        a[1].Name.Should().Be("John Doe");
        a[1].Lines.Should().Equal("U. of North");
        a[1].Notes.Should().Equal("E-mail: jdoe@unorth.edu");
        a[1].NoteSymbols.Should().Equal("†");
    }

    [Fact]
    public void An_and_inside_braces_does_not_split()
    {
        var a = TitleAuthors.Parse(@"Jane {Doe \and Co}\thanks{One \and two}");
        a.Should().HaveCount(1);
        a[0].Name.Should().Be("Jane Doe Co");
        a[0].Notes.Should().Equal("One two");
    }

    [Fact]
    public void Nested_braces_in_thanks_are_balanced()
    {
        var a = TitleAuthors.Parse(@"Ada\thanks{Supported by {\bf NSF} grant {12{3}}.} \and Bob");
        a.Should().HaveCount(2);
        a[0].Notes.Should().Equal("Supported by NSF grant 123.");
        a[1].Name.Should().Be("Bob");
    }

    [Fact]
    public void No_and_is_one_author_whose_first_line_is_the_name()
    {
        var a = TitleAuthors.Parse(@"Ada Lovelace \\ Analytical Engine Co.");
        a.Should().HaveCount(1);
        a[0].Name.Should().Be("Ada Lovelace");
        a[0].Lines.Should().Equal("Analytical Engine Co.");
        a[0].Notes.Should().BeEmpty();

        TitleAuthors.Parse("Ada Lovelace")[0].Name.Should().Be("Ada Lovelace");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@" \and  \and ")]
    public void Empty_input_gives_no_authors(string? s) =>
        TitleAuthors.Parse(s).Should().BeEmpty();

    [Fact]
    public void Three_authors_and_unknown_commands()
    {
        var a = TitleAuthors.Parse(@"A \and \textbf{X} B \\ \small Lab \and C\\Uni\\City");
        a.Select(x => x.Name).Should().Equal("A", "B", "C");
        a[1].Lines.Should().Equal("Lab");
        a[2].Lines.Should().Equal("Uni", "City");
    }

    [Fact]
    public void Escaped_characters_are_resolved()
    {
        TitleAuthors.Parse(@"Smith \& Sons \\ 50\% owned")[0].Lines.Should().Equal("50% owned");
    }

    [Fact]
    public void Notes_follow_the_LaTeX_symbol_sequence_and_go_numeric_past_nine()
    {
        var text = string.Join(@" \and ", Enumerable.Range(1, 11).Select(i => $@"P{i}\thanks{{N{i}}}"));
        var a = TitleAuthors.Parse(text);
        a.Select(x => x.NoteSymbols[0]).Should().Equal(
            "*", "†", "‡", "§", "¶", "‖", "**", "††", "‡‡", "10", "11");
    }

    [Fact]
    public void Two_notes_on_one_author_take_consecutive_symbols()
    {
        var a = TitleAuthors.Parse(@"A\thanks{x}\thanks{y} \and B\thanks{z}");
        a[0].NoteSymbols.Should().Equal("*", "†");
        a[1].NoteSymbols.Should().Equal("‡");
    }
}
