using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using VendingManager.Data;
using VendingManager.Services;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);




// 1. CORS (Para que la web funcione)
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirTodo", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// 2. BASE DE DATOS
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3. SERVICIO EXCEL (El único que necesitamos)
builder.Services.AddScoped<ExcelService>();

// --- AQUÍ QUITAMOS LOS BOTS Y SELENIUM ---

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "Vending Manager API";
        options.Theme = ScalarTheme.Mars;
    });
}

app.UseHttpsRedirection();
app.UseCors("PermitirTodo");
app.UseAuthorization();
app.MapControllers();

app.Run();