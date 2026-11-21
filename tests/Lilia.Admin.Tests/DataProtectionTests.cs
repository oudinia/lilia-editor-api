using FluentAssertions;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Admin.Tests;

/// <summary>
/// The console's data protection (Blazor's antiforgery tokens and circuit state) keeps its key ring in the database,
/// with the API's application name (21 Nov 2026): a standby machine, or the API, reads what this console protected.
/// </summary>
[Collection("Admin")]
public class DataProtectionTests(AdminFixture fx)
{
    [Fact]
    public async Task What_the_console_protects_another_machine_on_the_database_unprotects()
    {
        var console = fx.Factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test.admin");
        var payload = console.Protect("from the console");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LiliaDbContext>(o => o.UseNpgsql(fx.ConnectionString));
        services.AddLiliaDataProtection();
        await using var other = services.BuildServiceProvider();

        other.GetRequiredService<IDataProtectionProvider>().CreateProtector("test.admin")
            .Unprotect(payload).Should().Be("from the console");
        await using var db = fx.CreateDbContext();
        (await db.DataProtectionKeys.CountAsync()).Should().BeGreaterThan(0);
    }
}
