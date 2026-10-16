using Lilia.Admin.Components;
using Lilia.Admin.Data;
using Lilia.Admin.Security;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// The admin and support console (16 Oct 2026): what happens in Lilia, for the operator. Its own
// process on 127.0.0.1, reached at admin.liliaeditor.com through the tunnel, behind Cloudflare
// Access; it reads the API's database. See Security/AdminAccess for who may open it.
var builder = WebApplication.CreateBuilder(args);

// The API's user secrets hold the connection strings; load them in every environment, like the API.
builder.Configuration.AddUserSecrets<Program>(optional: true);

var database = DatabaseTarget.Resolve(builder.Configuration);
Console.WriteLine($"[BOOT] admin database target: {database}");
// A handful of connections: the console is one operator, and Neon's pool is shared with the API.
var connection = new NpgsqlConnectionStringBuilder(database.ConnectionString) { MaxPoolSize = 5, ApplicationName = "lilia-admin" };
builder.Services.AddDbContextFactory<LiliaDbContext>(o => o
    .UseNpgsql(connection.ConnectionString)
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AdminQueries>();
builder.Services.AddScoped<AdminStats>();

builder.AddAdminAccess();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>For WebApplicationFactory in the tests.</summary>
public partial class Program;
