using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace Lilia.Api.Tests.Integration.Infrastructure;

public class TestDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();
    public LiliaWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Run the full migration chain — ensures the schema matches what the
        // app expects at prod (including every historical table still
        // referenced by cleanup SQL, e.g. templates/formulas/snippets).
        // Program.cs's Migrate is skipped in the Testing environment, so this
        // is the single place migrations run for tests.
        var options = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseNpgsql(ConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var context = new LiliaDbContext(options);
        await context.Database.MigrateAsync();

        Factory = new LiliaWebApplicationFactory(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (Factory != null)
            await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}
