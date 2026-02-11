using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Lilia.Api.Tests.Integration.Infrastructure;

public class TestDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();
    public LiliaWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Create schema from EF Core model
        var options = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var context = new LiliaDbContext(options);
        await context.Database.EnsureCreatedAsync();

        Factory = new LiliaWebApplicationFactory(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (Factory != null)
            await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}
