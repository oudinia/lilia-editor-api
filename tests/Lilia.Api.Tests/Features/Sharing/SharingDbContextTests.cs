using FluentAssertions;
using Lilia.Api.Directories;
using Lilia.Api.Features.Sharing.Data;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Sharing;

/// <summary>
/// Sharing's own database context (2 Nov 2026, the bounded contexts, step 4). It maps Sharing's tables
/// exactly as the main context does, and nothing else; Sharing's code uses it and never the main one; it
/// runs on the request's connection, so the account erase can remove shares inside its transaction.
/// </summary>
public class SharingDbContextTests : IntegrationTestBase
{
    public SharingDbContextTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static readonly string[] SharingTables =
        ["document_collaborators", "document_groups", "document_pending_invites", "comments", "comment_replies", "notifications", "roles"];

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
    public void Sharing_maps_its_tables_exactly_as_the_main_context_does()
    {
        var sharing = ModelOf<SharingDbContext>(o => new SharingDbContext(o));
        var main = ModelOf<LiliaDbContext>(o => new LiliaDbContext(o));

        foreach (var e in sharing.GetEntityTypes())
        {
            var same = main.FindEntityType(e.ClrType);
            same.Should().NotBeNull($"{e.ClrType.Name} is a table of the main model too");
            Describe(e).Should().Be(Describe(same!), "the two contexts read and write the same table");
            // The main context has more: EF indexes each link it keeps (to users, documents…). Indexes are the
            // migrations', which stay with the main context; what Sharing declares must be there too.
            IndexesOf(same!).Should().Contain(IndexesOf(e), "Sharing's indexes are the table's");
        }
    }

    [Fact]
    public void Sharing_maps_its_own_tables_and_nothing_else()
    {
        var sharing = ModelOf<SharingDbContext>(o => new SharingDbContext(o));

        sharing.GetEntityTypes().Select(e => e.GetTableName()).Should().BeEquivalentTo(SharingTables);
    }

    [Fact]
    public void Sharing_code_never_uses_the_main_context()
    {
        // The compiler then holds the line: the main context's other tables aren't reachable from Sharing.
        var dir = Path.Combine(RepoRoot(), "src", "Lilia.Features.Sharing");
        var users = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("LiliaDbContext"))
            .Select(f => Path.GetRelativePath(dir, f));

        users.Should().BeEmpty("Sharing reads and writes through SharingDbContext, and asks the directories for the rest");
    }

    [Fact]
    public void A_request_uses_one_connection_for_both_contexts()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var main = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var sharing = scope.ServiceProvider.GetRequiredService<SharingDbContext>();

        sharing.Database.GetDbConnection().Should().BeSameAs(main.Database.GetDbConnection());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_erases_shares_go_with_its_transaction(bool commit)
    {
        var owner = $"ctx_owner_{Guid.NewGuid():N}"[..24];
        var friend = $"ctx_friend_{Guid.NewGuid():N}"[..24];
        await SeedUserAsync(owner);
        await SeedUserAsync(friend);
        var doc = await SeedDocumentAsync(owner, "Shared paper");
        await using (var seed = CreateDbContext())
        {
            seed.DocumentCollaborators.Add(new DocumentCollaborator
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = friend,
                RoleId = (await seed.Roles.FirstAsync(r => r.Name == "viewer")).Id, InvitedBy = owner, CreatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            var main = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
            var accounts = scope.ServiceProvider.GetRequiredService<ISharingAccounts>();
            await using var tx = await main.Database.BeginTransactionAsync();

            await accounts.ForgetAsync(friend, tx.GetDbTransaction());

            if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        }

        await using var check = CreateDbContext();
        (await check.DocumentCollaborators.CountAsync(c => c.UserId == friend))
            .Should().Be(commit ? 0 : 1, commit ? "the erase committed" : "the erase rolled back, and Sharing's part with it");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "Lilia.Api", "Lilia.Api.csproj"))) return dir.FullName;
        throw new InvalidOperationException("the repository's src/Lilia.Api was not found above " + AppContext.BaseDirectory);
    }
}
