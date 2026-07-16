using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Infrastructure.Services;

/// <summary>
/// Post-migration startup guard for the seeded <c>admin</c> account (REQ-AUTH-02).
/// Runs once per boot, after <c>Database.Migrate()</c> has completed (hosted
/// services start inside <c>app.Run()</c>, which is guaranteed to be after the
/// inline migration block in <c>Program.cs</c>).
///
/// While the <c>admin</c> user's password hash still equals the seeded
/// <c>BCrypt("admin")</c> hash, this emits a Serilog warning on every boot. If the
/// operator has set <c>SEED_ADMIN_PASSWORD</c>, the hash is additionally rotated
/// to <c>BCrypt(SEED_ADMIN_PASSWORD)</c> — opt-in only, so this can never lock an
/// operator out. Once the hash is no longer the seeded default (either via this
/// rotation or a manual change through <c>UsersController.UpdateUser</c>), this
/// guard becomes a no-op on every subsequent boot: no warning, no rotation.
/// </summary>
public class AdminCredentialGuardHostedService : IHostedService
{
    private const string DefaultAdminUsername = "admin";
    private const string DefaultAdminPassword = "admin";
    private const string SeedAdminPasswordEnvVar = "SEED_ADMIN_PASSWORD";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AdminCredentialGuardHostedService> _logger;

    public AdminCredentialGuardHostedService(
        IServiceProvider serviceProvider,
        ILogger<AdminCredentialGuardHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var admin = await context.Users
            .FirstOrDefaultAsync(u => u.Username == DefaultAdminUsername, cancellationToken);

        if (admin == null)
            return;

        bool isDefaultPassword = BCrypt.Net.BCrypt.Verify(DefaultAdminPassword, admin.PasswordHash);
        if (!isDefaultPassword)
        {
            // Hash no longer equals the seeded default (rotated or manually
            // changed) — nothing to warn about, nothing to rotate.
            return;
        }

        _logger.LogWarning(
            "SECURITY: the '{Username}' user still has the default seeded password. " +
            "Set the {EnvVar} environment variable to rotate it to a new value on next boot.",
            DefaultAdminUsername, SeedAdminPasswordEnvVar);

        var seedPassword = Environment.GetEnvironmentVariable(SeedAdminPasswordEnvVar);
        if (string.IsNullOrEmpty(seedPassword))
            return;

        // Guard against re-hashing on every boot once the target password is
        // already applied (BCrypt.HashPassword generates a new salt each call,
        // so this check — not just the isDefaultPassword one above — is what
        // keeps rotation idempotent/no-op once seedPassword is in effect).
        if (BCrypt.Net.BCrypt.Verify(seedPassword, admin.PasswordHash))
            return;

        admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(seedPassword);
        context.Users.Update(admin);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "SECURITY: the '{Username}' password was rotated from the {EnvVar} environment variable.",
            DefaultAdminUsername, SeedAdminPasswordEnvVar);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
