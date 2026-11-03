using FluentAssertions;
using Lilia.Api.Directories;
using Lilia.Api.Features.Teams.Data;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Teams;

/// <summary>
/// Teams' own database context (3 Nov 2026, Teams' bounded context, step 4), held as Sharing's is: it maps
/// Teams' tables exactly as the main context does, and nothing else; Teams' code never uses the main one; it
/// runs on the request's connection, so a new team and the person's pointer to it share one transaction.
/// </summary>
public class TeamsDbContextTests : IntegrationTestBase
{
    public TeamsDbContextTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static IModel ModelOf<T>(Func<DbContextOptions<T>, T> create) where T : DbContext
    {
        // Building the model connects to nothing.
        using var db = create(new DbContextOptionsBuilder<T>().UseNpgsql("Host=model.invalid").Options);
        return db.GetService<IDesignTimeModel>().Model;
    }

    private static string Describe(IEntityType e)
    {
        var table = StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());
        var columns = e.GetProperties()
            .Select(p => $"{p.GetColumnName(table)} {p.GetColumnType()} {(p.IsColumnNullable(table) ? "null" : "not null")} {p.GetDefaultValueSql(table)}{p.GetDefaultValue(table)}")
            .OrderBy(c => c);
        var key = string.Join(",", e.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName(table)));
        return $"{e.GetTableName()} key({key})\n  {string.Join("\n  ", columns)}";
    }

    private static IEnumerable<string> IndexesOf(IEntityType e)
    {
        var table = StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());
        return e.GetIndexes().Select(i => $"{string.Join(",", i.Properties.Select(p => p.GetColumnName(table)))}{(i.IsUnique ? " unique" : "")} {i.GetFilter()}");
    }

    [Fact]
    public void Teams_maps_its_tables_exactly_as_the_main_context_does()
    {
        var teams = ModelOf<TeamsDbContext>(o => new TeamsDbContext(o));
        var main = ModelOf<LiliaDbContext>(o => new LiliaDbContext(o));

        foreach (var e in teams.GetEntityTypes())
        {
            var same = main.FindEntityType(e.ClrType);
            same.Should().NotBeNull($"{e.ClrType.Name} is a table of the main model too");
            Describe(e).Should().Be(Describe(same!), "the two contexts read and write the same table");
            IndexesOf(same!).Should().Contain(IndexesOf(e), "Teams' indexes are the table's");
        }
    }

    [Fact]
    public void Teams_maps_its_own_tables_and_nothing_else()
    {
        var teams = ModelOf<TeamsDbContext>(o => new TeamsDbContext(o));

        teams.GetEntityTypes().Select(e => e.GetTableName()).Should().BeEquivalentTo(["teams", "groups", "group_members", "roles"]);
    }

    [Fact]
    public void Teams_code_never_uses_the_main_context()
    {
        var dir = Path.Combine(RepoRoot(), "src", "Lilia.Api", "Features", "Teams");
        var users = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("LiliaDbContext"))
            .Select(f => Path.GetRelativePath(dir, f));

        users.Should().BeEmpty("Teams reads and writes through TeamsDbContext, and asks the directories for the rest");
    }

    [Fact]
    public void A_request_uses_one_connection_for_both_contexts()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var main = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var teams = scope.ServiceProvider.GetRequiredService<TeamsDbContext>();

        teams.Database.GetDbConnection().Should().BeSameAs(main.Database.GetDbConnection());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_default_team_claim_goes_with_the_teams_transaction(bool commit)
    {
        var userId = $"ctx_team_{Guid.NewGuid():N}"[..24];
        await SeedUserAsync(userId);
        var teamId = Guid.NewGuid();

        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            var teams = scope.ServiceProvider.GetRequiredService<TeamsDbContext>();
            var accounts = scope.ServiceProvider.GetRequiredService<ITeamAccounts>();
            teams.Teams.Add(new Team { Id = teamId, Name = "Ctx team", TeamCode = $"ctx-{Guid.NewGuid():N}"[..16], OwnerId = userId });
            await using var tx = await teams.Database.BeginTransactionAsync();
            await teams.SaveChangesAsync();

            (await accounts.ClaimDefaultTeamAsync(userId, teamId, tx.GetDbTransaction())).Should().BeTrue();

            if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        }

        await using var check = CreateDbContext();
        (await check.Teams.AnyAsync(t => t.Id == teamId)).Should().Be(commit);
        (await check.Users.Where(u => u.Id == userId).Select(u => u.DefaultTeamId).SingleAsync())
            .Should().Be(commit ? teamId : null, "the pointer goes with the team, or not at all");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "Lilia.Api", "Lilia.Api.csproj"))) return dir.FullName;
        throw new InvalidOperationException("the repository's src/Lilia.Api was not found above " + AppContext.BaseDirectory);
    }
}
