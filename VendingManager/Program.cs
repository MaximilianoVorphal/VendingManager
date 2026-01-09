using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using VendingManager.Infrastructure.Data;
using VendingManager.Infrastructure.Services;
using VendingManager.Web;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

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

// Auth
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "auth_token";
        options.LoginPath = "/login";
        options.Cookie.SameSite = SameSiteMode.Strict;
        // options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Uncomment if HTTPS only
    });

builder.Services.AddAuthorization();

// 2. Base de Datos (SQL Express)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3. Registrar tus Servicios (Clean Architecture)
builder.Services.AddScoped<IExcelService, ExcelService>();
builder.Services.AddScoped<ICajaService, CajaService>();
builder.Services.AddScoped<IVentasService, VentasService>();
builder.Services.AddScoped<IInventarioService, InventarioService>();
builder.Services.AddScoped<IMaquinaService, MaquinaService>();
builder.Services.AddScoped<IInformesService, InformesService>();
builder.Services.AddScoped<VendingManager.Core.Interfaces.IOrdenCargaService, OrdenCargaService>();

// Servicios en segundo plano (Background Workers)
builder.Services.AddHttpClient<VendingManager.Core.Interfaces.IScraperClient, VendingManager.Infrastructure.Clients.ScraperClient>(); 
builder.Services.AddHostedService<AutomatedReportService>();

// 4. Configuración Blazor
// 4. Configuración Blazor
builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<VendingManager.Web.Auth.PersistingAuthenticationStateProvider>();

builder.Services.AddOpenApi();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/octet-stream", "application/wasm" });
});

// Registrar HttpClient para Pre-rendering (Server Side)
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:8080") });

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<VendingManager.Infrastructure.Data.ApplicationDbContext>();
        context.Database.Migrate();

        // Seed default user if not exists
        if (!context.Users.Any())
        {
            var adminUser = new VendingManager.Core.Entities.User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin")
            };
            context.Users.Add(adminUser);
            context.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
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
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseResponseCompression();
app.UseCors("PermitirBlazor"); // Activar CORS

app.UseAuthentication();
app.UseAuthorization();

// 5. Mapear componentes y API
app.MapStaticAssets();
app.MapControllers();

app.MapRazorComponents<VendingManager.Components.App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(VendingManager.Web.App).Assembly);

app.Run();