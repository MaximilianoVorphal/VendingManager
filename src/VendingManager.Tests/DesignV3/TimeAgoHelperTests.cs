using FluentAssertions;
using VendingManager.Web.Components.Shared;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class TimeAgoHelperTests
{
    private static readonly DateTime Base = new(2026, 6, 30, 14, 0, 0);

    [Fact]
    public void Format_5SecondsAgo_ReturnsHaceSeg()
    {
        var result = TimeAgoHelper.Format(Base.AddSeconds(-5), Base);
        result.Should().Be("hace 5 seg");
    }

    [Fact]
    public void Format_1MinuteAgo_ReturnsHace1Min()
    {
        var result = TimeAgoHelper.Format(Base.AddMinutes(-1), Base);
        result.Should().Be("hace 1 min");
    }

    [Fact]
    public void Format_3MinutesAgo_ReturnsHace3Min()
    {
        var result = TimeAgoHelper.Format(Base.AddMinutes(-3), Base);
        result.Should().Be("hace 3 min");
    }

    [Fact]
    public void Format_1HourAgo_ReturnsHace1Hora()
    {
        var result = TimeAgoHelper.Format(Base.AddHours(-1), Base);
        result.Should().Be("hace 1 hora");
    }

    [Fact]
    public void Format_2HoursAgo_ReturnsHace2Horas()
    {
        var result = TimeAgoHelper.Format(Base.AddHours(-2), Base);
        result.Should().Be("hace 2 horas");
    }

    [Fact]
    public void Format_1DayAgo_ReturnsHace1Dia()
    {
        var result = TimeAgoHelper.Format(Base.AddDays(-1), Base);
        result.Should().Be("hace 1 día");
    }

    [Fact]
    public void Format_3DaysAgo_ReturnsHace3Dias()
    {
        var result = TimeAgoHelper.Format(Base.AddDays(-3), Base);
        result.Should().Be("hace 3 días");
    }
}
