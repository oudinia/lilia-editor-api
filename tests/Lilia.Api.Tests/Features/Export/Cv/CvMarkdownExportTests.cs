using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Export.Cv;

/// <summary>
/// The document sweep (15 Nov 2026): a CV exported to Markdown was "Unsupported block type" comments, the person's
/// name and every entry gone. Each CV block now writes what the HTML export shows.
/// </summary>
public class CvMarkdownExportTests
{
    private static readonly RenderService Render = new(
        new LiliaDbContext(new DbContextOptionsBuilder<LiliaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options),
        NullLogger<RenderService>.Instance);

    private static string Md(string type, object content) => Render.RenderBlockToMarkdown(new Block
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type, SortOrder = 0,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    });

    [Fact]
    public void Personal_information_is_the_name_headline_and_contact()
    {
        var md = Md("personalInfo", new
        {
            name = "Ada Lovelace", headline = "Analyst", email = "ada@example.org", location = "London",
            phones = new[] { new { kind = "mobile", number = "+44 20 0000 0000" } },
            socials = new[] { new { network = "github", handle = "ada" } },
        });
        md.Should().Contain("# Ada Lovelace").And.Contain("*Analyst*").And.Contain("ada@example.org")
            .And.Contain("+44 20 0000 0000").And.Contain("London").And.Contain("github: ada");
        md.Should().NotContain("Unsupported");
    }

    [Fact]
    public void A_section_is_a_heading_and_an_entry_keeps_every_field()
    {
        Md("cvSection", new { title = "Experience" }).Should().Be("## Experience");
        Md("cvEntry", new { period = "2020–2024", role = "Engineer", org = "Lab", location = "Paris", highlight = "Lead", description = "Built things.", tech = new[] { "C#", "TypeScript" } })
            .Should().Contain("**Engineer**, Lab — Paris (2020–2024)").And.Contain("*Lead*").And.Contain("Built things.").And.Contain("C#, TypeScript");
    }

    [Fact]
    public void An_entry_with_only_a_role_has_no_stray_separators()
        => Md("cvEntry", new { role = "Volunteer" }).Should().Be("**Volunteer**");

    [Fact]
    public void A_photo_is_an_image_and_an_empty_one_is_nothing()
    {
        Md("photo", new { src = "/api/x/photo.png", alt = "Ada" }).Should().Be("![Ada](/api/x/photo.png)");
        Md("photo", new { src = "" }).Should().BeEmpty();
    }
}
