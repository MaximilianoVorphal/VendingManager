using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VendingManager.Shared.DTOs;
using Xunit;

namespace VendingManager.Tests.Integration;

/// <summary>
/// Integration tests for REQ-AUTH-01 (login rate limiting) exercising the real
/// HTTP pipeline (rate limiter middleware + routing) via <see cref="CustomWebApplicationFactory"/>.
/// The fixed-window reset scenario (~60s real-time wait) is explicitly descoped per
/// orchestrator decision — this class covers only the 429-on-overflow scenario.
/// </summary>
public class AccountControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AccountControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SeedAdminUser();
    }

    [Fact]
    public async Task Login_SixthRequestInWindow_Returns429()
    {
        using var client = _factory.CreateClient();
        var invalidLogin = new LoginDto { Username = "admin", Password = "wrong-password" };

        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/account/login", invalidLogin);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var sixthResponse = await client.PostAsJsonAsync("/api/account/login", invalidLogin);

        sixthResponse.StatusCode.Should().Be((HttpStatusCode)429);
    }
}
