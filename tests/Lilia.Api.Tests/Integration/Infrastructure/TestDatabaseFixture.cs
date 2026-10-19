using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace Lilia.Api.Tests.Integration.Infrastructure;

public class TestDatabaseFixture : IAsyncLifetime
{
    // Loopback only (19 Oct): Docker publishes on every interface by default and its rules go around
    // ufw, so each test run's database (default password) was reachable from the internet for minutes.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithCreateParameterModifier(p =>
        {
            foreach (var binding in p.HostConfig.PortBindings?.Values.SelectMany(b => b) ?? [])
                binding.HostIP = "127.0.0.1";
        })
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

    /// <summary>
    /// Teardown is bounded: a host that won't stop must not hold the run. On 12 Oct the host's
    /// shutdown waited forever on Postgres (Wolverine releasing an advisory lock); the blame
    /// collector killed the run after 3 minutes and every later collection was lost.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (Factory != null)
        {
            try { await Factory.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(60)); }
            catch (TimeoutException) { /* the container below goes away regardless */ }
        }
        try { await _container.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(60)); }
        catch (TimeoutException) { }
    }
}
