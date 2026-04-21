using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.Infrastructure;

public class LiliaWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public LiliaWebApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:LiliaCore", _connectionString);
        builder.UseSetting("Auth:SecretKey", "test_secret_key_to_disable_dev_auth");
        builder.UseSetting("Auth:Authority", "https://test.lilia.test");
        builder.UseSetting("Storage:LocalPath", Path.Combine(Path.GetTempPath(), "lilia-tests", Guid.NewGuid().ToString()));

        builder.ConfigureTestServices(services =>
        {
            // Remove existing DbContext registration
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<LiliaDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<LiliaDbContext>(options => options
                .UseNpgsql(_connectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

            // Replace auth with test scheme
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Force the default scheme to TestScheme (AddAuthentication above may not override
            // the default because the auth system was already configured in Program.cs)
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            });

            // Replace user service with no-op for tests
            var userServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IUserService));
            if (userServiceDescriptor != null) services.Remove(userServiceDescriptor);
            services.AddSingleton<IUserService, NoOpUserService>();
        });
    }
}

public class NoOpUserService : IUserService
{
    public Task<UserDto?> GetUserAsync(string userId) => Task.FromResult<UserDto?>(null);
    public Task<UserDto> CreateOrUpdateUserAsync(CreateOrUpdateUserDto dto) =>
        Task.FromResult(new UserDto(dto.Id, dto.Email, dto.Name, dto.Image, DateTime.UtcNow));
    public Task<UserDto?> GetUserByEmailAsync(string email) => Task.FromResult<UserDto?>(null);
}
