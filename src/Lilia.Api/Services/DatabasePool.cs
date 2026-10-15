namespace Lilia.Api.Services;

/// <summary>
/// The Npgsql pool settings forced on top of whatever connection string the environment carries.
/// </summary>
public static class DatabasePool
{
    /// <summary>
    /// <list type="bullet">
    /// <item>At most 18 connections: the managed Postgres plans' slot limits (Sentry LILIA-API-9, 53300).</item>
    /// <item>None kept open when idle: an idle connection is closed after 30 s. The pool kept one open
    /// for ever (MinPoolSize 1, which the idle pruning never goes below), Neon dropped it while idle,
    /// and the first request after a quiet spell failed with "Connection reset by peer"
    /// (14 Oct: GET /api/billing/plans 500; 15 Oct: a connector call 500). No keep-alive
    /// queries either: they would keep Neon's compute awake, which is billed.</item>
    /// </list>
    /// </summary>
    public static string Tune(string connectionString) =>
        new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = 18,
            MinPoolSize = 0,
            ConnectionIdleLifetime = 30,
            Pooling = true,
        }.ConnectionString;
}
