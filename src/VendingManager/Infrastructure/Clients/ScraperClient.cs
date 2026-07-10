using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using VendingManager.Core.Interfaces;
using VendingManager.Shared.DTOs;

namespace VendingManager.Infrastructure.Clients
{
    public class ScraperClient : IScraperClient
    {
        // Cache key for the machine-status payload. Shared across all callers and
        // instances in the same process. A multi-instance deployment would need a
        // distributed cache (e.g. Redis) to keep instances in sync.
        private const string MachineStatusCacheKey = "vendingmanager:machine-status";
        private static readonly TimeSpan MachineStatusCacheTtl = TimeSpan.FromHours(6);

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ScraperClient> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        };

        public ScraperClient(
            HttpClient httpClient,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<ScraperClient> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _cache = cache;
            _logger = logger;

            // Disable the default 100-second HttpClient.Timeout entirely so every call
            // controls its own deadline through a dedicated CancellationTokenSource.
            // This prevents concurrent calls (sales-report vs machine-status) from
            // racing on the shared Timeout property and causing false breaker trips.
            _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        }

        public async Task<(Stream FileStream, string FileName)> DownloadReportAsync(string machineId, DateTime startDate, DateTime endDate)
        {
            var scraperUrl = _configuration["ScraperServiceUrl"] ?? "http://scraper:8000";

            var requestData = new
            {
                machine_id = machineId,
                start_date = startDate.ToString("yyyy-MM-dd"),
                end_date = endDate.ToString("yyyy-MM-dd")
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(12));

            var response = await _httpClient.PostAsJsonAsync($"{scraperUrl}/download", requestData, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Error llamando al Scraper ALT: {response.StatusCode}");
            }

            var stream = await response.Content.ReadAsStreamAsync();
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                           ?? $"Report_ALT_{machineId}_{DateTime.Now:yyyyMMdd}.xls";

            return (stream, fileName);
        }

        public async Task<MachineStatusResponse> GetMachineStatusAsync()
        {
            // Server-side cache: 6h TTL to match the scraper's own polling cadence.
            // First request after expiry hits the scraper; subsequent requests within
            // the TTL are served from memory. This is what makes the home sidebar
            // render instantly on reload — the API call is now a few ms, not seconds.
            return await _cache.GetOrCreateAsync(MachineStatusCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = MachineStatusCacheTtl;

                var scraperUrl = _configuration["ScraperServiceUrl"] ?? "http://scraper:8000";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var response = await _httpClient.GetAsync($"{scraperUrl}/api/machines/status", cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Error obteniendo estado de máquinas: {response.StatusCode}");
                }

                var result = await response.Content.ReadFromJsonAsync<MachineStatusResponse>(_jsonOptions);
                _logger.LogInformation("Refreshed machine-status cache (machines: {Count})", result?.Machines?.Count ?? 0);
                return result ?? new MachineStatusResponse();
            }) ?? new MachineStatusResponse();
        }

        public async Task<SalesReportResponse> GetSalesReportAsync(
            DateTime startDate, DateTime endDate, string machineId = "",
            CancellationToken cancellationToken = default)
        {
            var scraperUrl = _configuration["ScraperServiceUrl"] ?? "http://scraper:8000";
            // Timeout sized for the heavier Playwright browser flow (login + navigate + fetch).
            // Matches the max-cycle budget (3 minutes default) plus margin.
            // Uses a dedicated CancellationTokenSource so concurrent calls do not race on
            // the shared HttpClient.Timeout property.
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromMinutes(3));

            var url = $"{scraperUrl}/api/sales/report?start={startDate:yyyy-MM-dd}&end={endDate:yyyy-MM-dd}";
            if (!string.IsNullOrEmpty(machineId))
                url += $"&machine_id={machineId}";

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(url, linkedCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // CTS timeout fired — propagate as OCE so the orchestrator maps it to Timeout.
                throw;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                // Try to parse status/reason from the 503 body for richer diagnostics.
                SalesReportResponse? errorBody = null;
                try
                {
                    errorBody = await response.Content.ReadFromJsonAsync<SalesReportResponse>(_jsonOptions);
                }
                catch
                {
                    // Non-JSON 503 body — fall through with generic reason.
                }

                throw new WafBlockedException(
                    errorBody?.Reason ?? errorBody?.Status ?? "503 Service Unavailable");
            }

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Error obteniendo reporte de ventas: {response.StatusCode}");

            var result = await response.Content.ReadFromJsonAsync<SalesReportResponse>(_jsonOptions);
            return result ?? new SalesReportResponse();
        }
    }
}
