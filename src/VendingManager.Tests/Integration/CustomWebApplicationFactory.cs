using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Tests.Integration;

/// <summary>
/// WebApplicationFactory that swaps the SqlServer-backed <see cref="ApplicationDbContext"/>
/// registration for an isolated EF InMemory database (unique name per factory instance),
/// so integration tests can exercise the real HTTP pipeline (middleware, rate limiting,
/// routing) without depending on a real SQL Server connection.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"H1Test_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove every service registered by the original AddDbContext<ApplicationDbContext>
            // call (options, non-generic DbContextOptions, and internal per-context configuration
            // markers) — leaving any of them behind causes EF to see both the SqlServer and
            // InMemory providers registered for the same context and throw at runtime.
            var descriptorsToRemove = services.Where(d =>
                d.ServiceType == typeof(ApplicationDbContext) ||
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                (d.ServiceType.IsGenericType &&
                    d.ServiceType.GenericTypeArguments.Contains(typeof(ApplicationDbContext))))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });
        });
    }

    /// <summary>
    /// Seeds the well-known `admin` user (BCrypt("admin")) into the isolated
    /// in-memory database so login scenarios have a known credential.
    /// Call after the host has been created (e.g. after <c>CreateClient()</c>).
    /// </summary>
    public void SeedAdminUser()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!context.Users.Any(u => u.Username == "admin"))
        {
            context.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                Role = "Admin"
            });
            context.SaveChanges();
        }
    }
}
