using System.Net;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// The skill a free author downloads to run with their own AI is the one Ask Lilia runs (10 Nov 2026: the app
/// offered an old structure-only file whose JSON went into a paste box that no longer exists).
/// </summary>
public class SkillDownloadTests : IntegrationTestBase
{
    public SkillDownloadTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_architect_skill_downloads_as_written_with_its_front_matter()
    {
        await SeedUserAsync("test_user_001");
        var res = await Client.GetAsync("/api/ai/skills/lilia-document-architect/download");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/markdown");
        res.Content.Headers.ContentDisposition!.FileName.Should().Contain("lilia-document-architect.SKILL.md");
        var md = await res.Content.ReadAsStringAsync();
        md.Should().StartWith("---").And.Contain("name: lilia-document-architect").And.Contain("@blocktype");
    }

    [Fact]
    public async Task Only_the_router_s_skills_and_only_signed_in()
    {
        await SeedUserAsync("test_user_001");
        (await Client.GetAsync("/api/ai/skills/not-a-skill/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.GetAsync("/api/ai/skills/..%2Fappsettings/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var anon = CreateAnonymousClient();
        (await anon.GetAsync("/api/ai/skills/lilia-document-architect/download")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
