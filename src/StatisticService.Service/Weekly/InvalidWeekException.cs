namespace StatisticService.Service.Weekly;

// The week query value was neither "current" nor a valid "YYYY-Www". The API maps this to 400.
public class InvalidWeekException(string? week)
    : Exception($"'{week}' is not a valid week. Use 'current' or an ISO week such as '2026-W47'.");
