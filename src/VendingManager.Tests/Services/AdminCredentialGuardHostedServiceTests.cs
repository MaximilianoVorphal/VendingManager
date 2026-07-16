namespace VendingManager.Tests.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;
using VendingManager.Tests.TestData;

/// <summary>
/// Covers REQ-AUTH-02: the post-migration <c>admin</c> credential guard. Each test
/// controls the <c>SEED_ADMIN_PASSWORD</c> environment variable directly and
/// resets it afterward so tests don't leak state into each other.
/// </summary>
public class AdminCredentialGuardHostedServiceTests : IDisposable
{
    private const string SeedAdminPasswordEnvVar = "SEED_ADMIN_PASSWORD";

    private readonly ApplicationDbContext _context;
    private readonly Mock<ILogger<AdminCredentialGuardHostedService>> _mockLogger;
    private readonly AdminCredentialGuardHostedService _service;

    public AdminCredentialGuardHostedServiceTests()
    {
        _context = TestDataHelpers.CreateInMemoryContext($"AdminGuardTestDb_{Guid.NewGuid()}");
        _mockLogger = new Mock<ILogger<AdminCredentialGuardHostedService>>();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        var provider = services.BuildServiceProvider();

        _service = new AdminCredentialGuardHostedService(provider, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        Environment.SetEnvironmentVariable(SeedAdminPasswordEnvVar, null);
    }

    private async Task<User> SeedAdminAsync(string passwordHash)
    {
        var admin = new User
        {
            Username = "admin",
            PasswordHash = passwordHash,
            Role = "Admin"
        };
        _context.Users.Add(admin);
        await _context.SaveChangesAsync();
        return admin;
    }

    // ─── Scenario 1: env var unset — warning only, no rotation ─────────────

    [Fact]
    public async Task StartAsync_DefaultHash_EnvVarUnset_LogsWarningOnly_NoRotation()
    {
        // Arrange
        Environment.SetEnvironmentVariable(SeedAdminPasswordEnvVar, null);
        var admin = await SeedAdminAsync(BCrypt.Net.BCrypt.HashPassword("admin"));
        var originalHash = admin.PasswordHash;

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert — hash unchanged, warning logged
        var fetched = await _context.Users.FindAsync(admin.Id);
        fetched!.PasswordHash.Should().Be(originalHash);
        VerifyWarningLogged(Times.Once());
    }

    // ─── Scenario 2: env var set — rotation applied ────────────────────────

    [Fact]
    public async Task StartAsync_DefaultHash_EnvVarSet_RotatesHash()
    {
        // Arrange
        Environment.SetEnvironmentVariable(SeedAdminPasswordEnvVar, "NewSecurePass123!");
        var admin = await SeedAdminAsync(BCrypt.Net.BCrypt.HashPassword("admin"));

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert — hash now matches BCrypt(<value>); operator can log in with it afterward.
        var fetched = await _context.Users.FindAsync(admin.Id);
        BCrypt.Net.BCrypt.Verify("NewSecurePass123!", fetched!.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify("admin", fetched.PasswordHash).Should().BeFalse();
    }

    // ─── Scenario 3: idempotent across reboots ──────────────────────────────

    [Fact]
    public async Task StartAsync_AlreadyRotated_SameEnvVar_IsIdempotent_NoErrorNoChange()
    {
        // Arrange: simulate a prior boot having already rotated the hash.
        Environment.SetEnvironmentVariable(SeedAdminPasswordEnvVar, "NewSecurePass123!");
        var admin = await SeedAdminAsync(BCrypt.Net.BCrypt.HashPassword("NewSecurePass123!"));
        var hashAfterFirstRotation = admin.PasswordHash;

        // Act — boot again with the same env var.
        var act = () => _service.StartAsync(CancellationToken.None);

        // Assert — no error, no duplicate side effects, hash unchanged.
        await act.Should().NotThrowAsync();
        var fetched = await _context.Users.FindAsync(admin.Id);
        fetched!.PasswordHash.Should().Be(hashAfterFirstRotation);
        BCrypt.Net.BCrypt.Verify("NewSecurePass123!", fetched.PasswordHash).Should().BeTrue();

        // Since the hash is no longer the seeded default, no warning should fire either.
        VerifyWarningLogged(Times.Never());
    }

    // ─── Scenario 4: warning clears after manual change, no lockout possible ─

    [Fact]
    public async Task StartAsync_ManuallyChangedPassword_NoWarning_NoRotation_EvenIfEnvVarSet()
    {
        // Arrange: operator changed admin's password via UsersController.UpdateUser —
        // hash no longer equals the seeded default. SEED_ADMIN_PASSWORD may still be
        // set (e.g. leftover deploy config); this guard must never touch it again.
        Environment.SetEnvironmentVariable(SeedAdminPasswordEnvVar, "SomeOtherSeedValue");
        var admin = await SeedAdminAsync(BCrypt.Net.BCrypt.HashPassword("OperatorChosenPassword!"));
        var originalHash = admin.PasswordHash;

        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert — no warning, no rotation, no lockout path triggered.
        var fetched = await _context.Users.FindAsync(admin.Id);
        fetched!.PasswordHash.Should().Be(originalHash);
        BCrypt.Net.BCrypt.Verify("OperatorChosenPassword!", fetched.PasswordHash).Should().BeTrue();
        VerifyWarningLogged(Times.Never());
    }

    [Fact]
    public async Task StartAsync_NoAdminUser_DoesNotThrow()
    {
        // Arrange — no admin user seeded at all.

        // Act
        var act = () => _service.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ─── Scenario 5: malformed/empty PasswordHash must never crash startup ──

    [Theory]
    [InlineData("")]
    [InlineData("not-a-bcrypt-hash")]
    public async Task StartAsync_MalformedOrEmptyPasswordHash_DoesNotThrow(string passwordHash)
    {
        // Arrange — the seeded admin has an empty or non-BCrypt hash (e.g. corrupted
        // data or a manual DB edit), which would otherwise throw SaltParseException
        // or an ArgumentException out of BCrypt.Verify. (EF InMemory rejects a true
        // null PasswordHash as a required-property violation at seed time, so the
        // empty-string case exercises the same IsNullOrEmpty guard clause in
        // AdminCredentialGuardHostedService as a null hash would.)
        await SeedAdminAsync(passwordHash);

        // Act
        var act = () => _service.StartAsync(CancellationToken.None);

        // Assert — guard is best-effort and must never abort startup.
        await act.Should().NotThrowAsync();
    }

    private void VerifyWarningLogged(Times times)
    {
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}
