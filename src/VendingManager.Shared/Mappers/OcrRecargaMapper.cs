using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;
using VendingManager.Shared.Models;

namespace VendingManager.Shared.Mappers;

/// <summary>
/// Maps OCR result DTO to the LecturaRecarga domain model with confidence grading.
/// </summary>
public static class OcrRecargaMapper
{
    /// <summary>
    /// Translates an OCR result into a reviewable lectura by grading confidence,
    /// applying forced Baja rules, and enriching with machine slot data.
    /// </summary>
    /// <param name="dto">OCR result from the backend service.</param>
    /// <param name="actualSlots">The machine's actual slot configuration (Slot, Producto, Capacidad).</param>
    /// <returns>A LecturaRecarga with per-slot detection and review metadata.</returns>
    public static LecturaRecarga FromOcr(OcrRecargaResultDto dto, IReadOnlyList<SlotActual> actualSlots)
    {
        var detectedSlots = new List<SlotDetectado>();

        foreach (var matched in dto.ExtractedSlots)
        {
            var slotId = matched.MatchedSlot ?? string.Empty;
            var actualSlot = actualSlots.FirstOrDefault(s => s.Slot == slotId);
            var isKnownSlot = actualSlot != null;

            var capacidad = isKnownSlot ? actualSlot!.Capacidad : 0;
            var confianza = isKnownSlot
                ? GradeConfianza(matched.Confidence)
                : Confianza.Baja;

            // Force Baja if detected quantity exceeds slot capacity
            if (isKnownSlot && matched.Quantity > capacidad)
            {
                confianza = Confianza.Baja;
            }

            detectedSlots.Add(new SlotDetectado
            {
                Slot = slotId,
                SlotIndex = isKnownSlot ? actualSlot!.Index : -1,
                Producto = isKnownSlot ? actualSlot!.Producto : null,
                CantidadDetectada = matched.Quantity,
                Capacidad = capacidad,
                Confianza = confianza
            });
        }

        return new LecturaRecarga
        {
            Slots = detectedSlots
        };
    }

    private static Confianza GradeConfianza(float confidence)
    {
        return confidence switch
        {
            >= 0.85f => Confianza.Alta,
            >= 0.60f => Confianza.Media,
            _ => Confianza.Baja
        };
    }
}
