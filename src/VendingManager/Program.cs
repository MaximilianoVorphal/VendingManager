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
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirBlazor", policy =>
    {
        policy.WithOrigins(
                "https://localhost:5091",
                "http://localhost:5091",
                "https://localhost:5093",
                "http://localhost:5093",
                "http://localhost:5095",
                "https://localhost:5095"
            )
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("LoginPolicy", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 5;
        opt.QueueLimit = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
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
    try
    {
        var context = services.GetRequiredService<VendingManager.Infrastructure.Data.ApplicationDbContext>();
        context.Database.Migrate();

        // Crear usuario por defecto si no existe (Safety net para Dev y Prod)
        if (!context.Users.Any())
        {
            // Solo crear seed si está expresamente permitido o estrictamente en Development sin usuarios
            // Por ahora lo mantenemos simple: si no hay usuarios, crear admin via Env Var o default si Dev
            var seedPassword = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");

            if (app.Environment.IsDevelopment() || !string.IsNullOrEmpty(seedPassword))
            {
                var passwordToUse = !string.IsNullOrEmpty(seedPassword) ? seedPassword : "admin";

                var adminUser = new VendingManager.Core.Entities.User
                {
                    Username = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordToUse),
                    Role = Roles.Admin
                };
                context.Users.Add(adminUser);
                context.SaveChanges();
            }
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
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
app.MapHealthChecks("/health/db", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("db") // readiness: SQL Server check
});

// 9. Mapear componentes y API
app.MapStaticAssets();
app.MapControllers();

app.MapRazorComponents<VendingManager.Components.App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(VendingManager.Web.App).Assembly);

app.Run();
