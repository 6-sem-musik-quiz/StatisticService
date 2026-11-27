using System.Globalization;

namespace StatisticService.Service.Weekly;

// A leaderboard week: Monday 00:00:00 to Sunday 23:59:59 in Danish time.
// StartUtc/EndUtc is the half-open interval [StartUtc, EndUtc) to query EndTime with.
public sealed record WeeklyPeriod(
    DateTimeOffset WeekStart,
    DateTimeOffset WeekEnd,
    DateTime StartUtc,
    DateTime EndUtc)
{
    // Years outside this range are rejected as invalid weeks (keeps date arithmetic far from DateTime limits).
    private const int MinYear = 2000;
    private const int MaxYear = 2100;

    public static readonly TimeZoneInfo CopenhagenZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    // Pure function: the only inputs are the week text, the current time and the zone.
    // "current" (any case) is the week that contains `now`; otherwise "YYYY-Www" (ISO 8601).
    public static bool TryParse(string? week, DateTimeOffset now, TimeZoneInfo zone, out WeeklyPeriod period)
    {
        period = null!;
        if (string.IsNullOrWhiteSpace(week))
            return false;

        DateTime monday;
        if (string.Equals(week, "current", StringComparison.OrdinalIgnoreCase))
        {
            var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
            // ISO week-year differs from the calendar year around New Year, so go through ISOWeek.
            monday = ISOWeek.ToDateTime(ISOWeek.GetYear(localNow), ISOWeek.GetWeekOfYear(localNow), DayOfWeek.Monday);
        }
        else if (!TryParseIsoWeek(week, out monday))
        {
            return false;
        }

        period = ForMonday(monday, zone);
        return true;
    }

    public static bool TryParse(string? week, DateTimeOffset now, out WeeklyPeriod period)
        => TryParse(week, now, CopenhagenZone, out period);

    // SCRUM-195: the week a moment falls in (e.g. a game's EndTime), in Danish time.
    public static WeeklyPeriod Containing(DateTime utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return ForMonday(ISOWeek.ToDateTime(ISOWeek.GetYear(local), ISOWeek.GetWeekOfYear(local), DayOfWeek.Monday), zone);
    }

    public static WeeklyPeriod Containing(DateTime utc) => Containing(utc, CopenhagenZone);

    // The ISO week as text, e.g. "2026-W48". Used as the week's cache key.
    public string Key
    {
        get
        {
            var monday = WeekStart.DateTime;
            return $"{ISOWeek.GetYear(monday):0000}-W{ISOWeek.GetWeekOfYear(monday):00}";
        }
    }

    // `monday` is a local date (time of day 00:00). Midnight never falls in a Copenhagen DST gap
    // (the clock jumps at 02:00/03:00), so converting local midnight is unambiguous.
    private static WeeklyPeriod ForMonday(DateTime monday, TimeZoneInfo zone)
    {
        var startLocal = DateTime.SpecifyKind(monday.Date, DateTimeKind.Unspecified);
        var nextMondayLocal = startLocal.AddDays(7);
        var lastSecondLocal = nextMondayLocal.AddSeconds(-1);

        // Offsets are computed per moment: the Sunday end can have a different offset than the Monday start.
        return new WeeklyPeriod(
            new DateTimeOffset(startLocal, zone.GetUtcOffset(startLocal)),
            new DateTimeOffset(lastSecondLocal, zone.GetUtcOffset(lastSecondLocal)),
            TimeZoneInfo.ConvertTimeToUtc(startLocal, zone),
            TimeZoneInfo.ConvertTimeToUtc(nextMondayLocal, zone));
    }

    // Strict "YYYY-Www": 4-digit year, 'W' (upper or lower case), exactly 2-digit week.
    private static bool TryParseIsoWeek(string text, out DateTime monday)
    {
        monday = default;
        if (text.Length != 8 || text[4] != '-' || (text[5] != 'W' && text[5] != 'w'))
            return false;
        if (!text.AsSpan(0, 4).ContainsOnlyAsciiDigits() || !text.AsSpan(6, 2).ContainsOnlyAsciiDigits())
            return false;

        var year = int.Parse(text.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var weekNumber = int.Parse(text.AsSpan(6, 2), CultureInfo.InvariantCulture);
        if (year < MinYear || year > MaxYear || weekNumber < 1 || weekNumber > ISOWeek.GetWeeksInYear(year))
            return false;

        monday = ISOWeek.ToDateTime(year, weekNumber, DayOfWeek.Monday);
        return true;
    }
}

internal static class SpanExtensions
{
    public static bool ContainsOnlyAsciiDigits(this ReadOnlySpan<char> span)
    {
        foreach (var c in span)
            if (c is < '0' or > '9')
                return false;
        return true;
    }
}
