using System.Globalization;

namespace ValiITMonitor.Processing;

/// <summary>The summary tiers. Raw minute readings roll into Day; Days roll into the rest.</summary>
public enum PeriodType
{
    Day = 1,
    Week = 2,
    Month = 3,
    Quarter = 4,
    Year = 5,
}

/// <summary>
/// Calendar maths for periods. All dates here are LOCAL dates (midnight, Kind = Unspecified),
/// because "a day" for a report means the server's local day, not a UTC day.
/// </summary>
public static class Periods
{
    public static readonly PeriodType[] All = [PeriodType.Day, PeriodType.Week, PeriodType.Month, PeriodType.Quarter, PeriodType.Year];

    public static readonly PeriodType[] AboveDay = [PeriodType.Week, PeriodType.Month, PeriodType.Quarter, PeriodType.Year];

    /// <summary>The first day of the period that contains <paramref name="date"/>.</summary>
    public static DateTime StartOf(PeriodType type, DateTime date, DayOfWeek firstDayOfWeek)
    {
        var d = date.Date;
        return type switch
        {
            PeriodType.Day => d,
            PeriodType.Week => d.AddDays(-(((int)d.DayOfWeek - (int)firstDayOfWeek + 7) % 7)),
            PeriodType.Month => new DateTime(d.Year, d.Month, 1),
            PeriodType.Quarter => new DateTime(d.Year, ((d.Month - 1) / 3 * 3) + 1, 1),
            PeriodType.Year => new DateTime(d.Year, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    /// <summary>The first day of the period after the one starting at <paramref name="start"/>.</summary>
    public static DateTime Next(PeriodType type, DateTime start) => type switch
    {
        PeriodType.Day => start.AddDays(1),
        PeriodType.Week => start.AddDays(7),
        PeriodType.Month => start.AddMonths(1),
        PeriodType.Quarter => start.AddMonths(3),
        PeriodType.Year => start.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static DateTime Previous(PeriodType type, DateTime start, DayOfWeek firstDayOfWeek)
        => StartOf(type, start.AddDays(-1), firstDayOfWeek);

    /// <summary>Human-friendly name, used in report titles.</summary>
    public static string Label(PeriodType type, DateTime start) => type switch
    {
        PeriodType.Day => start.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture),
        PeriodType.Week => $"Week of {start:MMMM d, yyyy}",
        PeriodType.Month => start.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
        PeriodType.Quarter => $"Q{(start.Month - 1) / 3 + 1} {start.Year}",
        PeriodType.Year => start.Year.ToString(CultureInfo.InvariantCulture),
        _ => start.ToString("yyyy-MM-dd"),
    };

    /// <summary>Short sortable name, used in file names.</summary>
    public static string FileStem(PeriodType type, DateTime start) => type switch
    {
        PeriodType.Day => start.ToString("yyyy-MM-dd"),
        PeriodType.Week => $"{start:yyyy-MM-dd}_week",
        PeriodType.Month => start.ToString("yyyy-MM"),
        PeriodType.Quarter => $"{start.Year}-Q{(start.Month - 1) / 3 + 1}",
        PeriodType.Year => start.Year.ToString(CultureInfo.InvariantCulture),
        _ => start.ToString("yyyy-MM-dd"),
    };

    public static string Adjective(PeriodType type) => type switch
    {
        PeriodType.Day => "Daily",
        PeriodType.Week => "Weekly",
        PeriodType.Month => "Monthly",
        PeriodType.Quarter => "Quarterly",
        PeriodType.Year => "Yearly",
        _ => type.ToString(),
    };

    /// <summary>Parses "daily", "week", "monthly" etc. from the command line.</summary>
    public static bool TryParse(string text, out PeriodType type)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "day" or "daily": type = PeriodType.Day; return true;
            case "week" or "weekly": type = PeriodType.Week; return true;
            case "month" or "monthly": type = PeriodType.Month; return true;
            case "quarter" or "quarterly": type = PeriodType.Quarter; return true;
            case "year" or "yearly" or "annual": type = PeriodType.Year; return true;
            default: type = PeriodType.Day; return false;
        }
    }

    // ----- Local date <-> Unix time (seconds since 1970 UTC, how readings are stored) -----

    public static long ToUnix(DateTime localTime)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(localTime);
        return new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), offset).ToUnixTimeSeconds();
    }

    public static DateTime FromUnix(long unixSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
}
