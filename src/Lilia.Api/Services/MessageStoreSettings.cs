using Microsoft.Extensions.Configuration;
using Wolverine;

namespace Lilia.Api.Services;

/// <summary>
/// How Wolverine uses its Postgres message store.
/// </summary>
public static class MessageStoreSettings
{
    /// <summary>
    /// <list type="bullet">
    /// <item>Solo by default: Lilia runs one API process per database. Balanced mode (Wolverine's
    /// default) elects a leader with a session advisory lock held on a dedicated connection, polls a
    /// control queue every few seconds, runs node health checks and writes node records. Through
    /// Neon's pooler (PgBouncer, transaction mode) a session lock is not held by the session that
    /// took it, and every stall of that path became a lost leadership, a re-election and a burst of
    /// [ERR] lines: 150–300 a day on production (Oct 2026). Solo has no lock, no control queue and
    /// no node records; the durability agent (recovery, scheduled messages) still runs.
    /// <c>Wolverine:Solo=false</c> brings Balanced back for a second node.</item>
    /// <item>No envelope-storage metrics: nothing reads them (no metrics exporter), and they were a
    /// count query every 5 seconds.</item>
    /// </list>
    /// </summary>
    public static void Apply(DurabilitySettings durability, IConfiguration configuration)
    {
        if (configuration.GetValue("Wolverine:Solo", true))
            durability.Mode = DurabilityMode.Solo;
        durability.DurabilityMetricsEnabled = false;
    }
}
