using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Infrastructure.Data;

/// <summary>
/// Where ASP.NET Data Protection keeps its keys: in the main database, <c>data_protection_keys</c> (21 Nov 2026).
///
/// <para>Until then no host configured it, so each kept its key ring on its own disk
/// (<c>~/.aspnet/DataProtection-Keys</c>). Production is getting a standby on a second machine (database in Neon,
/// files in R2, so either machine can serve): with keys on disk, everything one machine protected (the connector's
/// signed file links, the admin console's antiforgery tokens) would fail to unprotect on the other after a switch.
/// With the ring in the database both machines read and write the same keys.</para>
///
/// <para>One application name for every host on this database (the API and the admin console), so a key made by one
/// is a key of the other; each use still has its own purpose string, so one host's payloads mean nothing to another
/// use. The test copy has its own database, so its own ring.</para>
///
/// <para>The keys are stored as ASP.NET writes them, unencrypted XML, as they were on disk: whoever can read the
/// database can forge what they sign. That reader can already read every document, so this adds no new door.</para>
/// </summary>
public static class LiliaDataProtection
{
    public const string ApplicationName = "lilia";

    /// <summary>Data protection with its key ring in <see cref="LiliaDbContext"/> (which must be registered).</summary>
    public static IDataProtectionBuilder AddLiliaDataProtection(this IServiceCollection services) =>
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<LiliaDbContext>();
}
