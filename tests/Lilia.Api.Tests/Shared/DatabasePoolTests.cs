using FluentAssertions;
using Lilia.Api.Services;
using Npgsql;

namespace Lilia.Api.Tests.Shared;

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
        tuned.KeepAlive.Should().Be(0, "a pool with nothing kept open needs no keep-alive");
        tuned.Host.Should().Be("db");
    }

    // Oussama, 15 Nov 2026: "Keep 3 connections open". The first page after a quiet hour opened seven connections at
    // about 1.1 s each. The main pool keeps three, kept alive and recycled so Neon can't drop one under a request.
    [Fact]
    public void The_main_pool_keeps_three_connections_alive_and_recycles_them()
    {
        var tuned = new NpgsqlConnectionStringBuilder(
            DatabasePool.Tune("Host=db;Database=lilia;Username=u", DatabasePool.WarmConnections));

        tuned.MinPoolSize.Should().Be(3);
        tuned.KeepAlive.Should().Be(60);
        tuned.ConnectionLifetime.Should().Be(600);
        tuned.ConnectionIdleLifetime.Should().BeLessThanOrEqualTo(30, "connections above the three still close when idle");
        tuned.MaxPoolSize.Should().Be(18);
    }
}
