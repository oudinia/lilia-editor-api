using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Lilia.Api.Tests.Integration.Infrastructure;

/// <summary>
/// One integration test class's database and API host (IClassFixture, through IntegrationTestBase).
///
/// <para>Parallel since 1 Nov 2026. Every class used to share one collection, so the 1,072 database
/// tests ran one class at a time: 8.8 minutes of a 13.5-minute run. Now one Postgres container serves
/// the whole run, the migrations run once into a template database, and each class gets its own copy
/// (CREATE DATABASE … TEMPLATE, well under a second), so classes run side by side and none sees
/// another's rows.</para>
/// </summary>
public class TestDatabaseFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = null!;
    public LiliaWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConnectionString = SharedPostgres.ConnectionStringFor(await SharedPostgres.CloneAsync());
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
            catch (TimeoutException) { /* the database goes with the container at the end of the run */ }
        }
        // The class's idle connections close (Postgres refused the run at its connection limit while
        // finished classes' pools held theirs). The database itself stays until the container goes at
        // the end of the run: nothing more to do, and nothing to race a stopping host for.
        if (ConnectionString is not null) NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
    }
}

/// <summary>
/// The run's one Postgres container (18, as Neon) and its migrated template database.
/// </summary>
internal static class SharedPostgres
{
    private const string Template = "lilia_template";

    // Loopback only (19 Oct): Docker publishes on every interface by default and its rules go around
    // ufw, so each test run's database (default password) was reachable from the internet for minutes.
    // Removed when the test process exits (StartAsync): Testcontainers' own cleaner, Ryuk, is off on
    // this server (~/.testcontainers.properties), and ops/ci-local.sh sweeps whatever a crash left.
    private static readonly PostgreSqlContainer Container = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        // A host per class, each with its own pool: well past Postgres's default of 100.
        .WithCommand("-c", "max_connections=1000")
        .WithCreateParameterModifier(p =>
        {
            foreach (var binding in p.HostConfig.PortBindings?.Values.SelectMany(b => b) ?? [])
                binding.HostIP = "127.0.0.1";
        })
        .Build();

    private static readonly Lazy<Task> Ready = new(StartAsync);

    // Postgres copies a template only while nobody else is connected to it, a copy in progress included.
    private static readonly SemaphoreSlim OneCopyAtATime = new(1, 1);

    private static async Task StartAsync()
    {
        await Container.StartAsync();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Container.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(30)); } catch (Exception) { }
        };
        await using (var admin = new NpgsqlConnection(Container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {Template}", admin);
            await create.ExecuteNonQueryAsync();
        }

        // The full migration chain, once: the schema matches what the app expects in production
        // (including every historical table still referenced by cleanup SQL). Program.cs's Migrate
        // is skipped in the Testing environment, so this is the one place migrations run for tests.
        var options = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseNpgsql(ConnectionStringFor(Template))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var context = new LiliaDbContext(options))
            await context.Database.MigrateAsync();
        NpgsqlConnection.ClearAllPools();
    }

    public static string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(Container.GetConnectionString()) { Database = database }.ConnectionString;

    /// <summary>A fresh database with the migrated schema, for one test class.</summary>
    public static async Task<string> CloneAsync()
    {
        await Ready.Value;
        var name = "lilia_" + Guid.NewGuid().ToString("N");
        await OneCopyAtATime.WaitAsync();
        try
        {
            await using var admin = new NpgsqlConnection(Container.GetConnectionString());
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name} TEMPLATE {Template}", admin);
            await create.ExecuteNonQueryAsync();
        }
        finally { OneCopyAtATime.Release(); }
        return name;
    }
}
