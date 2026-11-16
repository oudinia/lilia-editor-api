using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// MCP-58: CV field names an app guesses were stored and printed nothing (firstName/lastName,
/// organization, startDate; LML startDate stored period ""), and highlight / tech never printed.
/// </summary>
public class CvFieldsTests
{
    private readonly RenderService _render = new(null!, NullLogger<RenderService>.Instance);

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o);

    private static Block B(string type, JsonElement content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(content.GetRawText()),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    // The launch bot's #119 (SixtySecondsCV): the sidebar's GitHub and Overleaf links printed as plain text; only the
    // homepage was a link. A link is clickable now; a handle stays text.
    [Fact]
    public void A_social_link_is_clickable_and_a_handle_stays_text()
    {
        var latex = _render.RenderBlockToLatex(B("personalInfo", Json(new
        {
            name = "Raging Bull",
            socials = new object[]
            {
                new { network = "github", handle = "https://github.com/LaGuer/SixtySecondsCV" },
                new { network = "twitter", handle = "@sparrow_x" },
            },
        })));
        latex.Should().Contain(@"\url{https://github.com/LaGuer/SixtySecondsCV}");
        latex.Should().Contain(@"@sparrow\_x").And.NotContain(@"\url{@sparrow");
    }

    // The launch bot's #119: "($\varnothing\, 1,0$)" in an entry's title stayed raw LaTeX (a CV field is plain text).
    // Maths made only of symbols becomes what it prints; anything else is left as written.
    [Theory]
    [InlineData(@"Master Theses ($\varnothing\, 1,0$)", "Master Theses (∅ 1,0)")]
    [InlineData(@"Grade $\alpha$ and $\leq 3$", "Grade α and ≤ 3")]
    [InlineData(@"Score $x^2$", @"Score $x^2$")]
    [InlineData(@"Cost \$5 to \$7", @"Cost \$5 to \$7")]
    [InlineData(@"Odd $\notacommand$", @"Odd $\notacommand$")]
    public void Symbol_maths_in_a_CV_field_becomes_its_characters(string field, string expected) =>
        ModernCv.Clean(field).Should().Be(expected);

    [Fact]
    public void Guessed_entry_fields_become_lilias()
    {
        var c = CvFields.Normalize("cvEntry", Json(new { title = "Engineer", organization = "Acme", startDate = "2020", endDate = "2024", city = "Lyon", tech = "C#, SQL" }));
        c.GetProperty("role").GetString().Should().Be("Engineer");
        c.GetProperty("org").GetString().Should().Be("Acme");
        c.GetProperty("period").GetString().Should().Be("2020 – 2024");
        c.GetProperty("location").GetString().Should().Be("Lyon");
        c.GetProperty("tech").EnumerateArray().Select(t => t.GetString()).Should().Equal("C#", "SQL");
        c.TryGetProperty("organization", out _).Should().BeFalse("a used alias is removed");
    }

    [Fact]
    public void Lilias_own_field_wins_and_other_types_pass_through()
    {
        var c = CvFields.Normalize("cvEntry", Json(new { org = "Lilia", organization = "Other", period = "2019", startDate = "2020" }));
        c.GetProperty("org").GetString().Should().Be("Lilia");
        c.GetProperty("period").GetString().Should().Be("2019");
        var p = Json(new { text = "x", title = "y" });
        CvFields.Normalize("paragraph", p).GetRawText().Should().Be(p.GetRawText());
    }

    [Fact]
    public void Guessed_person_fields_become_lilias()
    {
        var c = CvFields.Normalize("personalInfo", Json(new { firstName = "Ada", lastName = "Lovelace", jobTitle = "Analyst", phone = "+44 1", website = "https://ada.example", address = "London" }));
        c.GetProperty("name").GetString().Should().Be("Ada Lovelace");
        c.GetProperty("headline").GetString().Should().Be("Analyst");
        c.GetProperty("phones")[0].GetProperty("number").GetString().Should().Be("+44 1");
        c.GetProperty("homepage").GetString().Should().Be("https://ada.example");
        c.GetProperty("location").GetString().Should().Be("London");
        CvFields.Normalize("personalInfo", Json(new { name = "A", phones = new[] { "1", "2" } }))
            .GetProperty("phones")[1].GetProperty("number").GetString().Should().Be("2");
    }

    [Fact]
    public void Lml_start_and_end_dates_make_the_period()
    {
        var r = new LmlTextParser().Parse("@cvEntry[role=Engineer, company=Acme, startDate=2020, endDate=2024, tech=\"Go, Rust\"]\n  Built things.");
        var c = JsonSerializer.SerializeToElement(r.Blocks.Single(b => b.Type == "cvEntry").Content);
        c.GetProperty("period").GetString().Should().Be("2020 – 2024");
        c.GetProperty("org").GetString().Should().Be("Acme");
        c.GetProperty("tech").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Highlight_and_tech_print()
    {
        var block = B("cvEntry", Json(new { role = "Engineer", org = "Acme", period = "2020 – 2024", highlight = "Led the move", description = "Built things.", tech = new[] { "C#", "SQL" } }));
        var tex = _render.RenderBlockToLatex(block);
        tex.Should().Contain("Led the move").And.Contain("C\\#, SQL");
        var html = _render.RenderBlockToHtml(block);
        html.Should().Contain("Led the move").And.Contain("C#, SQL");
    }
}
