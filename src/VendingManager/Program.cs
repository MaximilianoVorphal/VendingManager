using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using VendingManager.Core.Configuration;
using VendingManager.Infrastructure.Data;
using VendingManager.Infrastructure.Data.Repositories;
using VendingManager.Infrastructure.Interceptors;
using VendingManager.Infrastructure.Services;
using VendingManager.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Globalization;
using VendingManager.Shared.Constants;
using VendingManager.Web.Middleware;
using VendingManager.Web.ModelBinders;

// Forzar cultura chilena ($ pesos, fechas dd/MM/yyyy)
// Usamos InvariantCulture como base porque es-CL puede no estar instalada en la imagen Docker
var culturaChilena = (CultureInfo)CultureInfo.InvariantCulture.Clone();
culturaChilena.NumberFormat.CurrencySymbol = "$";
culturaChilena.NumberFormat.CurrencyPositivePattern = 0;       // $n
culturaChilena.NumberFormat.CurrencyNegativePattern = 1;       // -$n
culturaChilena.NumberFormat.CurrencyDecimalSeparator = ".";
culturaChilena.NumberFormat.CurrencyGroupSeparator = ".";
culturaChilena.NumberFormat.NumberDecimalSeparator = ".";
culturaChilena.NumberFormat.NumberGroupSeparator = ".";
culturaChilena.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
culturaChilena.DateTimeFormat.LongDatePattern = "dd/MM/yyyy";
culturaChilena.DateTimeFormat.ShortTimePattern = "HH:mm";
CultureInfo.DefaultThreadCurrentCulture = culturaChilena;
CultureInfo.DefaultThreadCurrentUICulture = culturaChilena;

var builder = WebApplication.CreateBuilder(args);

// User-secrets para desarrollo local — sobreescribe variables de entorno del .env
// Docker Compose sigue usando .env para contenedores, pero dotnet run usa user-secrets
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// 0. Serilog — structured JSON logging, coexisting with ILogger<T>
builder.Logging.ClearProviders();
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatter: new RenderedCompactJsonFormatter())
    .CreateLogger();
builder.Host.UseSerilog();

// Memory Cache (requerido por CajaBusinessService, SalesAnalyticsService, PurchasingService)
builder.Services.AddMemoryCache();

// 1. Configurar CORS (DEBE ir ANTES de AddControllers)
// M-5: In Production, CORS origin is read from config (Cors:AllowedOrigin) and must use https://.
// In Development, localhost origins (https + http) are allowed for local tooling convenience.
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirBlazor", policy =>
    {
        string[] origins;
        if (builder.Environment.IsDevelopment())
        {
            origins = new[]
            {
                "https://localhost:5091",
                "http://localhost:5091",
                "https://localhost:5093",
                "http://localhost:5093",
                "http://localhost:5095",
                "https://localhost:5095"
            };
        }
        else
        {
            // TODO(operator): set Cors:AllowedOrigin in appsettings.Production.json to the real production https origin.
            var prodOrigin = builder.Configuration["Cors:AllowedOrigin"]
                ?? throw new InvalidOperationException("Cors:AllowedOrigin must be configured for non-Development environments.");
            if (!prodOrigin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cors:AllowedOrigin must use https:// in Production. Got: {prodOrigin}");
            origins = new[] { prodOrigin };
        }

        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // H-1: partition the login limiter per client IP so one client cannot exhaust a
    // global bucket (DoS) and brute-force is throttled per source. Behind a reverse
    // proxy, configure ForwardedHeaders so RemoteIpAddress reflects the real client.
    options.AddPolicy("LoginPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 5,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
});

// Auth
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "auth_token";
        options.LoginPath = "/login";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole(Roles.Admin));
});

// 2. Base de Datos (SQL Express)
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AuditSaveChangesInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
    options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
});

// 2.1 Configuration — VendingConfig
builder.Services.Configure<VendingConfig>(builder.Configuration.GetSection("VendingConfig"));
builder.Services.Configure<AnalyticsThresholds>(builder.Configuration.GetSection("AnalyticsThresholds"));

// 3. Registrar tus Servicios (Clean Architecture)
builder.Services.AddScoped<ISyncOrchestratorService, SyncOrchestratorService>();
builder.Services.AddScoped<ISalesImportService, SalesImportService>();
builder.Services.AddScoped<ICatalogExcelService, CatalogExcelService>();
builder.Services.AddScoped<IOrdenCargaExcelService, OrdenCargaExcelService>();
builder.Services.AddScoped<ICajaService, CajaService>();
builder.Services.AddScoped<CajaBusinessService>();
builder.Services.AddScoped<IVentasService, VentasService>();
builder.Services.AddScoped<ISalesAnalyticsService, SalesAnalyticsService>();
builder.Services.AddScoped<IPurchasingService, PurchasingService>();
builder.Services.AddScoped<IInventarioService, InventarioService>();
builder.Services.AddScoped<IMaquinaService, MaquinaService>();
builder.Services.AddScoped<IInformesService, InformesService>();
builder.Services.AddScoped<VendingManager.Core.Interfaces.IOrdenCargaService, OrdenCargaService>();
builder.Services.AddScoped<ITemplateRecargaService, TemplateRecargaService>();
builder.Services.AddScoped<ITemplateRecargaLifecycleService, TemplateRecargaLifecycleService>();
builder.Services.AddScoped<ITemplateRecargaAnalyticsService, TemplateRecargaAnalyticsService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IUploadPathProvider, DefaultUploadPathProvider>();
builder.Services.AddScoped<ICompraService, CompraService>();
builder.Services.AddScoped<IGastoRecurrenteService, GastoRecurrenteService>();
builder.Services.AddScoped<ITransferenciaService, TransferenciaService>();
builder.Services.AddScoped<IRendicionService, RendicionService>();
builder.Services.AddScoped<IContabilidadService, ContabilidadService>();
builder.Services.AddScoped<IExcelExportService, ExcelExportService>();
builder.Services.AddScoped<IVentaRepository, VentaRepository>();
builder.Services.AddScoped<IMaquinaRepository, MaquinaRepository>();
builder.Services.AddScoped<IAccountingPeriodRepository, AccountingPeriodRepository>();
builder.Services.AddScoped<IProductoEANRepository, ProductoEANRepository>();
builder.Services.AddScoped<IProductMatchingService, ProductMatchingService>();
builder.Services.AddScoped<IIntegrityCheckService, IntegrityCheckService>();
builder.Services.AddScoped<IFileContentValidator, FileContentValidator>(); // M-1: magic-byte upload validation
builder.Services.AddHttpClient<IFacturaOcrService, FacturaOcrService>();
builder.Services.AddHttpClient<IRecargaOcrService, RecargaOcrService>();
// Servicios en segundo plano (Background Workers)
builder.Services.AddHttpClient<VendingManager.Core.Interfaces.IScraperClient, VendingManager.Infrastructure.Clients.ScraperClient>();
builder.Services.AddHostedService<AutomatedReportService>();

// 4. Configuración Blazor
builder.Services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new DateTimeModelBinderProvider());
});
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<VendingManager.Web.Auth.PersistingAuthenticationStateProvider>();

builder.Services.AddOpenApi();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream" });
});

// Registrar HttpClient para Pre-rendering (Server Side)
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:8080") });

// 5. Health Checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("App is running"), tags: ["liveness"])
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found."),
        name: "sqlserver",
        tags: ["db", "readiness"]);

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<VendingManager.Infrastructure.Data.ApplicationDbContext>();
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating the database.");
    }

    // Seed admin user — OUTSIDE the migration try/catch so failures are not silently swallowed.
    // C-1: No hardcoded password. SEED_ADMIN_PASSWORD env var is the sole password source.
    try
    {
        var context = services.GetRequiredService<VendingManager.Infrastructure.Data.ApplicationDbContext>();
        if (!context.Users.Any())
        {
            var seedPassword = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");

            if (!string.IsNullOrEmpty(seedPassword))
            {
                // Seed with the provided password in any environment.
                var adminUser = new VendingManager.Core.Entities.User
                {
                    Username = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(seedPassword),
                    Role = Roles.Admin
                };
                context.Users.Add(adminUser);
                context.SaveChanges();
                logger.LogInformation("Admin user seeded successfully.");
            }
            else if (app.Environment.IsDevelopment())
            {
                // Development: skip seeding, warn the operator.
                logger.LogWarning("Skipping admin seed: SEED_ADMIN_PASSWORD not set (Development).");
            }
            else
            {
                // Production (or any non-Development env): fail loud so the deployment is blocked.
                logger.LogCritical("SEED_ADMIN_PASSWORD must be set to seed the initial admin in Production.");
                throw new InvalidOperationException(
                    "SEED_ADMIN_PASSWORD must be set to seed the initial admin in Production.");
            }
        }
    }
    catch (InvalidOperationException)
    {
        // Re-throw so startup halts. This is intentional (C-1 fail-loud in Production).
        throw;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while seeding the admin user.");
    }
}

// Configuración de entorno
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    app.UseExceptionHandler("/Error");
    // Personalizar HSTS para producción
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseResponseCompression();
app.UseCors("PermitirBlazor"); // Activar CORS

app.UseRateLimiter(); // Aplicar Rate Limiting
app.UseAuthentication();
app.UseAuthorization();

// 6. Serilog request logging middleware (captures HTTP request duration)
app.UseSerilogRequestLogging();

// 7. Global ProblemDetails middleware (after routing, before MapControllers)
app.UseMiddleware<GlobalProblemDetailsMiddleware>();

// 8. Map health check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // liveness: only self-check (always passes if app is running)
});
// M-2: /health/db is protected — anonymous callers must not see DB connectivity status.
app.MapHealthChecks("/health/db", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("db") // readiness: SQL Server check
}).RequireAuthorization("RequireAdmin");

// 9. Mapear componentes y API
app.MapStaticAssets();
app.MapControllers();

app.MapRazorComponents<VendingManager.Components.App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(VendingManager.Web.App).Assembly);

app.Run();

// Expose Program as a partial class so tests can use WebApplicationFactory<Program>.
public partial class Program { }
