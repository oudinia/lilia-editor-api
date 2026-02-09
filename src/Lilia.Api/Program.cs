using System.Text.Json;
using System.Text.Json.Serialization;
using Lilia.Api.Hubs;
using Lilia.Api.Middleware;
using Lilia.Api.Services;
using Lilia.Core.Interfaces;
using Lilia.Infrastructure.Data;
using Lilia.Infrastructure.Data.Seeds;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);
});

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // Accept camelCase from frontend
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Lilia Editor API",
        Version = "v1",
        Description = "API for document management, blocks, bibliography, collaboration, and more."
    });
});

// Configure CORS
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["https://app.lilia.app", "https://editor.lilia.app"];

    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin =>
              {
                  // Allow any localhost/127.0.0.1 origin (any port) for development
                  if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                  {
                      if (uri.Host == "localhost" || uri.Host == "127.0.0.1")
                          return true;
                  }
                  // Also allow configured production origins
                  return allowedOrigins.Contains(origin);
              })
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Configure Authentication
var clerkAuthority = builder.Configuration["Clerk:Authority"];
if (!string.IsNullOrEmpty(clerkAuthority))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = clerkAuthority;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Clerk:Issuer"] ?? clerkAuthority,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });
}
else
{
    // Development: Accept Clerk tokens without signature validation
    // This allows testing with real Clerk tokens without needing the JWKS
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                ValidateIssuerSigningKey = false,
                RequireSignedTokens = false,
                SignatureValidator = (token, _) => new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token),
                // Map Clerk claims to standard claims
                NameClaimType = "name",
                RoleClaimType = "role"
            };

            // Parse and map claims from Clerk JWT
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    // Clerk JWTs have claims in the token, ensure they're accessible
                    if (context.SecurityToken is Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt)
                    {
                        var claims = new List<System.Security.Claims.Claim>();
                        foreach (var claim in jwt.Claims)
                        {
                            claims.Add(claim);
                        }

                        // Create a new identity with the JWT claims
                        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Bearer");
                        context.Principal = new System.Security.Claims.ClaimsPrincipal(identity);
                    }
                    return Task.CompletedTask;
                }
            };
        });
}

builder.Services.AddAuthorization();

// Configure Database
var connectionString = builder.Configuration.GetConnectionString("LiliaCore")
    ?? "Host=localhost;Database=lilia_core;Username=postgres;Password=postgres";

builder.Services.AddDbContext<LiliaDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configure Storage Service
if (!string.IsNullOrEmpty(builder.Configuration["R2:Endpoint"]))
{
    builder.Services.AddSingleton<IStorageService, R2StorageService>();
}
else
{
    builder.Services.AddSingleton<IStorageService, LocalStorageService>();
}

// Register Application Services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddSingleton<IBlockTypeService, BlockTypeService>();
builder.Services.AddScoped<IBlockService, BlockService>();
builder.Services.AddScoped<IBibliographyService, BibliographyService>();
builder.Services.AddScoped<ILabelService, LabelService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<ICollaboratorService, CollaboratorService>();
builder.Services.AddScoped<IVersionService, VersionService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddScoped<IFormulaService, FormulaService>();
builder.Services.AddScoped<ISnippetService, SnippetService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<IPreferencesService, PreferencesService>();
builder.Services.AddScoped<IRenderService, RenderService>();
builder.Services.AddScoped<IPreviewCacheService, PreviewCacheService>();

// Add distributed cache for preview caching (in-memory for now, can upgrade to Redis later)
builder.Services.AddDistributedMemoryCache();

// Add SignalR for real-time updates
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Register import progress service
builder.Services.AddScoped<IImportProgressService, ImportProgressService>();

// Register Clerk service for fetching user data from Clerk API
builder.Services.AddHttpClient<IClerkService, ClerkService>();

// Add HttpClient for external API calls (DOI lookup)
builder.Services.AddHttpClient();

// Register Lilia.Import services for document conversion
builder.Services.AddSingleton<Lilia.Import.Interfaces.IDocxImportService, Lilia.Import.Services.DocxImportService>();
builder.Services.AddSingleton<Lilia.Import.Interfaces.IDocxExportService, Lilia.Import.Services.DocxExportService>();

// Register Lorem Ipsum generator
builder.Services.AddSingleton<ILoremIpsumService, LoremIpsumService>();

// Register Citation Style formatter
builder.Services.AddSingleton<ICitationStyleService, CitationStyleService>();

// Register LML Conversion service
builder.Services.AddSingleton<ILmlConversionService, LmlConversionService>();

var app = builder.Build();

// Request logging with enhanced diagnostic context
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
        diagnosticContext.Set("ContentLength", httpContext.Request.ContentLength);

        // Add user ID if authenticated
        var userId = httpContext.User?.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            diagnosticContext.Set("UserId", userId);
        }
    };

    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
});

// Swagger UI - always enabled for now
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Lilia Editor API v1");
    c.RoutePrefix = string.Empty; // Serve Swagger UI at root
});

// Serve static files for local storage uploads
var uploadsPath = builder.Configuration["Storage:LocalPath"]
    ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
uploadsPath = Path.GetFullPath(uploadsPath);
Directory.CreateDirectory(uploadsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseCors();

// Development auth - create a fake user when no auth token is provided
app.UseDevelopmentAuth();

app.UseAuthentication();
app.UseAuthorization();

// Sync Clerk user data on authenticated requests
app.UseClerkUserSync();

app.MapControllers();

// Map SignalR hubs
app.MapHub<ImportHub>("/hubs/import");
app.MapHub<DocumentHub>("/hubs/document");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Seed system templates
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
    await SystemTemplateSeeder.SeedAsync(dbContext);
    await SystemFormulaSeeder.SeedAsync(dbContext);
    await SystemSnippetSeeder.SeedAsync(dbContext);
}

app.Run();
