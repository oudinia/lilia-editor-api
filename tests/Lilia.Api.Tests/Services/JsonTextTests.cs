using FluentAssertions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Pulling the object out of a reply that is mostly an object. The cases that
/// matter are the ones a model actually produces — a preamble sentence, a
/// closing explanation — and the ones it must NOT rescue, where "helpfully"
/// returning something would hand the caller a table that is not the one the
/// model meant.
/// </summary>
public class JsonTextTests
{
    [Fact]
    public void Takes_the_object_when_the_model_talks_first()
    {
        var found = JsonText.FirstObject("""
            Here's the revised table:
            { "block": { "type": "table" }, "note": "Bolded the header." }
            """);
        found.Should().NotBeNull();
        found.Should().StartWith("{").And.EndWith("}");
        found.Should().Contain("Bolded the header.");
    }

    [Fact]
    public void Takes_the_object_when_the_model_talks_afterwards()
    {
        var found = JsonText.FirstObject("""
            { "note": "Done." }
            Let me know if you'd like the columns in a different order.
            """);
        found.Should().Be("""{ "note": "Done." }""");
    }

    [Fact]
    public void Counts_braces_inside_strings_as_text_not_structure()
    {
        // A LaTeX cell is the normal case here, not an edge one: every bolded
        // cell in every table this touches is \textbf{...}.
        var reply = """{ "content": { "headers": ["\\textbf{Ours}"] }, "note": "ok" }""";
        var found = JsonText.FirstObject($"Sure!\n{reply}");
        found.Should().Be(reply);
    }

    [Fact]
    public void Is_not_fooled_by_an_escaped_quote_before_a_brace()
    {
        var reply = """{ "note": "the cell reads \" } \" now" }""";
        JsonText.FirstObject(reply).Should().Be(reply);
    }

    [Fact]
    public void Returns_null_when_the_object_never_closes()
    {
        // Truncated output. Repairing it would invent content the model never
        // produced, which is worse than reporting that the ask failed.
        JsonText.FirstObject("""{ "block": { "type": "table" """).Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_the_braces_balance_but_the_json_does_not()
    {
        JsonText.FirstObject("""{ "block": }""").Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_prose_with_no_object_at_all()
    {
        JsonText.FirstObject("I can't do that with this table.").Should().BeNull();
        JsonText.FirstObject("").Should().BeNull();
        JsonText.FirstObject(null).Should().BeNull();
    }
}
