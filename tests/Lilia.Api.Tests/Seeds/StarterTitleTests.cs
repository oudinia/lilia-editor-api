using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data.Seeds;
using Xunit;

namespace Lilia.Api.Tests.Seeds;

/// <summary>
/// The starters used to open with their name as a level-1 heading, so the title
/// sat left-aligned like a section (user, 10 Oct 2026, on a phone). The seeder
/// now writes a Title block and retires the old starters it recognises.
/// </summary>
public class StarterTitleTests
{
    private static Block B(string type, int order, string json) => new()
    {
        Id = Guid.NewGuid(), Type = type, SortOrder = order, Content = JsonDocument.Parse(json),
    };

    private static Document Doc(string title, params Block[] blocks)
    {
        var d = new Document { Id = Guid.NewGuid(), Title = title };
        foreach (var b in blocks) d.Blocks.Add(b);
        return d;
    }

    [Fact]
    public void An_old_starter_opens_with_its_name_as_a_heading()
    {
        var doc = Doc("Sample Article",
            B("abstract", 1, """{"text":"x"}"""),
            B("heading", 0, """{"text":"Sample Article","level":1}"""));
        StarterDocumentSeeder.OpensWithHeadingTitle(doc).Should().BeTrue();
    }

    [Fact]
    public void A_new_starter_or_another_heading_is_left_alone()
    {
        StarterDocumentSeeder.OpensWithHeadingTitle(Doc("Sample Article",
            B("title", 0, """{"title":"Sample Article","author":"","dateMode":"today","date":""}"""))).Should().BeFalse();
        StarterDocumentSeeder.OpensWithHeadingTitle(Doc("Sample Article",
            B("heading", 0, """{"text":"1. Introduction","level":1}"""))).Should().BeFalse();
        StarterDocumentSeeder.OpensWithHeadingTitle(Doc("Sample CV")).Should().BeFalse();
    }
}
