using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Xunit;

namespace Lilia.Api.Tests.Shared;

/// <summary>
/// The API host, with no Wolverine setting at all, runs Solo: no leadership lock, no control
/// queue. The test factory no longer sets Wolverine:Solo, so this is the production default.
/// </summary>
public class MessageStoreModeTests : IntegrationTestBase
{
    public MessageStoreModeTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public void The_host_runs_Wolverine_as_a_single_node()
    {
        var options = Fixture.Factory.Services.GetRequiredService<WolverineOptions>();

        options.Durability.Mode.Should().Be(DurabilityMode.Solo);
        options.Durability.DurabilityMetricsEnabled.Should().BeFalse();
    }
}
