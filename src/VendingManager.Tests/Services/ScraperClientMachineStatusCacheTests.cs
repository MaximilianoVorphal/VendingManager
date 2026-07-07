using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VendingManager.Infrastructure.Clients;
using VendingManager.Shared.DTOs;
using Xunit;

namespace VendingManager.Tests.Services;

/// <summary>
/// HttpMessageHandler that returns a canned JSON response and counts how many
/// times the underlying endpoint was actually hit.
/// </summary>
internal class CountingJsonHandler : HttpMessageHandler
{
    private readonly string _json;
    public int CallCount { get; private set; }

    public CountingJsonHandler(object payload)
    {
        _json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_json, Encoding.UTF8, "application/json"),
        });
    }
}

public class ScraperClientMachineStatusCacheTests
{
    private static ScraperClient BuildClient(out CountingJsonHandler handler, out MemoryCache cache)
    {
        handler = new CountingJsonHandler(new
        {
            machines = new[]
            {
                new { machine_id = "2410280012", name = "MAQUINA 001", status = "online" },
                new { machine_id = "2410280022", name = "MAQUINA 002", status = "online" }
            }
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ScraperServiceUrl"] = "http://localhost"
            })
            .Build();
        cache = new MemoryCache(new MemoryCacheOptions());
        return new ScraperClient(http, config, cache, NullLogger<ScraperClient>.Instance);
    }

    [Fact]
    public async Task GetMachineStatusAsync_CachesResultBetweenCalls()
    {
        var client = BuildClient(out var handler, out var cache);

        var first = await client.GetMachineStatusAsync();
        var second = await client.GetMachineStatusAsync();

        handler.CallCount.Should().Be(1, "the second call should be served from the in-memory cache");
        first.Machines.Should().HaveCount(2);
        second.Machines.Should().HaveCount(2);
        cache.Dispose();
    }

    [Fact]
    public async Task GetMachineStatusAsync_RefreshesAfterCacheEviction()
    {
        // Build the first client + cache and populate the cache.
        var firstClient = BuildClient(out var firstHandler, out var cache);
        await firstClient.GetMachineStatusAsync();
        await firstClient.GetMachineStatusAsync();
        firstHandler.CallCount.Should().Be(1);

        // Simulate a TTL expiry (or process restart): evict the cache entry and
        // build a second client against a brand new handler. The second call
        // must hit the scraper again because the cache is empty.
        cache.Compact(1.0);
        var secondClient = BuildClient(out var secondHandler, out cache);
        await secondClient.GetMachineStatusAsync();

        firstHandler.CallCount.Should().Be(1, "the first handler should not see new traffic after eviction");
        secondHandler.CallCount.Should().Be(1, "the second handler should be hit once for the cold fetch");
        cache.Dispose();
    }
}
