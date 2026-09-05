using Microsoft.Extensions.Configuration;

namespace Lilia.Infrastructure.Data;

/// <summary>
/// Which database a host talks to, chosen explicitly rather than by accident.
///
/// <para>This exists because the two were previously decided by config
/// precedence: <c>appsettings.json</c> named localhost, user secrets overrode
/// <c>ConnectionStrings:LiliaCore</c> with Neon, and secrets win — so the editor
/// ran against production on a developer's machine while the tools host, having
/// no secrets file, ran against localhost. Two hosts, one solution, different
/// databases, and nothing on screen said so.</para>
///
/// <para>The target is now a named choice with a safe default. Local is what you
/// get unless you ask for something else, and the connection strings live under
/// distinct keys so a secret can no longer quietly capture the default.</para>
///
/// <list type="bullet">
///   <item><c>Database:Target=local</c> (default) → <c>ConnectionStrings:LiliaCore</c></item>
///   <item><c>Database:Target=neon</c> → <c>ConnectionStrings:LiliaCoreNeon</c></item>
/// </list>
///
/// <para>Environment variables override files, so a container or a systemd unit
/// switches with <c>Database__Target=neon</c> and
/// <c>ConnectionStrings__LiliaCoreNeon=…</c> — nothing to rebuild, and the same
/// image runs anywhere.</para>
/// </summary>
public static class DatabaseTarget
{
    public const string LocalKey = "LiliaCore";
    public const string NeonKey = "LiliaCoreNeon";

    public sealed record Resolved(string Name, string ConnectionString, string Host)
    {
        /// <summary>Safe to log: host and database only, never credentials.</summary>
        public override string ToString() => $"{Name} ({Host})";
    }

    public static Resolved Resolve(IConfiguration configuration)
    {
        var name = (configuration["Database:Target"] ?? "local").Trim().ToLowerInvariant();
        var key = name switch
        {
            "neon" => NeonKey,
            "local" => LocalKey,
            _ => throw new InvalidOperationException(
                $"Database:Target must be 'local' or 'neon', not '{name}'."),
        };

        var connectionString = configuration.GetConnectionString(key)
            ?? throw new InvalidOperationException(
                $"Database:Target is '{name}' but ConnectionStrings:{key} is not configured. " +
                $"Set it in user secrets for a developer machine, or as " +
                $"ConnectionStrings__{key} in the environment.");

        return new Resolved(name, connectionString, DescribeHost(connectionString));
    }

    /// <summary>
    /// Host and database from a connection string, for logging which one a
    /// process actually opened. Never returns the password — this ends up in
    /// logs, and a connection string is mostly credential.
    /// </summary>
    private static string DescribeHost(string connectionString)
    {
        string? host = null, database = null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var k = part[..eq].Trim();
            var v = part[(eq + 1)..].Trim();
            if (k.Equals("Host", StringComparison.OrdinalIgnoreCase)) host = v;
            else if (k.Equals("Database", StringComparison.OrdinalIgnoreCase)) database = v;
        }
        return $"{host ?? "?"}/{database ?? "?"}";
    }
}
