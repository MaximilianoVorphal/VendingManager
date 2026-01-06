using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using VendingManager.Core.Interfaces;

namespace VendingManager.Infrastructure.Clients
{
    public class ScraperClient : IScraperClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public ScraperClient(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
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

            // Aumentar Timeout para Playwright
            _httpClient.Timeout = TimeSpan.FromMinutes(3); // Un poco más que antes por seguridad

            var response = await _httpClient.PostAsJsonAsync($"{scraperUrl}/download", requestData);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Error llamando al Scraper: {response.StatusCode}");
            }

            var stream = await response.Content.ReadAsStreamAsync();
            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') 
                           ?? $"Report_{machineId}_{DateTime.Now:yyyyMMdd}.xls";

            return (stream, fileName);
        }
    }
}
