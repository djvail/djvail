using System;
using System.Globalization;

namespace ValiITMonitor.Processing
{
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
    /// Calendar maths for periods. All dates here are LOCAL dates (midnight),
    /// because "a day" for a report means the server's local day, not a UTC day.
    /// </summary>
    public static class Periods
    {
        public static readonly PeriodType[] All = { PeriodType.Day, PeriodType.Week, PeriodType.Month, PeriodType.Quarter, PeriodType.Year };

        public static readonly PeriodType[] AboveDay = { PeriodType.Week, PeriodType.Month, PeriodType.Quarter, PeriodType.Year };

        /// <summary>The first day of the period that contains <paramref name="date"/>.</summary>
        public static DateTime StartOf(PeriodType type, DateTime date, DayOfWeek firstDayOfWeek)
        {
            var d = date.Date;
            switch (type)
            {
                case PeriodType.Day: return d;
                case PeriodType.Week: return d.AddDays(-(((int)d.DayOfWeek - (int)firstDayOfWeek + 7) % 7));
                case PeriodType.Month: return new DateTime(d.Year, d.Month, 1);
                case PeriodType.Quarter: return new DateTime(d.Year, ((d.Month - 1) / 3 * 3) + 1, 1);
                case PeriodType.Year: return new DateTime(d.Year, 1, 1);
                default: throw new ArgumentOutOfRangeException("type");
            }
        }

        /// <summary>The first day of the period after the one starting at <paramref name="start"/>.</summary>
        public static DateTime Next(PeriodType type, DateTime start)
        {
            switch (type)
            {
                case PeriodType.Day: return start.AddDays(1);
                case PeriodType.Week: return start.AddDays(7);
                case PeriodType.Month: return start.AddMonths(1);
                case PeriodType.Quarter: return start.AddMonths(3);
                case PeriodType.Year: return start.AddYears(1);
                default: throw new ArgumentOutOfRangeException("type");
            }
        }

        public static DateTime Previous(PeriodType type, DateTime start, DayOfWeek firstDayOfWeek)
        {
            return StartOf(type, start.AddDays(-1), firstDayOfWeek);
        }

        /// <summary>Human-friendly name, used in report titles.</summary>
        public static string Label(PeriodType type, DateTime start)
        {
            switch (type)
            {
                case PeriodType.Day: return start.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
                case PeriodType.Week: return "Week of " + start.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
                case PeriodType.Month: return start.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
                case PeriodType.Quarter: return "Q" + ((start.Month - 1) / 3 + 1) + " " + start.Year;
                case PeriodType.Year: return start.Year.ToString(CultureInfo.InvariantCulture);
                default: return start.ToString("yyyy-MM-dd");
            }
        }

        /// <summary>Short sortable name, used in file names.</summary>
        public static string FileStem(PeriodType type, DateTime start)
        {
            switch (type)
            {
                case PeriodType.Day: return start.ToString("yyyy-MM-dd");
                case PeriodType.Week: return start.ToString("yyyy-MM-dd") + "_week";
                case PeriodType.Month: return start.ToString("yyyy-MM");
                case PeriodType.Quarter: return start.Year + "-Q" + ((start.Month - 1) / 3 + 1);
                case PeriodType.Year: return start.Year.ToString(CultureInfo.InvariantCulture);
                default: return start.ToString("yyyy-MM-dd");
            }
        }

        public static string Adjective(PeriodType type)
        {
            switch (type)
            {
                case PeriodType.Day: return "Daily";
                case PeriodType.Week: return "Weekly";
                case PeriodType.Month: return "Monthly";
                case PeriodType.Quarter: return "Quarterly";
                case PeriodType.Year: return "Yearly";
                default: return type.ToString();
            }
        }

        /// <summary>Parses "daily", "week", "monthly" etc. from the command line.</summary>
        public static bool TryParse(string text, out PeriodType type)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "day":
                case "daily": type = PeriodType.Day; return true;
                case "week":
                case "weekly": type = PeriodType.Week; return true;
                case "month":
                case "monthly": type = PeriodType.Month; return true;
                case "quarter":
                case "quarterly": type = PeriodType.Quarter; return true;
                case "year":
                case "yearly":
                case "annual": type = PeriodType.Year; return true;
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
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
        }
    }
}
