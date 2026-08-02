using Lilia.Engines;
using Lilia.Import.Interfaces;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Lilia.Tools.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Tools.Api;

/// <summary>
/// The standalone tools host.
///
/// <para>Written with an explicit <c>Main</c> rather than top-level statements on
/// purpose: those emit a <c>Program</c> class into the *global* namespace, and a
/// second host in the same solution then collides with the editor's — every
/// <c>WebApplicationFactory&lt;Program&gt;</c> in the suite fails to compile as
/// soon as a test project references both assemblies. A namespaced entry point
/// keeps the two hosts independent, which is the whole point of the split.</para>
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Same committed-secrets shim as Lilia.Api, and the same guard. This file
        // is added after CreateBuilder, so it outranks every other source —
        // including settings an integration test injects on the test host. In
        // Lilia.Api that silently pointed the Stytch webhook test at the real API
        // instead of its local stub. Any host loading this file inherits the trap,
        // so the guard travels with it.
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            builder.Configuration.AddJsonFile("local-dev.secrets.json", optional: true, reloadOnChange: false);
        }

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        // Read-mostly against the shared database: the registry is cached at boot
        // and the write path is a handful of small event/artifact rows.
        builder.Services.AddDbContext<LiliaDbContext>(options =>
            options
                .UseNpgsql(connectionString)
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

        // ── engines, shared with the editor by project reference ──────────────
        builder.Services.AddHttpClient();
        builder.Services.AddScoped<IRenderService, RenderService>();
        builder.Services.AddScoped<IBibliographyService, BibliographyService>();
        builder.Services.AddScoped<IDocxImportService, DocxImportService>();
        builder.Services.AddSingleton<ICompilationQueueService, CompilationQueueService>();

        // ── the tools surface ─────────────────────────────────────────────────
        builder.Services.AddSingleton<IToolCatalogService, ToolCatalogService>();
        builder.Services.AddScoped<IToolRunnerService, ToolRunnerService>();
        builder.Services.AddScoped<IEntitlementService, EntitlementService>();

        builder.Services.AddControllers();

        // The browser never reaches this host directly — lilia-cloud's BFF proxies
        // with the anon identity — so no CORS policy is opened here on purpose.
        builder.Services.AddHealthChecks();

        var app = builder.Build();

        app.UseRouting();
        app.MapControllers();
        app.MapHealthChecks("/health");

        // The registry is DB-authoritative and rebuilt on boot, as in the editor.
        // A failed preload should not take the host down: /tools then serves empty
        // and the next request reloads, which beats a crash loop on a cold database.
        using (var scope = app.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IToolCatalogService>();
            try
            {
                await catalog.PreloadAsync();
            }
            catch (Exception ex)
            {
                app.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Startup")
                    .LogError(ex, "[Tools] registry preload failed; serving empty until it reloads");
            }
        }

        await app.RunAsync();
    }
}
