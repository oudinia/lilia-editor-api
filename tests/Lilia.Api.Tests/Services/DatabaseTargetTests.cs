using FluentAssertions;
using Lilia.Infrastructure.Data;
using Microsoft.Extensions.Configuration;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Which database a host opens.
///
/// <para>This used to be decided by config precedence rather than by anyone:
/// appsettings named localhost, user secrets overrode
/// <c>ConnectionStrings:LiliaCore</c> with Neon, and secrets win — so the editor
/// ran against production on a developer machine while the tools host, having no
/// secrets file, ran against localhost. The keys are now distinct so a secret
/// cannot capture the default, and the target is a named choice.</para>
/// </summary>
public class DatabaseTargetTests
{
    private const string Local = "Host=localhost;Database=lilia;Username=lilia;Password=x";
    private const string Neon = "Host=ep-something.neon.tech;Database=neondb;Username=owner;Password=y";

    private static IConfiguration Config(params (string key, string value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.key, e => (string?)e.value))
            .Build();

    [Fact]
    public void Local_is_what_you_get_without_asking()
    {
        var resolved = DatabaseTarget.Resolve(Config(("ConnectionStrings:LiliaCore", Local)));

        resolved.Name.Should().Be("local");
        resolved.ConnectionString.Should().Be(Local);
    }

    [Fact]
    public void Neon_requires_asking_for_it()
    {
        var resolved = DatabaseTarget.Resolve(Config(
            ("ConnectionStrings:LiliaCore", Local),
            ("ConnectionStrings:LiliaCoreNeon", Neon),
            ("Database:Target", "neon")));

        resolved.Name.Should().Be("neon");
        resolved.ConnectionString.Should().Be(Neon);
    }

    [Fact]
    public void A_neon_string_present_does_not_capture_the_default()
    {
        // The whole point: having the production string configured must not be
        // enough to be pointed at production.
        var resolved = DatabaseTarget.Resolve(Config(
            ("ConnectionStrings:LiliaCore", Local),
            ("ConnectionStrings:LiliaCoreNeon", Neon)));

        resolved.ConnectionString.Should().Be(Local);
    }

    [Fact]
    public void Asking_for_neon_without_configuring_it_fails_loudly()
    {
        // Falling back to local here would be the worst outcome: a deploy that
        // believes it is on Neon and quietly writes somewhere else.
        var act = () => DatabaseTarget.Resolve(Config(
            ("ConnectionStrings:LiliaCore", Local),
            ("Database:Target", "neon")));

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*LiliaCoreNeon*");
    }

    [Theory]
    [InlineData("NEON")]
    [InlineData("  neon  ")]
    [InlineData("Local")]
    public void The_target_is_read_forgivingly(string target)
    {
        var act = () => DatabaseTarget.Resolve(Config(
            ("ConnectionStrings:LiliaCore", Local),
            ("ConnectionStrings:LiliaCoreNeon", Neon),
            ("Database:Target", target)));

        act.Should().NotThrow();
    }

    [Fact]
    public void An_unrecognised_target_is_rejected_rather_than_guessed()
    {
        var act = () => DatabaseTarget.Resolve(Config(
            ("ConnectionStrings:LiliaCore", Local),
            ("Database:Target", "staging")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*'staging'*");
    }

    [Fact]
    public void The_description_names_the_host_without_leaking_the_password()
    {
        var resolved = DatabaseTarget.Resolve(Config(("ConnectionStrings:LiliaCore", Local)));

        resolved.ToString().Should().Be("local (localhost/lilia)");
        resolved.ToString().Should().NotContain("Password");
    }
}
