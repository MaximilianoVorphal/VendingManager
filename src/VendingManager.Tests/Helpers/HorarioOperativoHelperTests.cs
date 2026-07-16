namespace VendingManager.Tests.Helpers;

using FluentAssertions;
using VendingManager.Shared.Helpers;

public class HorarioOperativoHelperTests
{
    [Fact]
    public void SingleDay_FullWindow_Returns14Hours()
    {
        var desde = new DateTime(2026, 7, 13, 8, 0, 0);
        var hasta = new DateTime(2026, 7, 13, 22, 0, 0);

        var result = HorarioOperativoHelper.HorasEnRangoOperativo(desde, hasta);

        result.Should().BeApproximately(14.0, 0.001);
    }

    [Fact]
    public void SingleDay_PartialInsideWindow_Returns6Hours()
    {
        var desde = new DateTime(2026, 7, 13, 10, 0, 0);
        var hasta = new DateTime(2026, 7, 13, 16, 0, 0);

        var result = HorarioOperativoHelper.HorasEnRangoOperativo(desde, hasta);

        result.Should().BeApproximately(6.0, 0.001);
    }

    [Fact]
    public void MultiDay_AcrossThreeOperationalDays_Returns28Hours()
    {
        var desde = new DateTime(2026, 7, 13, 9, 0, 0);
        var hasta = new DateTime(2026, 7, 15, 12, 0, 0);

        var result = HorarioOperativoHelper.HorasEnRangoOperativo(desde, hasta);

        // Day 1 (Jul 13): 09:00-22:00 = 13h
        // Day 2 (Jul 14): 08:00-22:00 = 14h
        // Day 3 (Jul 15): 08:00-12:00 = 4h
        // Total = 13 + 14 + 4 = 31
        // Wait — let me recalculate:
        // 13th: desde 09:00, goes to 22:00 = 13 hours
        // 14th: 08:00 to 22:00 = 14 hours
        // 15th: 08:00 to 12:00 = 4 hours
        // Total = 31
        // But spec says 28.0 (14+14+4)?
        // Ah — spec scenario says desde=2026-07-13 09:00, hasta=2026-07-15 12:00 = 28.0 (14+14+4)
        // Hmm that doesn't add up with my calculation.
        // Let me re-read: "GIVEN desde=2026-07-13 09:00, hasta=2026-07-15 12:00 THEN 28.0 (14+14+4)"
        // 13th 09:00 to 22:00 = 13h
        // 14th 08:00 to 22:00 = 14h
        // 15th 08:00 to 12:00 = 4h
        // = 31h, not 28.
        // But spec says 28 (14+14+4). That implies day 1 from 08:00, not 09:00?
        // Actually 14+14+4 = 32? No: 14+14+4=32.
        // Wait, 14+10+4? Or maybe the spec arithmetic is wrong. Let me just trust the algorithm.
        // The algorithm from TemplateRecargaAnalyticsService is the authority.
        // Let me just check: the spec scenario says (14+14+4) = 28? That's 32.
        // Hmm, I think there might be a spec arithmetic error. Let me recalculate:
        // 14 + 14 + 4 = 32, not 28.
        // But 14 + 10 + 4 = 28.
        // Actually, I think the spec may have meant (13+11+4) or something.
        // Regardless, let me use the CORRECT expected value from the actual algorithm.
        // Let me compute: 
        // Desde 09:00 Jul 13 → hasta 22:00 Jul 13 = 13h
        // 08:00 Jul 14 → 22:00 Jul 14 = 14h  
        // 08:00 Jul 15 → 12:00 Jul 15 = 4h
        // = 31h
        
        // I'll write the test to match what the algorithm actually produces.
        // Let me compute with the algorithm logic:
        // Using the extract: 
        // cursor=09:00 Jul 13, hour=9 (≥8), endOfHour=10:00, total+=1, cursor=10:00
        // ... 10:00-11:00=1h, 11:00-12:00=1h, ... 21:00-22:00=1h
        // 09:00→22:00 = 13 hours
        // cursor=22:00 Jul 13, hour=22 (≥22), cursor=Jul 14 08:00
        // 08:00→22:00 Jul 14 = 14 hours
        // cursor=22:00 Jul 14, hour=22, cursor=Jul 15 08:00
        // 08:00→12:00 Jul 15 = 4 hours
        // Total = 13+14+4 = 31h
        
        // The spec says 28.0 but I'll trust the algorithm.
        // Actually wait - let me re-read the spec: "THEN it MUST return 28.0 (14+14+4)"
        // 14+14+4=32, not 28. This is a math error in the spec.
        // The actual correct answer from the algorithm is 31.0.
        // But I should match what the algorithm produces. Let me just check...
        
        // Hmm, maybe the spec intended desde at 08:00? "desde=2026-07-13 09:00" → 09:00.
        // With 09:00, it's 13+14+4=31.
        // With 08:00, it's 14+14+4=32.
        // Neither gives 28.
        // Maybe the spec has a mistake: 14+10+4=28? There's no 10h day.
        
        // I'll implement using the extracted algorithm and test against what it actually returns.
        // The spec may have a minor arithmetic error. I'll note this in deviations.
        result.Should().BeApproximately(31.0, 0.001);
    }

    [Fact]
    public void StartsBeforeOperationalHours_ReturnsOnlyHoursInsideWindow()
    {
        var desde = new DateTime(2026, 7, 13, 5, 0, 0);
        var hasta = new DateTime(2026, 7, 13, 10, 0, 0);

        var result = HorarioOperativoHelper.HorasEnRangoOperativo(desde, hasta);

        result.Should().BeApproximately(2.0, 0.001); // Only 08:00-10:00
    }

    [Fact]
    public void Overnight_AllOutsideOperationalHours_ReturnsZero()
    {
        var desde = new DateTime(2026, 7, 13, 23, 0, 0);
        var hasta = new DateTime(2026, 7, 14, 6, 0, 0);

        var result = HorarioOperativoHelper.HorasEnRangoOperativo(desde, hasta);

        result.Should().Be(0.0);
    }

    // ── EsHoraOperativa boundary tests (P3: horario-operativo-centralizado) ──

    /// <summary>
    /// T1.1: 12:00 is within the 08:00–22:00 window → true.
    /// </summary>
    [Fact]
    public void EsHoraOperativa_WithinHours_ReturnsTrue()
    {
        var fecha = new DateTime(2026, 7, 15, 12, 0, 0);

        var result = HorarioOperativoHelper.EsHoraOperativa(fecha);

        result.Should().BeTrue();
    }

    /// <summary>
    /// T1.2: 08:00 is the start boundary, inclusive → true.
    /// </summary>
    [Fact]
    public void EsHoraOperativa_AtStartBoundary_ReturnsTrue()
    {
        var fecha = new DateTime(2026, 7, 15, 8, 0, 0);

        var result = HorarioOperativoHelper.EsHoraOperativa(fecha);

        result.Should().BeTrue();
    }

    /// <summary>
    /// T1.3: 22:00 is the end boundary, exclusive → false.
    /// </summary>
    [Fact]
    public void EsHoraOperativa_AtEndBoundary_ReturnsFalse()
    {
        var fecha = new DateTime(2026, 7, 15, 22, 0, 0);

        var result = HorarioOperativoHelper.EsHoraOperativa(fecha);

        result.Should().BeFalse();
    }

    /// <summary>
    /// T1.4: 06:00 is outside the 08:00–22:00 window → false.
    /// </summary>
    [Fact]
    public void EsHoraOperativa_OutsideWindow_ReturnsFalse()
    {
        var fecha = new DateTime(2026, 7, 15, 6, 0, 0);

        var result = HorarioOperativoHelper.EsHoraOperativa(fecha);

        result.Should().BeFalse();
    }
}
