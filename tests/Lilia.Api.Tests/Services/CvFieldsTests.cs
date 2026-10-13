using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

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
