using StatisticService.Service.Weekly;

namespace StatisticService.Service.Tests.Weekly;

public class WeeklyPeriodTests
{
    private static readonly TimeSpan Winter = TimeSpan.FromHours(1);   // CET
    private static readonly TimeSpan Summer = TimeSpan.FromHours(2);   // CEST

    private static WeeklyPeriod Parse(string week, string nowUtc = "2026-06-10T12:00:00Z")
    {
        Assert.True(WeeklyPeriod.TryParse(week, DateTimeOffset.Parse(nowUtc), out var period), $"'{week}' should be valid");
        return period;
    }

    private static WeeklyPeriod Current(string nowUtc) => Parse("current", nowUtc);

    [Fact]
    public void IsoWeek_RunsMondayMidnightToSundayLastSecond_InDanishTime()
    {
        var period = Parse("2026-W47");

        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, Winter), period.WeekStart);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 23, 59, 59, Winter), period.WeekEnd);
        // Half-open UTC interval for the query: Monday 00:00 CET = Sunday 23:00 UTC.
        Assert.Equal(new DateTime(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc), period.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 22, 23, 0, 0, DateTimeKind.Utc), period.EndUtc);
        Assert.Equal(DateTimeKind.Utc, period.StartUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, period.EndUtc.Kind);
    }

    [Fact]
    public void Current_SundayLastSecond_IsStillTheOldWeek()
    {
        // Sunday 2026-11-22 23:59:59 Danish time = 22:59:59 UTC.
        var period = Current("2026-11-22T22:59:59Z");

        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, Winter), period.WeekStart);
    }

    [Fact]
    public void Current_MondayMidnight_IsTheNextWeek()
    {
        // Monday 2026-11-23 00:00:00 Danish time = 23:00:00 UTC on Sunday.
        var period = Current("2026-11-22T23:00:00Z");

        Assert.Equal(new DateTimeOffset(2026, 11, 23, 0, 0, 0, Winter), period.WeekStart);
    }

    [Fact]
    public void Current_UsesDanishDate_NotUtcDate()
    {
        // 23:30 UTC on Sunday is already Monday 00:30 in Denmark, so a UTC calendar-day check would pick the wrong week.
        var period = Current("2026-11-22T23:30:00Z");

        Assert.Equal(new DateOnly(2026, 11, 23), DateOnly.FromDateTime(period.WeekStart.DateTime));
    }

    [Fact]
    public void GameEndingOnTheLastSecondAndOnTheNextMidnight_FallInDifferentWeeks()
    {
        var week47 = Parse("2026-W47");
        var week48 = Parse("2026-W48");
        var lastSecond = new DateTime(2026, 11, 22, 22, 59, 59, DateTimeKind.Utc);
        var midnight = new DateTime(2026, 11, 22, 23, 0, 0, DateTimeKind.Utc);

        Assert.True(lastSecond >= week47.StartUtc && lastSecond < week47.EndUtc);
        Assert.False(midnight >= week47.StartUtc && midnight < week47.EndUtc);
        Assert.True(midnight >= week48.StartUtc && midnight < week48.EndUtc);
        Assert.Equal(week47.EndUtc, week48.StartUtc); // no gap and no overlap between weeks
    }

    [Fact]
    public void SpringForward_WeekIs167Hours_AndEndOffsetIsSummerTime()
    {
        // Clocks go forward Sunday 2026-03-29 at 02:00.
        var period = Parse("2026-W13");

        Assert.Equal(new DateTimeOffset(2026, 3, 23, 0, 0, 0, Winter), period.WeekStart);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 23, 59, 59, Summer), period.WeekEnd);
        Assert.Equal(new DateTime(2026, 3, 22, 23, 0, 0, DateTimeKind.Utc), period.StartUtc);
        Assert.Equal(new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc), period.EndUtc);
        Assert.Equal(TimeSpan.FromHours(167), period.EndUtc - period.StartUtc);
    }

    [Fact]
    public void FallBack_WeekIs169Hours_AndEndOffsetIsWinterTime()
    {
        // Clocks go back Sunday 2026-10-25 at 03:00.
        var period = Parse("2026-W43");

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 0, 0, 0, Summer), period.WeekStart);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 59, 59, Winter), period.WeekEnd);
        Assert.Equal(new DateTime(2026, 10, 18, 22, 0, 0, DateTimeKind.Utc), period.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc), period.EndUtc);
        Assert.Equal(TimeSpan.FromHours(169), period.EndUtc - period.StartUtc);
    }

    [Fact]
    public void Current_AtSummerTimeMondayMidnight_SwitchesWeek()
    {
        // Monday 2026-06-15 00:00 CEST = Sunday 22:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 6, 8, 0, 0, 0, Summer), Current("2026-06-14T21:59:59Z").WeekStart);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 0, 0, 0, Summer), Current("2026-06-14T22:00:00Z").WeekStart);
    }

    [Theory]
    [InlineData("2026-12-28T00:00:00Z")]  // Monday of week 53
    [InlineData("2026-12-31T23:30:00Z")]  // already 1 January in Denmark, but still ISO week 53 of 2026
    [InlineData("2027-01-03T22:59:59Z")]  // Sunday 23:59:59 Danish time
    public void Current_AroundNewYear_UsesIsoWeekYear(string nowUtc)
    {
        var period = Current(nowUtc);

        Assert.Equal(new DateTimeOffset(2026, 12, 28, 0, 0, 0, Winter), period.WeekStart);
        Assert.Equal(new DateTimeOffset(2027, 1, 3, 23, 59, 59, Winter), period.WeekEnd);
    }

    [Fact]
    public void Current_FirstDaysOfJanuary_CanBelongToLastWeekOfPreviousYear_OrFirstWeekOfNewOne()
    {
        // 2025-12-29 is Monday of ISO week 2026-W01.
        Assert.Equal(new DateTimeOffset(2025, 12, 29, 0, 0, 0, Winter), Current("2025-12-31T12:00:00Z").WeekStart);
        Assert.Equal(new DateTimeOffset(2026, 1, 4, 23, 59, 59, Winter), Current("2025-12-31T12:00:00Z").WeekEnd);
    }

    [Fact]
    public void Week53_ExistsInAYearWith53Weeks()
    {
        var period = Parse("2026-W53");

        Assert.Equal(new DateTimeOffset(2026, 12, 28, 0, 0, 0, Winter), period.WeekStart);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("CURRENT")]
    [InlineData("Current")]
    public void Current_IsCaseInsensitive(string value)
        => Assert.Equal(new DateTimeOffset(2026, 6, 8, 0, 0, 0, Summer), Parse(value).WeekStart);

    [Fact]
    public void LowercaseW_IsAccepted() => Assert.Equal(Parse("2026-W47"), Parse("2026-w47"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("now")]
    [InlineData("current ")]
    [InlineData("2026-W00")]
    [InlineData("2026-W54")]
    [InlineData("2025-W53")]   // 2025 only has 52 ISO weeks
    [InlineData("2026-W5")]    // week number must be two digits
    [InlineData("2026W47")]
    [InlineData("2026-47")]
    [InlineData("26-W47")]
    [InlineData("2026-W47 ")]
    [InlineData("2026-W4a")]
    [InlineData("-026-W47")]
    [InlineData("2026-W-1")]
    [InlineData("2026-W47-1")]
    [InlineData("1999-W10")]
    [InlineData("2101-W01")]
    [InlineData("2026-11-17")]
    public void InvalidWeek_IsRejected(string? week)
        => Assert.False(WeeklyPeriod.TryParse(week, DateTimeOffset.Parse("2026-06-10T12:00:00Z"), out _));
}
