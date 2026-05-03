using System.Text.Json.Serialization;

namespace VendingManager.Shared.DTOs
{
    /// <summary>
    /// DTO for OCR extraction result from a refill list photo.
    /// Contains slot-level matches with fuzzy matching metadata.
    /// </summary>
    public class OcrRecargaResultDto
    {
        [JsonPropertyName("slots")]
        public List<OcrRecargaSlotDto> Slots { get; set; } = new();

        [JsonPropertyName("extracted_slots")]
        public List<MatchedSlotDto> ExtractedSlots { get; set; } = new();

        [JsonPropertyName("unmatched_ocr_slots")]
        public List<string> UnmatchedOcrSlots { get; set; } = new();

        [JsonPropertyName("machine_slot_numbers")]
        public List<string> MachineSlotNumbers { get; set; } = new();
    }

    /// <summary>
    /// Raw slot data returned from OCR (slot number + quantity).
    /// </summary>
    public class OcrRecargaSlotDto
    {
        [JsonPropertyName("slot_number")]
        public string SlotNumber { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Matched slot with fuzzy match metadata.
    /// </summary>
    public class MatchedSlotDto
    {
        [JsonPropertyName("slot_number")]
        public string SlotNumber { get; set; } = string.Empty;

        [JsonPropertyName("matched_slot")]
        public string? MatchedSlot { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }
    }
}