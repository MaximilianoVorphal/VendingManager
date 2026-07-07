using System.Text.Json.Serialization;

namespace VendingManager.Shared.DTOs;

public class SalesReportResponse
{
    public int Total { get; set; }
    public decimal TotalAmount { get; set; }
    public List<SalesReportRowDto> Rows { get; set; } = new();
}

public class SalesReportRowDto
{
    public string MachineId { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string Slot { get; set; } = "";
    public string PayType { get; set; } = "";
    public string PayLabel { get; set; } = "";
    public decimal Price { get; set; }
    public string Result { get; set; } = "";
    public string MachineTime { get; set; } = "";
    public string ServerTime { get; set; } = "";
    public bool IsRemote { get; set; }

    [JsonPropertyName("_tr_id")]
    public string TrId { get; set; } = "";

    [JsonPropertyName("_serial")]
    public string TrSerialNumber { get; set; } = "";
}
