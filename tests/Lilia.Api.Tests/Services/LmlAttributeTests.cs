using System.Text.Json;
using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// LML attributes, both directions.
///
/// <para>The exporter has always written <c>[key=val][key2=val2]</c> — one
/// bracket group per attribute. The parser's regex accepted exactly one group,
/// so any directive with more than one attribute matched nothing, fell through
/// to the default, and became a <b>paragraph containing its own raw
/// markup</b>:</para>
///
/// <code>{"type":"paragraph","content":{"text":"@title[title=A Paper][author=E. Author][date=\today]"}}</code>
///
/// <para>This parser backs Ask Lilia's <c>apply_lml</c>, the tool it uses to
/// rewrite a whole document. So asking Lilia to restructure a paper replaced
/// its title block with a line of visible syntax, and the title, author and
/// date stopped being structure (2026-09-10).</para>
///
/// <para>The second half was quieter: an unquoted value is read only as far as
/// the first comma, so "Statement, Proofs, and Generalizations" came back as
/// "Statement". The exporter now quotes anything the reader could not
/// otherwise recover.</para>
/// </summary>
public class LmlAttributeTests
{
    private static LmlParsedBlock[] Parse(string lml) =>
        [.. new LmlTextParser().Parse(lml).Blocks];

    private static string ContentOf(LmlParsedBlock block) =>
        JsonSerializer.Serialize(block.Content);

    // ── the directive that started it ────────────────────────────────────

    [Fact]
    public void ATitleDirectiveBecomesATitleBlock()
    {
        var blocks = Parse("@title[title=A Paper][author=E. Author][date=2026]");

        blocks.Should().HaveCount(1);
        blocks[0].Type.Should().Be("title", "not a paragraph full of raw markup");

        var content = ContentOf(blocks[0]);
        content.Should().Contain("A Paper");
        content.Should().Contain("E. Author", "the author is the point of the block");
        content.Should().Contain("2026");
    }

    [Fact]
    public void ATitleDirectiveDoesNotLeakItsOwnSyntax()
    {
        var blocks = Parse("@title[title=A Paper][author=E. Author][date=2026]");

        ContentOf(blocks[0]).Should().NotContain("@title");
        ContentOf(blocks[0]).Should().NotContain("[author=");
    }

    // ── multiple attribute groups, generally ─────────────────────────────

    [Fact]
    public void EveryAttributeGroupIsRead()
    {
        var attrs = LmlTextParser.ParseAttributes("[src=a.png][alt=An image][width=0.5]");

        attrs.Should().ContainKey("src").WhoseValue.Should().Be("a.png");
        attrs.Should().ContainKey("alt").WhoseValue.Should().Be("An image");
        attrs.Should().ContainKey("width").WhoseValue.Should().Be("0.5");
    }

    [Fact]
    public void ASingleGroupStillWorks()
    {
        // The form hand-written LML uses; it must not regress.
        var attrs = LmlTextParser.ParseAttributes("[caption=A caption, with a comma]");

        attrs.Should().ContainKey("caption");
    }

    [Fact]
    public void CommasInOneGroupDoNotLeakIntoTheNext()
    {
        var attrs = LmlTextParser.ParseAttributes("""[title="A, B"][author=C]""");

        attrs["title"].Should().Be("A, B");
        attrs["author"].Should().Be("C", "the second group is its own scope");
    }

    [Fact]
    public void NoAttributesAtAllIsFine()
    {
        LmlTextParser.ParseAttributes(null).Should().BeEmpty();
        LmlTextParser.ParseAttributes("").Should().BeEmpty();
        LmlTextParser.ParseAttributes("[]").Should().BeEmpty();
    }

    // ── the quoting the exporter now does ────────────────────────────────

    [Fact]
    public void ATitleContainingCommasSurvives()
    {
        // The real one: "On the Pythagorean Theorem: Statement, Proofs, and
        // Generalizations" used to come back as "Statement".
        const string real = "On the Pythagorean Theorem: Statement, Proofs, and Generalizations";
        var attrs = LmlTextParser.ParseAttributes($"""[title="{real}"][author=E. Author]""");

        attrs["title"].Should().Be(real);
        attrs["author"].Should().Be("E. Author");
    }

    [Fact]
    public void AQuotedValueMayContainBrackets()
    {
        var attrs = LmlTextParser.ParseAttributes("""[caption="Figure [3] revisited"]""");

        attrs["caption"].Should().Contain("revisited");
    }

    // ── everything else keeps working ────────────────────────────────────

    [Fact]
    public void OtherDirectivesAreUnaffected()
    {
        var blocks = Parse("""
            @abstract
            An abstract.

            # A heading

            A plain paragraph.
            """);

        blocks.Select(b => b.Type).Should().Contain(["abstract", "heading", "paragraph"]);
    }

    [Fact]
    public void AnUnknownDirectiveStillDegradesToAParagraph()
    {
        // Degrading is correct — losing the text would not be.
        var blocks = Parse("@somethingnobodyhasimplemented[a=1] with text");

        blocks.Should().NotBeEmpty();
        ContentOf(blocks[0]).Should().Contain("text");
    }

    [Fact]
    public void ADocumentHeaderStillSetsTheTitle()
    {
        var result = new LmlTextParser().Parse("""
            @document
            title: From The Header

            A paragraph.
            """);

        result.Title.Should().Be("From The Header");
    }
}
