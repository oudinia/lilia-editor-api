using FluentAssertions;
using Lilia.Api.Services;
using Npgsql;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// No connection is held open while idle: Neon drops idle connections, and a pool that kept one
/// (MinPoolSize 1) handed it to the next request, which failed with a connection reset.
/// </summary>
public class DatabasePoolTests
{
    [Fact]
    public void Idle_connections_are_closed_rather_than_kept_for_Neon_to_drop()
    {
        var tuned = new NpgsqlConnectionStringBuilder(
            DatabasePool.Tune("Host=db;Database=lilia;Username=u;Minimum Pool Size=5;Maximum Pool Size=100"));

        tuned.MinPoolSize.Should().Be(0);
        tuned.ConnectionIdleLifetime.Should().BeLessThanOrEqualTo(30);
        tuned.MaxPoolSize.Should().Be(18, "the managed Postgres slot limit still applies");
        tuned.KeepAlive.Should().Be(0, "keep-alive queries would keep Neon's compute awake");
        tuned.Host.Should().Be("db");
    }
}
