namespace VendingManager.Shared.DTOs;

public class OffsetDriftDto
{
    public int MaquinaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string IdInternoMaquina { get; set; } = string.Empty;
    public int? ConfiguredOffsetHours { get; set; }
    public int ImpliedOffsetHours { get; set; }
    public int SampleCount { get; set; }
    public DateTime MeasuredAtUtc { get; set; }
    public bool IsFirstTimeProposal { get; set; }
}
