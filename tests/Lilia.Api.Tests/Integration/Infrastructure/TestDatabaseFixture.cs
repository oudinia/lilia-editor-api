using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    private string? _database;

    public string ConnectionString { get; private set; } = null!;
    public LiliaWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _database = await SharedPostgres.CloneAsync();
        ConnectionString = SharedPostgres.ConnectionStringFor(_database);
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
        // finished classes' pools held theirs), then its copy goes: without FORCE, so a connection a
        // stopping host still holds makes the drop fail, not the host. The next run's start sweeps
        // what is left (an exit-time drop never ran: the test host ends first, 120 copies stayed).
        if (ConnectionString is not null) NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
        if (_database is not null) await SharedPostgres.DropAsync(_database);
    }
}

/// <summary>
/// The run's Postgres (18, as Neon) and its migrated template database.
///
/// <para>Always on since 2 Nov 2026 (the owner: "containers should be up all the time for tests"): the
/// server keeps a test Postgres running (lilia-test-pg, loopback only, in memory), named by
/// LILIA_TEST_PG (~/.config/lilia/test-pg.env, which ops/ci-local.sh and lilia test read). Starting a
/// container and migrating it took most of a small run (a 7-second class took 52 s). The template is
/// named by the migrations it holds, so a branch that adds one gets its own and no branch sees another's
/// schema; an unchanged one is ready at once, and a new one starts from the closest earlier template. A class's copy goes when the class is done, and a
/// copy that couldn't go (a crashed run's) is swept by a start an hour later.</para>
///
/// <para>Without LILIA_TEST_PG, or when that server doesn't answer, a container of its own for the
/// run, as before (Testcontainers).</para>
/// </summary>
internal static class SharedPostgres
{
    // A run's copies are named after it, so the run can drop them and a later one can find a crashed run's.
    private static readonly string Run = $"lilia_r{DateTime.UtcNow:yyMMddHHmmss}_{Environment.ProcessId}";
    private static readonly Regex CopyName = new(@"^lilia_r(\d{12})_\d+_[0-9a-f]{8}$", RegexOptions.Compiled);

    private static string _admin = null!;
    private static string _template = null!;

    private static readonly Lazy<Task> Ready = new(StartAsync);

    // Postgres copies a template only while nobody else is connected to it, a copy in progress included.
    private static readonly SemaphoreSlim OneCopyAtATime = new(1, 1);

    private static async Task StartAsync()
    {
        _admin = await AlwaysOnAsync() ?? await ContainerAsync();

        // The template for exactly these migrations: built once, under a lock, for every run after.
        List<string> ids;
        await using (var names = NewContext("postgres"))
            ids = names.Database.GetMigrations().ToList();
        _template = "lilia_tpl_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ids))))[..16].ToLowerInvariant();
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await Exec(admin, "SELECT pg_advisory_lock(72017)");
        try
        {
            if (!await Exists(admin, _template))
            {
                // Built under another name and renamed when complete: a run stopped half-way leaves no
                // template that looks finished.
                var building = _template + "_building";
                await Exec(admin, $"DROP DATABASE IF EXISTS {building} WITH (FORCE)");
                // From the template that already holds the most of these migrations, in order: a branch
                // that adds one applies just that one (about a second, against 25 s for all 122). None
                // that fits (a branch that removed or reordered one): from an empty database.
                var closest = await ClosestTemplateAsync(admin, ids);
                await Exec(admin, closest is null ? $"CREATE DATABASE {building}" : $"CREATE DATABASE {building} TEMPLATE {closest}");
                // The migrations it lacks: the schema matches what the app expects in production
                // (including every historical table still referenced by cleanup SQL). Program.cs's Migrate
                // is skipped in the Testing environment, so this is the one place migrations run for tests.
                await using (var context = NewContext(building))
                    await context.Database.MigrateAsync();
                NpgsqlConnection.ClearAllPools();
                await Exec(admin, $"ALTER DATABASE {building} RENAME TO {_template}");
            }
            // The eight newest templates stay (each is about 15 MB of the server's memory). One in use
            // (being copied) refuses the drop and stays too.
            var templates = await Names(admin, "lilia_tpl_%", byNewest: true);
            foreach (var old in templates.Where(t => !t.EndsWith("_building") && t != _template).Skip(7))
                try { await Exec(admin, $"DROP DATABASE IF EXISTS {old}"); } catch (PostgresException) { }
            // Copies a crashed run left (its exit never came), from runs over an hour old.
            var cutoff = DateTime.UtcNow.AddHours(-1).ToString("yyMMddHHmmss");
            foreach (var old in await Names(admin, "lilia_r%"))
                if (CopyName.Match(old) is { Success: true } m && string.CompareOrdinal(m.Groups[1].Value, cutoff) < 0)
                    await Exec(admin, $"DROP DATABASE IF EXISTS {old} WITH (FORCE)");
        }
        finally { await Exec(admin, "SELECT pg_advisory_unlock(72017)"); }

    }

    // The always-on server, when there is one and it answers.
    private static async Task<string?> AlwaysOnAsync()
    {
        if (Environment.GetEnvironmentVariable("LILIA_TEST_PG") is not { Length: > 0 } cs) return null;
        try
        {
            await using var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(cs) { Timeout = 3 }.ConnectionString);
            await c.OpenAsync();
            return cs;
        }
        catch (Exception) { return null; }
    }

    // A container of the run's own.
    // Loopback only (19 Oct): Docker publishes on every interface by default and its rules go around
    // ufw, so each test run's database (default password) was reachable from the internet for minutes.
    // Removed when the test process exits: Testcontainers' own cleaner, Ryuk, is off on this server
    // (~/.testcontainers.properties), and ops/ci-local.sh sweeps whatever a crash left.
    private static async Task<string> ContainerAsync()
    {
        var container = new PostgreSqlBuilder()
            .WithImage("postgres:18-alpine")
            // A host per class, each with its own pool: well past Postgres's default of 100.
            .WithCommand("-c", "max_connections=1000")
            .WithCreateParameterModifier(p =>
            {
                foreach (var binding in p.HostConfig.PortBindings?.Values.SelectMany(b => b) ?? [])
                    binding.HostIP = "127.0.0.1";
            })
            .Build();
        await container.StartAsync();
        // Synchronously, with Docker's own command: the test host's exit doesn't wait for an async
        // dispose, and the container outlived the run (2 Nov).
        var id = container.Id;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("docker", $"rm -f {id}")?.WaitForExit(15000); } catch (Exception) { }
        };
        return container.GetConnectionString();
    }

    private static LiliaDbContext NewContext(string database) => new(new DbContextOptionsBuilder<LiliaDbContext>()
        .UseNpgsql(ConnectionStringFor(database))
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
        .Options);

    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> Exists(NpgsqlConnection c, string database) =>
        (await Names(c, database)).Count > 0;

    // The template whose migrations are the longest prefix of these, or null.
    private static async Task<string?> ClosestTemplateAsync(NpgsqlConnection admin, List<string> ids)
    {
        string? best = null;
        var bestCount = 0;
        foreach (var name in await Names(admin, "lilia_tpl_%"))
        {
            if (name.EndsWith("_building")) continue;
            var applied = new List<string>();
            var cs = ConnectionStringFor(name);
            await using (var c = new NpgsqlConnection(cs))
            {
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", c);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) applied.Add(r.GetString(0));
            }
            // Postgres copies a template only when nobody is connected to it, this look included.
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
            if (applied.Count > bestCount && applied.Count <= ids.Count && applied.SequenceEqual(ids.Take(applied.Count)))
                (best, bestCount) = (name, applied.Count);
        }
        return best;
    }

    private static async Task<List<string>> Names(NpgsqlConnection c, string like, bool byNewest = false)
    {
        await using var cmd = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datname LIKE @p" + (byNewest ? " ORDER BY oid DESC" : ""), c);
        cmd.Parameters.AddWithValue("p", like);
        var names = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) names.Add(r.GetString(0));
        return names;
    }

    public static string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_admin ?? "Host=localhost") { Database = database }.ConnectionString;

    /// <summary>A class's copy, when the class is done. Best effort: what stays is swept at a later start.</summary>
    public static async Task DropAsync(string database)
    {
        try
        {
            await using var admin = new NpgsqlConnection(_admin);
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {database}");
        }
        catch (Exception) { }
    }

    /// <summary>A fresh database with the migrated schema, for one test class.</summary>
    public static async Task<string> CloneAsync()
    {
        await Ready.Value;
        var name = $"{Run}_{Guid.NewGuid():N}"[..(Run.Length + 9)];
        await OneCopyAtATime.WaitAsync();
        try
        {
            await using var admin = new NpgsqlConnection(_admin);
            await admin.OpenAsync();
            await Exec(admin, $"CREATE DATABASE {name} TEMPLATE {_template}");
        }
        finally { OneCopyAtATime.Release(); }
        return name;
    }
}
