namespace Lilia.Api.Services;

/// <summary>
/// The Npgsql pool settings forced on top of whatever connection string the environment carries.
/// </summary>
public static class DatabasePool
{
    /// <summary>
    /// <list type="bullet">
    /// <item>At most 18 connections: the managed Postgres plans' slot limits (Sentry LILIA-API-9, 53300).</item>
    /// <item>The main pool keeps <see cref="WarmConnections"/> open (Oussama, 15 Nov 2026: "Keep 3 connections open").
    /// The first page after a quiet hour took 3–4 s: its seven parallel requests each opened a new connection, about
    /// 1.1 s each, because idle connections closed after 30 s. On 15 Oct a connection kept open while idle was dropped
    /// by Neon and the next request failed ("Connection reset by peer"): it sat idle with no traffic. Now a keep-alive every
    /// 60 s means Neon never sees the warm ones idle, and each is recycled after 10 minutes. (Retrying a dropped
    /// connection is a follow-up: EF's retrying strategy refuses the transactions Lilia opens itself in eight places.)
    /// The keep-alive keeps Neon's compute awake, which the watchdog's /health query every 2 minutes already does.</item>
    /// <item>Other pools (Wolverine's message store) keep none: an idle connection there is closed after 30 s.</item>
    /// </list>
    /// </summary>
    public static string Tune(string connectionString, int warm = 0) =>
        new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = 18,
            MinPoolSize = warm,
            ConnectionIdleLifetime = 30,
            KeepAlive = warm > 0 ? 60 : 0,
            ConnectionLifetime = warm > 0 ? 600 : 0,
            Pooling = true,
        }.ConnectionString;

    /// <summary>Connections the main pool keeps open between requests.</summary>
    public const int WarmConnections = 3;
}
