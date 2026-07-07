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

            _httpClient.Timeout = TimeSpan.FromMinutes(12); // Polling 6min + descarga, margen de seguridad

            var response = await _httpClient.PostAsJsonAsync($"{scraperUrl}/download", requestData);

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
                _httpClient.Timeout = TimeSpan.FromSeconds(30);

                var response = await _httpClient.GetAsync($"{scraperUrl}/api/machines/status");

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Error obteniendo estado de máquinas: {response.StatusCode}");
                }

                var result = await response.Content.ReadFromJsonAsync<MachineStatusResponse>(_jsonOptions);
                _logger.LogInformation("Refreshed machine-status cache (machines: {Count})", result?.Machines?.Count ?? 0);
                return result ?? new MachineStatusResponse();
            }) ?? new MachineStatusResponse();
        }

        public async Task<SalesReportResponse> GetSalesReportAsync(DateTime startDate, DateTime endDate, string machineId = "")
        {
            var scraperUrl = _configuration["ScraperServiceUrl"] ?? "http://scraper:8000";
            _httpClient.Timeout = TimeSpan.FromSeconds(60);

            var url = $"{scraperUrl}/api/sales/report?start={startDate:yyyy-MM-dd}&end={endDate:yyyy-MM-dd}";
            if (!string.IsNullOrEmpty(machineId))
                url += $"&machine_id={machineId}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Error obteniendo reporte de ventas: {response.StatusCode}");

            var result = await response.Content.ReadFromJsonAsync<SalesReportResponse>(_jsonOptions);
            return result ?? new SalesReportResponse();
        }
    }
}
