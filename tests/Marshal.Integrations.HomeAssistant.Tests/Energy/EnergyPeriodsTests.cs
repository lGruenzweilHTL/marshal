using Marshal.Abstractions;
using Marshal.Integrations.HomeAssistant.Energy;

namespace Marshal.Integrations.HomeAssistant.Tests.Energy;

public sealed class EnergyPeriodsTests
{
    private static readonly TimeZoneInfo _vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    // Thursday 2026-10-08 14:37 in Vienna (summer time).
    private static readonly DateTimeOffset _now = new(2026, 10, 8, 12, 37, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("today", "2026-10-08 00:00", "2026-10-08 14:00", "2026-10-07 00:00", "2026-10-07 14:00")]
    [InlineData("yesterday", "2026-10-07 00:00", "2026-10-08 00:00", "2026-10-06 00:00", "2026-10-07 00:00")]
    [InlineData("week", "2026-10-05 00:00", "2026-10-08 14:00", "2026-09-28 00:00", "2026-10-01 14:00")]
    [InlineData("month", "2026-10-01 00:00", "2026-10-08 14:00", "2026-09-01 00:00", "2026-09-08 14:00")]
    [InlineData("last_week", "2026-09-28 00:00", "2026-10-05 00:00", "2026-09-21 00:00", "2026-09-28 00:00")]
    [InlineData("last_month", "2026-09-01 00:00", "2026-10-01 00:00", "2026-08-01 00:00", "2026-09-01 00:00")]
    public void Resolve_PeriodAndPrevious_InTheOwnersTimeZone(string period, string start, string end, string previousStart, string previousEnd)
    {
        EnergyPeriod resolved = EnergyPeriods.Resolve(period, null, null, comparePrevious: true, _now, _vienna).Value;

        Assert.Equal((Local(start), Local(end)), (resolved.Current.LocalStart, resolved.Current.LocalEnd));
        Assert.Equal((Local(previousStart), Local(previousEnd)), (resolved.Previous!.LocalStart, resolved.Previous.LocalEnd));
    }

    [Fact]
    public void Resolve_Week_UtcBoundariesFromLocalTimes()
    {
        EnergyWindow week = EnergyPeriods.Resolve("week", null, null, comparePrevious: false, _now, _vienna).Value.Current;

        Assert.Equal((new DateTimeOffset(2026, 10, 4, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero)), (week.Start, week.End));
    }

    [Fact]
    public void Resolve_NoComparison_NoPrevious()
    {
        Assert.Null(EnergyPeriods.Resolve("today", null, null, comparePrevious: false, _now, _vienna).Value.Previous);
    }

    [Fact]
    public void Resolve_Range_ToInclusiveAndPreviousOfEqualLength()
    {
        EnergyPeriod range = EnergyPeriods.Resolve("range", "2026-10-01", "2026-10-03", comparePrevious: true, _now, _vienna).Value;

        Assert.Equal((Local("2026-10-01 00:00"), Local("2026-10-04 00:00")), (range.Current.LocalStart, range.Current.LocalEnd));
        Assert.Equal((Local("2026-09-28 00:00"), Local("2026-10-01 00:00")), (range.Previous!.LocalStart, range.Previous.LocalEnd));
    }

    [Fact]
    public void Resolve_MonthOnThe31st_PreviousClampedToFebruarysLastDay()
    {
        var now = new DateTimeOffset(2026, 3, 31, 8, 0, 0, TimeSpan.Zero); // 10:00 Vienna (summer time since 29 March)

        EnergyWindow previous = EnergyPeriods.Resolve("month", null, null, comparePrevious: true, now, _vienna).Value.Previous!;

        Assert.Equal((Local("2026-02-01 00:00"), Local("2026-02-28 10:00")), (previous.LocalStart, previous.LocalEnd));
    }

    [Fact]
    public void Resolve_WeekAcrossDstEnd_IsOneHourLonger()
    {
        var now = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero); // Wednesday; summer time ended Sunday 25 October

        EnergyWindow lastWeek = EnergyPeriods.Resolve("last_week", null, null, comparePrevious: false, now, _vienna).Value.Current;

        Assert.Equal(new DateTimeOffset(2026, 10, 18, 22, 0, 0, TimeSpan.Zero), lastWeek.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), lastWeek.End);
        Assert.Equal(TimeSpan.FromHours(169), lastWeek.End - lastWeek.Start);
    }

    [Theory]
    [InlineData("fortnight", null, null)]
    [InlineData(null, null, null)]
    [InlineData("range", "2026-10-01", null)]
    [InlineData("range", "1 October", "2026-10-03")]
    [InlineData("range", "2026-10-08", "2026-10-01")]
    public void Resolve_Invalid_ValidationError(string? period, string? from, string? to)
    {
        Result<EnergyPeriod> resolved = EnergyPeriods.Resolve(period, from, to, comparePrevious: false, _now, _vienna);

        Assert.Equal(ErrorType.Validation, resolved.Error.Type);
    }

    private static DateTime Local(string text) => DateTime.ParseExact(text, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
