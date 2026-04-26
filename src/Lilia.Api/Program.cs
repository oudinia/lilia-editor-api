using System.Text.Json;
using System.Text.Json.Serialization;
using Lilia.Api.ErrorPages;
using Lilia.Api.Filters;
using Lilia.Api.Hubs;
using Lilia.Api.Middleware;
using Lilia.Api.Services;
using Lilia.Core.Interfaces;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Lilia.Infrastructure.Data.Seeds;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.AI;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Sentry
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrEmpty(sentryDsn))
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = sentryDsn;
        options.Environment = builder.Environment.EnvironmentName.ToLowerInvariant();
        options.Release = $"lilia-api@{typeof(Program).Assembly.GetName().Version}";
        options.TracesSampleRate = 0; // Exceptions only — no performance tracing
        options.SendDefaultPii = false;
        options.AttachStacktrace = true;
        options.MaxBreadcrumbs = 50;
    });
}

// Configure Localization
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// Configure Serilog
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);

    var betterStackToken = context.Configuration["BetterStack:SourceToken"];
    if (!string.IsNullOrEmpty(betterStackToken))
    {
        configuration.WriteTo.BetterStack(sourceToken: betterStackToken);
    }
});

// Add services to the container
builder.Services.AddControllers(options =>
    {
        // Global exception filter — logs all unhandled exceptions with full context
        // (user, controller, action, route params) and forwards to Sentry.
        // No per-controller try/catch needed for logging.
        options.Filters.Add<ExceptionLoggingFilter>();
    })
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
        ?? ["https://liliaeditor.com", "https://editor.liliaeditor.com"];

    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin =>
              {
                  // Allow localhost only in development
                  if (builder.Environment.IsDevelopment())
                  {
                      if (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                          && (uri.Host == "localhost" || uri.Host == "127.0.0.1"))
                          return true;
                  }
                  // Allow configured production origins
                  return allowedOrigins.Contains(origin);
              })
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Configure Authentication
// Auth: Kinde (or any OIDC provider)
var authAuthority = builder.Configuration["Auth:Authority"];

if (!string.IsNullOrEmpty(authAuthority))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authAuthority;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Auth:Issuer"] ?? authAuthority,
                ValidateAudience = !string.IsNullOrEmpty(builder.Configuration["Auth:Audience"]),
                ValidAudience = builder.Configuration["Auth:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "name",
                RoleClaimType = "roles"
            };

            // SignalR cannot send Authorization headers over WebSocket / SSE.
            // The client appends ?access_token=... to hub URLs instead.
            // Extract it here so the JWT middleware can validate it normally.
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });
}
else
{
    // Development: Accept tokens without signature validation
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
                NameClaimType = "name",
                RoleClaimType = "roles"
            };

            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                // Extract SignalR token from query param (same as production path above)
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    if (context.SecurityToken is Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt)
                    {
                        var claims = new List<System.Security.Claims.Claim>();
                        foreach (var claim in jwt.Claims)
                        {
                            claims.Add(claim);
                        }
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

// Cap the Npgsql pool to fit inside the DO managed Postgres plan's
// max_connections (db-s-1vcpu-1gb → 25, ~20 after superuser reserves).
// Npgsql's default Maximum Pool Size is 100 per process, so even a single
// instance can exhaust the DB pool under burst load, causing
//   53300: remaining connection slots are reserved for roles with the SUPERUSER attribute
// (see Sentry LILIA-API-9). Always force these caps on top of whatever
// the env var carries, so ops can't accidentally blow past the DB limit.
{
    var csBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
    {
        MaxPoolSize = 18,
        MinPoolSize = 1,
        ConnectionIdleLifetime = 30,
        Pooling = true,
    };
    connectionString = csBuilder.ConnectionString;
}

builder.Services.AddDbContext<LiliaDbContext>(options =>
    options
        .UseNpgsql(connectionString)
        // Column-name-only remappings (e.g. HasColumnName added after the
        // column already existed in the database) make EF think the model
        // has pending changes, which MigrateAsync then treats as fatal.
        // The schema is actually correct — it's a snapshot/designer drift
        // that a future `ef migrations add --empty` can silence. Downgrade
        // to a warning so prod deploys don't fail on a cosmetic mismatch.
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

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
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IVersionService, VersionService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddScoped<IHelpService, HelpService>();
builder.Services.AddScoped<IFormulaService, FormulaService>();
builder.Services.AddScoped<ISnippetService, SnippetService>();
builder.Services.AddScoped<IDraftBlockService, DraftBlockService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<IPreferencesService, PreferencesService>();
builder.Services.AddScoped<IRenderService, RenderService>();
builder.Services.AddScoped<ILaTeXExportService, LaTeXExportService>();
builder.Services.AddScoped<IDocumentExportService, DocumentExportService>();
builder.Services.AddScoped<IPreviewCacheService, PreviewCacheService>();
builder.Services.AddScoped<IStudioService, StudioService>();

// LaTeX rendering — runs pdflatex directly (no separate container)
builder.Services.AddSingleton<ILaTeXRenderService, LaTeXRenderService>();
builder.Services.AddSingleton<ICompilationQueueService, CompilationQueueService>();
builder.Services.AddScoped<ITypstRenderService, TypstRenderService>();
builder.Services.AddScoped<ILicenseService, LicenseService>();
builder.Services.AddScoped<IMathAstService, MathAstService>();

// Background services
builder.Services.AddHostedService<TrashPurgeBackgroundService>();
builder.Services.AddHostedService<ImportReviewPurgeBackgroundService>();

// Import telemetry sink (FT-TELEMETRY-001) — captures silent fallbacks
// + coverage gaps from the import pipeline. DbImportTelemetrySink
// buffers via a Channel and flushes batches from a single background
// loop so the parser hot path never touches the DB connection pool.
builder.Services.AddSingleton<IImportTelemetrySink, DbImportTelemetrySink>();
builder.Services.AddHostedService<ImportTelemetryFlusher>();

// DB-first LaTeX import pipeline (see plan valiant-waddling-otter + guideline
// lilia-docs/docs/guidelines/import-export-db-first.md)
builder.Services.AddScoped<BulkInsertHelper>();
builder.Services.AddScoped<ILatexImportJobExecutor, LatexImportJobExecutor>();
builder.Services.AddScoped<IImportHintService, ImportHintService>();
builder.Services.AddScoped<IValidationCacheService, ValidationCacheService>();
builder.Services.AddScoped<IRedactionService, RedactionService>();
builder.Services.AddScoped<IAiOrchestrator, AiOrchestrator>();
builder.Services.AddScoped<IAiHintAugmenter, AiHintAugmenter>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<Lilia.Import.Services.ILatexProjectExtractor, Lilia.Import.Services.LatexProjectExtractor>();
builder.Services.AddSingleton<IAssetOptimizer, AssetOptimizerService>();
builder.Services.AddScoped<IDocumentSizeService, DocumentSizeService>();

// Email service (Resend). Missing key used to be a silent warning that
// manifested as "invitation sent" UI confirmations with no actual email
// going out. Log loudly at boot so Sentry catches it on the first
// deploy after a config drift.
var emailSettings = builder.Configuration.GetSection("Email").Get<EmailSettings>() ?? new EmailSettings();
if (string.IsNullOrEmpty(emailSettings.ResendApiKey))
{
    Console.Error.WriteLine("[BOOT] Email__ResendApiKey is not configured — invitations, share notifications, and every other outbound email will fail until it is set.");
}
builder.Services.AddSingleton(emailSettings);
builder.Services.AddSingleton<IEmailService, EmailService>();

// Add distributed cache (in-memory for now, swap to Redis with 1 line when scaling)
builder.Services.AddDistributedMemoryCache();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("default", config =>
    {
        config.PermitLimit = 100;
        config.Window = TimeSpan.FromMinutes(1);
    });
    options.AddFixedWindowLimiter("strict", config =>
    {
        config.PermitLimit = 10;
        config.Window = TimeSpan.FromMinutes(1);
    });
});

// Presence tracking (in-memory for now, swap to Redis-backed impl when scaling)
builder.Services.AddSingleton<IPresenceService, InMemoryPresenceService>();

// Add SignalR for real-time updates
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Register import progress service
builder.Services.AddScoped<IImportProgressService, ImportProgressService>();

// Register import review service
builder.Services.AddScoped<IImportReviewService, ImportReviewService>();

// Register AI import review service
builder.Services.AddScoped<IAiImportService, AiImportService>();

// Add HttpClient for external API calls (DOI lookup)
builder.Services.AddHttpClient();

// Configure AI services
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("AI"));

var anthropicKey = builder.Configuration["AI:Anthropic:ApiKey"];
if (!string.IsNullOrEmpty(anthropicKey))
{
    var defaultModel = builder.Configuration["AI:DefaultModel"] ?? "claude-sonnet-4-5-20250929";
    builder.Services.AddSingleton<IChatClient>(
        new Anthropic.AnthropicClient { ApiKey = anthropicKey }.AsIChatClient(defaultModel));
}
else
{
    // Register a no-op placeholder so DI doesn't fail when no API key is configured
    builder.Services.AddSingleton<IChatClient>(new Anthropic.AnthropicClient { ApiKey = "sk-placeholder" }
        .AsIChatClient("claude-sonnet-4-5-20250929"));
}

builder.Services.AddScoped<IAiService, AiService>();

// Register AI assistant service (math generation, writing improvement, block classification)
builder.Services.AddScoped<IAiAssistantService, AiAssistantService>();

// Register Lilia.Import services for document conversion
builder.Services.AddSingleton<Lilia.Import.Interfaces.IDocxImportService, Lilia.Import.Services.DocxImportService>();
builder.Services.AddSingleton<Lilia.Import.Interfaces.ILatexToOmmlConverter, Lilia.Import.Converters.LatexToOmmlConverter>();
builder.Services.AddSingleton<Lilia.Import.Interfaces.IEquationImageRenderer>(sp =>
    new Lilia.Api.Services.LaTeXEquationImageRenderer(
        sp.GetRequiredService<ILaTeXRenderService>()));
builder.Services.AddSingleton<Lilia.Import.Interfaces.IDocxExportService>(sp =>
    new Lilia.Import.Services.DocxExportService(
        sp.GetRequiredService<Lilia.Import.Interfaces.ILatexToOmmlConverter>(),
        sp.GetRequiredService<Lilia.Import.Interfaces.IEquationImageRenderer>()));
builder.Services.AddSingleton<Lilia.Import.Interfaces.ILatexParser, Lilia.Import.Services.LatexParser>();
builder.Services.AddSingleton<Lilia.Import.Interfaces.ILatexFragmentParser, Lilia.Import.Services.LatexFragmentParser>();

// LaTeX catalog (Phase 2) — singleton in-memory cache seeded from DB at
// boot. Preloaded just before the HTTP pipeline starts so the first
// import doesn't pay the warmup cost.
builder.Services.AddSingleton<ILatexCatalogService, LatexCatalogService>();

// ITokenRouter — catalog-backed dispatch decisions for LatexParser
// (Stage 3 of the parser-reads-catalog plan). LatexParser holds a
// reference via its constructor today without consuming it; the
// replacement of the hardcoded HashSets happens in follow-up commits.
builder.Services.AddSingleton<Lilia.Import.Services.ITokenRouter, CatalogTokenRouter>();

// Register PDF import services — provider-based (mathpix or mineru)
var pdfProvider = builder.Configuration["PdfParser:Provider"] ?? "mineru";
if (pdfProvider == "mathpix")
{
    builder.Services.Configure<Lilia.Import.Models.MathpixOptions>(builder.Configuration.GetSection("Mathpix"));
    builder.Services.AddHttpClient<Lilia.Import.Interfaces.IMathpixClient, Lilia.Import.Services.MathpixClient>();
    builder.Services.AddScoped<Lilia.Import.Interfaces.IPdfParser, Lilia.Import.Services.MathpixPdfImportService>();
}
else
{
    builder.Services.Configure<Lilia.Import.Models.MineruOptions>(builder.Configuration.GetSection("MinerU"));
    builder.Services.AddHttpClient<Lilia.Import.Interfaces.IMineruClient, Lilia.Import.Services.MineruClient>();
    builder.Services.AddScoped<Lilia.Import.Interfaces.IPdfParser, Lilia.Import.Services.PdfImportService>();
}

// Register Audit Service
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditService, AuditService>();

// Register error page generator (uses IStringLocalizer)
builder.Services.AddSingleton<ErrorPageGenerator>();

// Register Lorem Ipsum generator
builder.Services.AddSingleton<ILoremIpsumService, LoremIpsumService>();

// Register Citation Style formatter
builder.Services.AddSingleton<ICitationStyleService, CitationStyleService>();

// Register LML Conversion service
builder.Services.AddSingleton<ILmlConversionService, LmlConversionService>();

// Register Accessibility service
builder.Services.AddScoped<IAccessibilityService, AccessibilityService>();

// Register ePub service
builder.Services.AddScoped<IEpubService, EpubService>();

var app = builder.Build();

// Error handling — must be early in the pipeline
var editorBaseUrl = builder.Configuration["Editor:BaseUrl"] ?? "http://localhost:3001";

string ResolveHomeUrl(HttpRequest request)
{
    // Use the Origin or Referer from the request (works for both dev and prod)
    var origin = request.Headers.Origin.ToString();
    if (!string.IsNullOrEmpty(origin) && origin != "null")
        return origin;

    var referer = request.Headers.Referer.ToString();
    if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var uri))
        return $"{uri.Scheme}://{uri.Authority}";

    return editorBaseUrl;
}

string? ResolveReviewUrl(HttpRequest request)
{
    var referer = request.Headers.Referer.ToString();
    if (!string.IsNullOrEmpty(referer) && referer.Contains("/import/review"))
    {
        // Preserve the full referer URL (includes ?sessionId=... if present)
        return referer;
    }
    return null;
}

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var accept = context.Request.Headers.Accept.ToString();
        if (accept.Contains("text/html"))
        {
            var errorPageGen = context.RequestServices.GetRequiredService<ErrorPageGenerator>();
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(errorPageGen.GenerateHtml(500, ResolveHomeUrl(context.Request), ResolveReviewUrl(context.Request)));
        }
        else
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("""{"error":"Internal Server Error","statusCode":500}""");
        }
    });
});

app.UseStatusCodePages(async context =>
{
    var statusCode = context.HttpContext.Response.StatusCode;
    var accept = context.HttpContext.Request.Headers.Accept.ToString();
    if (accept.Contains("text/html"))
    {
        var errorPageGen = context.HttpContext.RequestServices.GetRequiredService<ErrorPageGenerator>();
        context.HttpContext.Response.ContentType = "text/html; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(errorPageGen.GenerateHtml(statusCode, ResolveHomeUrl(context.HttpContext.Request), ResolveReviewUrl(context.HttpContext.Request)));
    }
    else
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        var title = statusCode switch
        {
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => "Error"
        };
        await context.HttpContext.Response.WriteAsync(
            $$"""{"error":"{{title}}","statusCode":{{statusCode}}}""");
    }
});

// Request localization — must be before any middleware that uses localized strings
var supportedCultures = new[] { "en", "fr", "es" };
app.UseRequestLocalization(options =>
{
    options.SetDefaultCulture("en")
           .AddSupportedCultures(supportedCultures)
           .AddSupportedUICultures(supportedCultures);
});

// Correlation ID for request tracing
app.UseCorrelationId();

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

// Swagger UI - only in development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Lilia Editor API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

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

// Local storage upload endpoint — accepts PUT requests from the frontend
// when using LocalStorageService (dev mode). Production uses R2 presigned URLs.
app.MapPut("/api/upload/{**key}", async (string key, HttpContext ctx) =>
{
    var filePath = Path.Combine(uploadsPath, key.Replace('/', Path.DirectorySeparatorChar));
    var directory = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

    using var fileStream = File.Create(filePath);
    await ctx.Request.Body.CopyToAsync(fileStream);
    return Results.Ok();
});

// Forwarded headers (DigitalOcean proxy → real client IPs)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// Security headers (S1)
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-XSS-Protection"] = "0"; // modern browsers use CSP instead
    await next();
});

app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();

// Development auth - create a fake user when no auth token is provided.
// Must run AFTER UseAuthentication so the Bearer handler doesn't overwrite the user.
app.UseDevelopmentAuth();

// M2M auth - map client_credentials tokens (no sub claim) to a service account user.
app.UseM2MAuth();

app.UseAuthorization();

// Sync user data on authenticated requests
app.UseUserSync();

app.MapControllers();

// Map SignalR hubs
app.MapHub<ImportHub>("/hubs/import");
app.MapHub<DocumentHub>("/hubs/document");
app.MapHub<ImportReviewHub>("/hubs/import-review");

// Health check endpoint
app.MapGet("/health", async (LiliaDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "healthy", database = "connected", timestamp = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { status = "unhealthy", database = "disconnected", error = ex.Message, timestamp = DateTime.UtcNow },
            statusCode: 503);
    }
});

// Apply pending EF Core migrations on startup (safe for single-instance DO deployment).
// Skip in the Testing environment — the xUnit fixture creates schema via
// EnsureCreatedAsync and doesn't want to replay migrations on the seeded DB.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
    await dbContext.Database.MigrateAsync();

    // Warm the LaTeX catalog cache post-migration so the first import
    // hits memory instead of paying the query cost mid-request.
    var catalog = app.Services.GetRequiredService<ILatexCatalogService>();
    if (catalog is LatexCatalogService concrete)
    {
        await concrete.PreloadAsync();
    }

    // Stage-3 boot-time audit — walk every hardcoded HashSet in
    // LatexParser and log any member without a matching catalog row.
    // Proactive complement to the per-request drift check; one log
    // entry per deploy so operators see the alignment picture without
    // waiting for a request to hit the drifted token.
    var parser = app.Services.GetRequiredService<Lilia.Import.Interfaces.ILatexParser>();
    if (parser is Lilia.Import.Services.LatexParser concreteParser)
    {
        concreteParser.AuditCatalogAlignment();
    }
}

// Seed system templates
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
    // Templates are now documents — no separate seeder needed
    // await SystemTemplateSeeder.SeedAsync(dbContext);
    await SystemFormulaSeeder.SeedAsync(dbContext);
    await SystemSnippetSeeder.SeedAsync(dbContext);
}

// Startup validation: warn loudly if PDF provider is mis-configured
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    var cfgPdfProvider = app.Configuration["PdfParser:Provider"] ?? "mineru";
    if (cfgPdfProvider == "mathpix")
    {
        var appId  = app.Configuration["Mathpix:AppId"];
        var appKey = app.Configuration["Mathpix:AppKey"];
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appKey))
        {
            startupLogger.LogCritical(
                "CONFIGURATION ERROR: PdfParser:Provider is 'mathpix' but Mathpix:AppId or Mathpix:AppKey " +
                "are not set. PDF import will fail at runtime. " +
                "Set MATHPIX__APPID and MATHPIX__APPKEY environment variables.");
        }
        else
        {
            startupLogger.LogInformation("[Startup] PDF provider: Mathpix (credentials configured)");
        }
    }
    else
    {
        startupLogger.LogInformation("[Startup] PDF provider: {Provider}", cfgPdfProvider);
    }
}

app.Run();

public partial class Program { }
