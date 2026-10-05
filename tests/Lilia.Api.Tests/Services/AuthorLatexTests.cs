using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>The author string goes into \author{} as written (the user's decision, 5 Oct), with plain-text specials escaped.</summary>
public class AuthorLatexTests
{
    private static string Escape(string s) => "ESCAPED(" + s + ")";

    [Theory]
    [InlineData(@"Jane Doe\thanks{Funded by \textit{X}.}\\U. of South \and John Doe", @"Jane Doe\thanks{Funded by \textit{X}.}\\U. of South \and John Doe")]
    [InlineData(@"Jane\orcidlink{0000-0001-2345-6789}", @"Jane\orcidlink{0000-0001-2345-6789}")]
    [InlineData(@"\IEEEauthorblockN{Jane}\IEEEauthorblockA{Uni}", @"\IEEEauthorblockN{Jane}\IEEEauthorblockA{Uni}")]
    [InlineData(@"J.~Doe$^{1}$", @"J.~Doe$^{1}$")]
    [InlineData(@"Kurt G\""odel", @"Kurt G\""odel")]
    public void Commands_pass_as_written(string input, string expected) =>
        AuthorLatex.For(input, Escape).Should().Be(expected);

    [Theory]
    [InlineData("R&D Lab", @"R\&D Lab")]
    [InlineData("jane_doe@x.org", @"jane\_doe@x.org")]
    [InlineData(@"Equal contribution\thanks{50% each}", @"Equal contribution\thanks{50\% each}")]
    [InlineData("Team #1", @"Team \#1")]
    [InlineData(@"already\_escaped \& fine", @"already\_escaped \& fine")]
    public void Plain_text_specials_are_escaped(string input, string expected) =>
        AuthorLatex.For(input, Escape).Should().Be(expected);

    [Fact]
    public void Unbalanced_braces_fall_back_to_escaping_everything() =>
        AuthorLatex.For(@"Jane\thanks{oops", Escape).Should().Be(@"ESCAPED(Jane\thanks{oops)");

    [Fact]
    public void Empty_stays_empty() => AuthorLatex.For("  ", Escape).Should().Be("");
}
