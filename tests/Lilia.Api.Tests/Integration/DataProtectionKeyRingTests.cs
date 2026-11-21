using FluentAssertions;
using Lilia.Api.Mcp;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Integration;

/// <summary>
/// The data-protection key ring lives in the database (21 Nov 2026), so production and its standby machine unprotect
/// each other's payloads. Before, each machine kept its keys on its own disk: a switch to the standby would have
/// broken every connector file link the first machine signed. Each "machine" here is a service provider of its own
/// (its own key ring cache, as a separate process has) on the class's test database.
/// </summary>
public class DataProtectionKeyRingTests : IntegrationTestBase
{
    private const string Site = "https://editor.liliaeditor.com";

    public DataProtectionKeyRingTests(TestDatabaseFixture fixture) : base(fixture) { }

    /// <summary>A machine on this database, wired as the API and the admin console are.</summary>
    private ServiceProvider Machine()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LiliaDbContext>(o => o.UseNpgsql(Fixture.ConnectionString));
        services.AddLiliaDataProtection();
        return services.BuildServiceProvider();
    }

    /// <summary>The old wiring: keys on this machine's own disk.</summary>
    private static ServiceProvider MachineWithKeysOnDisk(DirectoryInfo disk)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection()
            .SetApplicationName(LiliaDataProtection.ApplicationName)
            .PersistKeysToFileSystem(disk);
        return services.BuildServiceProvider();
    }

    private static ConnectorTransfers Transfers(IServiceProvider machine) => new(
        new MemoryCache(new MemoryCacheOptions()), machine.GetRequiredService<IDataProtectionProvider>(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:PublicOrigin"] = Site }).Build());

    [Fact]
    public async Task Two_machines_on_one_database_unprotect_each_others_payloads()
    {
        await using var first = Machine();
        await using var second = Machine();
        var a = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test.purpose");
        var b = second.GetRequiredService<IDataProtectionProvider>().CreateProtector("test.purpose");

        b.Unprotect(a.Protect("made on the first")).Should().Be("made on the first");
        a.Unprotect(b.Protect("made on the second")).Should().Be("made on the second");

        await using var db = CreateDbContext();
        var keys = await db.DataProtectionKeys.AsNoTracking().ToListAsync();
        keys.Should().NotBeEmpty();
        keys.Should().OnlyContain(k => k.Xml!.StartsWith("<key "));
    }

    [Fact]
    public async Task The_API_host_keeps_its_keys_in_the_database()
    {
        // Program.cs's own wiring: what the API protects, another machine on the database reads.
        var api = Fixture.Factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test.api");
        await using var other = Machine();
        other.GetRequiredService<IDataProtectionProvider>().CreateProtector("test.api")
            .Unprotect(api.Protect("from the API host")).Should().Be("from the API host");
    }

    [Fact]
    public async Task A_connector_export_link_made_on_one_machine_works_on_the_other()
    {
        await using var first = Machine();
        await using var second = Machine();
        var doc = Guid.NewGuid();

        var (token, _) = Transfers(first).IssueExport("user-1", doc, "pdf");
        var ticket = Transfers(second).Export(token);

        ticket.Should().NotBeNull();
        ticket!.UserId.Should().Be("user-1");
        ticket.DocumentId.Should().Be(doc);
        ticket.Format.Should().Be("pdf");
    }

    [Fact]
    public async Task A_connector_import_link_made_on_one_machine_works_on_the_other_for_its_author_only()
    {
        await using var first = Machine();
        await using var second = Machine();

        var (token, _) = Transfers(first).IssueImport("user-1", "Claude");
        var other = Transfers(second);

        // The other machine's memory has no ticket: it comes back as waiting, as after a restart.
        other.Import(token, "user-1").Should().NotBeNull()
            .And.Match<ConnectorTransfers.ImportTicket>(t => t.App == "Claude" && t.Status == "waiting");
        other.Import(token, "user-2").Should().BeNull();
    }

    [Fact]
    public async Task With_keys_on_its_own_disk_a_machine_refused_the_others_links()
    {
        // What was wrong: a standby with its own key ring can't read production's links.
        var disk = Directory.CreateTempSubdirectory("lilia-dp-keys-");
        try
        {
            await using var production = Machine();
            await using var standby = MachineWithKeysOnDisk(disk);

            var (token, _) = Transfers(production).IssueExport("user-1", Guid.NewGuid(), "tex");

            Transfers(standby).Export(token).Should().BeNull();
            Transfers(production).Export(token).Should().NotBeNull();
        }
        finally { disk.Delete(recursive: true); }
    }
}
