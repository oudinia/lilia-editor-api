using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Lilia.Admin.Security;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lilia.Admin.Tests;

/// <summary>Signing keys the test controls, standing in for Cloudflare Access's.</summary>
public sealed class TestAccessKeys : IAccessKeys
{
    public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };
    public IEnumerable<SecurityKey> Current(string? kid) => [Key];
}

/// <summary>
/// The admin console against a real Postgres (Testcontainers, migrated like production), with
/// Cloudflare Access replaced by keys the test signs with. Tokens are minted like Access's:
/// issuer = the team domain, audience = the Access app's tag, an email claim.
/// </summary>
public sealed class AdminFixture : IAsyncLifetime
{
    public const string Team = "http://127.0.0.1:9";   // refused at once if the real key fetch is tried
    public const string Audience = "admin-aud-test";
    public const string AdminEmail = "admin@lilia.test";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder().WithImage("postgres:16").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ConnectionString => _db.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await using (var ctx = CreateDbContext()) await ctx.Database.MigrateAsync();
        Factory = Create("Testing", withAccess: true);
    }

    public WebApplicationFactory<Program> Create(string environment, bool withAccess) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Database:Target", "local");
            b.UseSetting("ConnectionStrings:LiliaCore", ConnectionString);
            b.UseSetting("Admin:Emails", AdminEmail);
            if (withAccess)
            {
                b.UseSetting("Access:TeamDomain", Team);
                b.UseSetting("Access:Audience", Audience);
            }
            b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IAccessKeys, TestAccessKeys>()));
        });

    public LiliaDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<LiliaDbContext>().UseNpgsql(ConnectionString).Options);

    public static string Token(string email, string? audience = Audience, string issuer = Team,
        SecurityKey? key = null, DateTime? expires = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var token = handler.CreateJwtSecurityToken(
            issuer: issuer, audience: audience,
            subject: new ClaimsIdentity([new Claim("email", email)]),
            notBefore: (expires ?? now.AddMinutes(10)).AddMinutes(-30),
            expires: expires ?? now.AddMinutes(10),
            issuedAt: (expires ?? now.AddMinutes(10)).AddMinutes(-30),
            signingCredentials: new SigningCredentials(key ?? TestAccessKeys.Key, SecurityAlgorithms.RsaSha256));
        return handler.WriteToken(token);
    }

    public HttpClient Client(string? token)
    {
        var c = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (token is not null) c.DefaultRequestHeaders.Add(AdminAccess.Header, token);
        return c;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition("Admin")]
public sealed class AdminCollection : ICollectionFixture<AdminFixture>;
