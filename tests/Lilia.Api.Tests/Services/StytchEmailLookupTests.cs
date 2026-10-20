using System.Security.Claims;
using FluentAssertions;
using Lilia.Api.Middleware;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A sign-in token without an email (Stytch's session JWT, by default): the owner's first Google
/// sign-up of 20 Oct was stored as &lt;id&gt;@auth.liliaeditor.com. Lilia asks Stytch, given its
/// secret. The user-sync step runs directly here, with an in-memory user store and a fake Stytch.
/// </summary>
public class StytchEmailLookupTests
{
    private sealed class FakeStytch : IStytchUserLookup
    {
        public Dictionary<string, StytchUserInfo> Users { get; } = new();
        public Task<StytchUserInfo?> FindAsync(string userId, CancellationToken ct = default) =>
            Task.FromResult(Users.TryGetValue(userId, out var u) ? u : null);
    }

    private sealed class Users : IUserService
    {
        public Dictionary<string, UserDto> ById { get; } = new();
        public Task<UserDto?> GetUserAsync(string userId) => Task.FromResult(ById.GetValueOrDefault(userId));
        public Task<UserDto?> GetUserByEmailAsync(string email) =>
            Task.FromResult(ById.Values.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));
        public Task<UserDto> CreateOrUpdateUserAsync(CreateOrUpdateUserDto dto)
        {
            var u = new UserDto(dto.Id, dto.Email, dto.Name ?? ById.GetValueOrDefault(dto.Id)?.Name, dto.Image, DateTime.UtcNow);
            ById[dto.Id] = u;
            return Task.FromResult(u);
        }
    }

    private readonly FakeStytch _stytch = new();
    private readonly Users _users = new();

    /// <summary>One request signed in as <paramref name="userId"/>, its token holding no email or name; a fresh sync cache each time.</summary>
    private async Task SignInAsync(string userId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId) }, "Bearer")),
        };
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var sync = new UserSyncMiddleware(_ => Task.CompletedTask, NullLogger<UserSyncMiddleware>.Instance);
        await sync.InvokeAsync(context, _users, cache, _stytch);
    }

    [Fact]
    public async Task A_new_user_without_an_email_in_the_token_gets_the_one_Stytch_holds()
    {
        _stytch.Users["user-test-ada"] = new StytchUserInfo("ada@example.org", "Ada Lovelace");
        await SignInAsync("user-test-ada");
        _users.ById["user-test-ada"].Should().Match<UserDto>(u => u.Email == "ada@example.org" && u.Name == "Ada Lovelace");
    }

    [Fact]
    public async Task A_user_still_on_the_stand_in_address_is_repaired_at_the_next_sign_in()
    {
        await SignInAsync("user-test-grace");   // Stytch can't say yet
        _users.ById["user-test-grace"].Email.Should().Be($"user-test-grace{StytchUserLookup.PlaceholderDomain}");

        _stytch.Users["user-test-grace"] = new StytchUserInfo("grace@example.org", "Grace Hopper");
        await SignInAsync("user-test-grace");
        _users.ById["user-test-grace"].Email.Should().Be("grace@example.org");
    }

    [Fact]
    public async Task An_email_another_account_already_has_stays_with_it()
    {
        _users.ById["user-test-other"] = new UserDto("user-test-other", "shared@example.org", "Other", null, DateTime.UtcNow);
        _stytch.Users["user-test-me"] = new StytchUserInfo("shared@example.org", "Me");
        await SignInAsync("user-test-me");
        _users.ById["user-test-me"].Email.Should().EndWith(StytchUserLookup.PlaceholderDomain);
        _users.ById["user-test-other"].Email.Should().Be("shared@example.org");
    }

    [Fact]
    public async Task A_user_with_a_real_email_is_left_alone()
    {
        _users.ById["user-test-kept"] = new UserDto("user-test-kept", "kept@example.org", "Kept", null, DateTime.UtcNow);
        _stytch.Users["user-test-kept"] = new StytchUserInfo("other@example.org", "Changed");
        await SignInAsync("user-test-kept");
        _users.ById["user-test-kept"].Email.Should().Be("kept@example.org");
    }

    [Fact]
    public void Stytch_answers_are_read_in_both_shapes_verified_email_first()
    {
        StytchUserLookup.Parse("""{"user_id":"user-1","emails":[{"email":"old@x.org","verified":false},{"email":"new@x.org","verified":true}],"name":{"first_name":"Ada","middle_name":"","last_name":"Lovelace"}}""")
            .Should().Be(new StytchUserInfo("new@x.org", "Ada Lovelace", EmailVerified: true));
        StytchUserLookup.Parse("""{"user":{"emails":[{"email":"only@x.org"}],"name":{"first_name":"Grace"}}}""")
            .Should().Be(new StytchUserInfo("only@x.org", "Grace"));
        StytchUserLookup.Parse("""{"user":{"emails":[]}}""").Should().BeNull();
    }

    [Fact]
    public void The_name_typed_at_sign_up_is_read_from_the_metadata()
    {
        StytchUserLookup.Parse("""{"user":{"emails":[{"email":"n@x.org","verified":true}],"name":{"first_name":""},"trusted_metadata":{"display_name":" Nadia Benali "}}}""")
            .Should().Be(new StytchUserInfo("n@x.org", "Nadia Benali", EmailVerified: true));
        StytchUserLookup.Parse("""{"user":{"emails":[{"email":"o@x.org"}],"untrusted_metadata":{"display_name":"Omar"}}}""")!
            .Name.Should().Be("Omar");
    }

    [Theory]
    [InlineData("project-test-74b2b0f7", null, "https://test.stytch.com")]
    [InlineData("project-live-ba37b474", null, "https://api.stytch.com")]
    [InlineData("project-live-ba37b474", "https://fossil-pig-0846.customers.stytch.com/", "https://fossil-pig-0846.customers.stytch.com")]
    public void Test_projects_ask_test_stytch_live_ones_api_stytch(string project, string? apiBase, string expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stytch:ProjectId"] = project,
            ["Stytch:ApiBase"] = apiBase,
        }).Build();
        StytchUserLookup.ApiBase(config).Should().Be(expected);
    }

    [Fact]
    public async Task Without_a_secret_Stytch_is_not_asked()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Stytch:ProjectId"] = "project-test-x" }).Build();
        var lookup = new StytchUserLookup(config, new NoHttp(), NullLogger<StytchUserLookup>.Instance);
        (await lookup.FindAsync("user-test-1")).Should().BeNull();
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Stytch must not be asked without a secret.");
    }
}
