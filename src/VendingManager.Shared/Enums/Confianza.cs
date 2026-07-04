namespace VendingManager.Shared.Enums;

/// <summary>
/// Confidence level of an OCR-matched slot detection.
/// - Alta: high confidence (≥0.85 confidence score)
/// - Media: medium confidence (≥0.6 and <0.85 confidence score)
/// - Baja: low confidence (<0.6 confidence score, or forced by qty>capacity or unknown slot)
/// </summary>
public enum Confianza
{
    Alta,
    Media,
    Baja
}
