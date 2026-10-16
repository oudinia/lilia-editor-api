using FluentAssertions;
using Lilia.Admin.Data;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Admin.Tests;

/// <summary>What the console reads, on a real database: counts, search, an account's page, triage.</summary>
[Collection("Admin")]
public class AdminQueriesTests(AdminFixture fx)
{
    private AdminQueries Queries(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AdminQueries>();

    private async Task<(string Id, Guid Doc)> SeedAuthorAsync(string tag, int failedCompiles = 0)
    {
        await using var db = fx.CreateDbContext();
        var id = $"adm-{tag}-{Guid.NewGuid():N}"[..28];
        var now = DateTime.UtcNow;
        db.Users.Add(new User { Id = id, Email = $"{id}@lilia.test", Name = $"Author {tag}", CreatedAt = now, UpdatedAt = now });
        var doc = new Document { Id = Guid.NewGuid(), OwnerId = id, Title = $"Paper by {tag}", CreatedAt = now, UpdatedAt = now };
        db.Documents.Add(doc);
        for (var i = 0; i < failedCompiles; i++)
            db.LaTeXCompilationEvents.Add(new LaTeXCompilationEvent
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = id, EventType = "compile", Success = false,
                ErrorCategory = "undefined_control_sequence", ErrorToken = $"\\macro{tag}", OccurredAt = now,
            });
        await db.SaveChangesAsync();
        return (id, doc.Id);
    }

    [Fact]
    public async Task The_overview_counts_new_accounts_documents_and_failed_compiles()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var before = await Queries(scope).OverviewAsync();
        await SeedAuthorAsync("ov", failedCompiles: 2);
        var after = await Queries(scope).OverviewAsync();

        (after.Users - before.Users).Should().Be(1);
        (after.NewUsers24h - before.NewUsers24h).Should().Be(1);
        (after.DocumentsCreated7d - before.DocumentsCreated7d).Should().Be(1);
        (after.CompilesFailed7d - before.CompilesFailed7d).Should().Be(2);
        after.SignupsByDay.Should().HaveCount(14);
        after.SignupsByDay[^1].Count.Should().BeGreaterThanOrEqualTo(1, "today's sign-up is in the last bar");
    }

    [Fact]
    public async Task Search_finds_an_account_by_part_of_its_email_and_its_page_lists_titles_only()
    {
        var (id, _) = await SeedAuthorAsync("search", failedCompiles: 1);
        using var scope = fx.Factory.Services.CreateScope();
        var rows = await Queries(scope).UsersAsync(id[4..16]);
        rows.Should().ContainSingle(r => r.Id == id).Which.Documents.Should().Be(1);

        var page = await Queries(scope).UserAsync(id);
        page.Should().NotBeNull();
        page!.Documents.Should().ContainSingle().Which.Title.Should().Be("Paper by search");
        page.RecentCompileFailures.Should().ContainSingle().Which.Token.Should().Be("\\macrosearch");
        // The console never carries a document's content: the row is title, dates and the trash flag.
        typeof(DocumentRow).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["Id", "Title", "CreatedAt", "UpdatedAt", "Deleted"]);

        (await Queries(scope).UserAsync("no-such-account")).Should().BeNull();
    }

    [Fact]
    public async Task Errors_group_failed_compiles_by_category_and_token()
    {
        await SeedAuthorAsync("grp", failedCompiles: 3);
        using var scope = fx.Factory.Services.CreateScope();
        var errors = await Queries(scope).ErrorsAsync();
        errors.Compiles.Should().Contain(g => g.Detail == "\\macrogrp" && g.Count == 3 && g.Kind == "undefined_control_sequence");
    }

    [Fact]
    public async Task Feedback_is_triaged_to_a_known_status_only()
    {
        var id = Guid.NewGuid();
        await using (var db = fx.CreateDbContext())
        {
            db.Feedback.Add(new Feedback { Id = id, Type = "bug", Message = "The export button does nothing", Status = "new", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var scope = fx.Factory.Services.CreateScope();
        (await Queries(scope).FeedbackAsync("new")).Should().Contain(f => f.Id == id);
        (await Queries(scope).SetFeedbackStatusAsync(id, "resolved")).Should().BeTrue();
        (await Queries(scope).SetFeedbackStatusAsync(id, "deleted")).Should().BeFalse();
        (await Queries(scope).SetFeedbackStatusAsync(Guid.NewGuid(), "resolved")).Should().BeFalse();
        await using var check = fx.CreateDbContext();
        (await check.Feedback.FirstAsync(f => f.Id == id)).Status.Should().Be("resolved");
    }
}
