using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Wolverine;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Wolverine runs as one node (Solo) unless told otherwise, without envelope metrics, and its
/// message store gets the same pool settings as EF: Balanced mode's advisory lock through Neon's
/// pooler logged 150–300 errors a day on production (Oct 2026).
/// </summary>
public class MessageStoreSettingsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void One_node_with_no_leader_election_and_no_metrics_polling_by_default()
    {
        var durability = new WolverineOptions().Durability;
        MessageStoreSettings.Apply(durability, Config());

        durability.Mode.Should().Be(DurabilityMode.Solo);
        durability.DurabilityMetricsEnabled.Should().BeFalse();
    }

    [Fact]
    public void A_second_node_can_turn_Balanced_mode_back_on()
    {
        var durability = new WolverineOptions().Durability;
        MessageStoreSettings.Apply(durability, Config(("Wolverine:Solo", "false")));

        durability.Mode.Should().Be(DurabilityMode.Balanced);
    }

    [Fact]
    public void The_message_store_connection_string_is_tuned_like_EFs()
    {
        var tuned = new NpgsqlConnectionStringBuilder(
            DatabasePool.Tune("Host=db;Database=lilia;Username=u;Minimum Pool Size=3"));

        tuned.MinPoolSize.Should().Be(0);
        tuned.ConnectionIdleLifetime.Should().BeLessThanOrEqualTo(30);
    }
}
