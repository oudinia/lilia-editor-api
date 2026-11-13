using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Features.Library;

/// <summary>
/// Full browser suite, 13 Nov 2026: a person at their plan's document limit who tried someone else's
/// private template was told 402 (the limit) about a template they can't see; everyone else gets 404.
/// Access is checked first now, so the answer doesn't depend on the caller's plan.
/// </summary>
public class TemplateUseAtLimitTests : IntegrationTestBase, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public TemplateUseAtLimitTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private HttpClient Caller(string userId)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b => b.UseSetting("Entitlements:Enforce", "true"));
        _factories.Add(factory);
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    private static string NewId(string what) => $"tpl_{what}_{Guid.NewGuid():N}"[..36];

    [Fact]
    public async Task Someone_elses_private_template_is_not_found_even_at_the_document_limit()
    {
        var owner = NewId("owner");
        var other = NewId("other");
        await SeedUserAsync(owner);
        await SeedUserAsync(other);
        await using (var db = CreateDbContext())
        {
            var free = await db.Plans.SingleAsync(p => p.Slug == "free");
            db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = other, PlanId = free.Id, Status = "active", StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        for (var i = 0; i < 3; i++) await SeedDocumentAsync(other, $"Mine {i}"); // Free's three documents

        var template = await SeedDocumentAsync(owner, "Private recipe");
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FindAsync(template.Id);
            d!.IsTemplate = true;
            d.IsPublicTemplate = false;
            d.TemplateName = "Private recipe";
            await db.SaveChangesAsync();
        }

        var res = await Caller(other).PostAsync($"/api/templates/{template.Id}/use", JsonContent.Create(new { }));
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
