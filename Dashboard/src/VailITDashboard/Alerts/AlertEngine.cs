using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace VailITDashboard.Alerts;

/// <summary>Something worth telling a person about: one series on one machine changed (or is still bad).</summary>
public sealed record AlertEvent(
    Machine Machine, SeriesKey Key, AlertChange Change, AlertLevel Level, AlertLevel PreviousLevel,
    double? Value, double? Threshold, string Message, long AlertId);

/// <summary>
/// Runs each incoming reading through its threshold and the <see cref="AlertRules"/>, records alerts,
/// and hands anything email-worthy to the <see cref="AlertNotifier"/>.
/// </summary>
public sealed class AlertEngine(
    Database db,
    AlertStore alerts,
    ThresholdStore thresholds,
    AlertNotifier notifier,
    IOptions<DashboardOptions> options,
    ILogger<AlertEngine> log)
{
    /// <summary>The pseudo-metric used for "this monitor has stopped reporting".</summary>
    public const string OfflineMetric = "monitor.offline";

    /// <summary>Checks the readings of one report, oldest sample first.</summary>
    public void EvaluateReport(Machine machine, IEnumerable<SampleDto> samples)
    {
        var all = thresholds.GetAll();
        var events = new List<AlertEvent>();

        using var c = db.Open();
        using var tx = c.BeginTransaction();
        var states = alerts.GetStates(c, tx, machine.MachineId);

        // A report coming in also means the machine is back online.
        if (states.ContainsKey(new SeriesKey(OfflineMetric, "")))
            Add(events, Apply(c, tx, machine, states, new SeriesKey(OfflineMetric, ""), AlertLevel.Ok, null, null, "Reporting again", UnixTime.Now, required: 1));

        foreach (var sample in samples.OrderBy(s => s.TimestampUtc))
        {
            long ts = UnixTime.From(sample.TimestampUtc);
            foreach (var r in sample.Readings)
            {
                var key = new SeriesKey(r.Metric, r.Instance ?? "");
                var t = ThresholdResolver.Resolve(all, machine, key);

                // No threshold (or it was switched off) means "fine", which also clears any old alert.
                if (t is null && !states.ContainsKey(key)) continue;

                // Already counted this reading (the monitor resent a batch): don't count it twice.
                if (states.TryGetValue(key, out var seen) && seen.LastTs >= ts) continue;

                var level = t is null ? AlertLevel.Ok : AlertRules.LevelFor(r.Value, t.Direction, t.Warning, t.Critical);
                double? limit = t is null ? null : level == AlertLevel.Critical ? t.Critical : t.Warning ?? t.Critical;
                string message = Describe(key, r.Value, t, level);
                Add(events, Apply(c, tx, machine, states, key, level, r.Value, limit, message, ts, options.Value.Alerts.ConsecutiveReports));
            }
        }

        tx.Commit();
        Notify(machine, events);
    }

    /// <summary>Called by the <see cref="Workers.WatchdogWorker"/> for a machine that has stopped reporting.</summary>
    public void EvaluateOffline(Machine machine, TimeSpan silentFor)
    {
        var events = new List<AlertEvent>();
        using (var c = db.Open())
        using (var tx = c.BeginTransaction())
        {
            var states = alerts.GetStates(c, tx, machine.MachineId);
            string message = $"No report for {Format.Duration(silentFor)} (offline after {Format.Duration(options.Value.OfflineAfter)})";
            Add(events, Apply(c, tx, machine, states, new SeriesKey(OfflineMetric, ""), AlertLevel.Critical,
                silentFor.TotalMinutes, options.Value.OfflineAfter.TotalMinutes, message, UnixTime.Now, required: 1));
            tx.Commit();
        }
        Notify(machine, events);
    }

    private AlertEvent? Apply(
        SqliteConnection c, SqliteTransaction tx, Machine machine, Dictionary<SeriesKey, AlertState> states,
        SeriesKey key, AlertLevel observed, double? value, double? limit, string message, long now, int required)
    {
        var before = states.GetValueOrDefault(key, AlertState.Initial);
        var (after, change) = AlertRules.Step(before, observed, now, required, options.Value.Alerts.RemindEveryMinutes * 60);
        after = after with { LastTs = now };

        switch (change)
        {
            case AlertChange.Opened:
                after = after with { AlertId = alerts.OpenAlert(c, tx, machine.MachineId, key, observed, value, limit, message, now) };
                break;
            case AlertChange.Escalated or AlertChange.Lowered or AlertChange.Reminder when after.AlertId is long id:
                alerts.UpdateAlert(c, tx, id, after.Active, value, limit, message);
                break;
            case AlertChange.Resolved when after.AlertId is long id:
                alerts.CloseAlert(c, tx, id, now);
                after = after with { AlertId = null };
                break;
        }

        if (after != before)
        {
            states[key] = after;
            alerts.SaveState(c, tx, machine.MachineId, key, after);
        }

        if (change == AlertChange.None) return null;
        log.LogInformation("{Machine} {Series}: {Change} ({Level}) {Message}", machine.Name, key, change, after.Active, message);
        return new AlertEvent(machine, key, change, after.Active, before.Active, value, limit, message, after.AlertId ?? before.AlertId ?? 0);
    }

    private static void Add(List<AlertEvent> list, AlertEvent? e)
    {
        if (e is not null) list.Add(e);
    }

    private void Notify(Machine machine, List<AlertEvent> events)
    {
        var a = options.Value.Alerts;
        var worthEmailing = events.Where(e => e.Change switch
        {
            AlertChange.Opened or AlertChange.Escalated or AlertChange.Reminder => e.Level >= a.MinimumEmailLevel,
            AlertChange.Resolved => a.SendRecovery && e.PreviousLevel >= a.MinimumEmailLevel,
            _ => false,
        }).ToList();

        if (worthEmailing.Count == 0) return;
        if (machine.IsMuted(UnixTime.Now))
        {
            log.LogInformation("{Machine} is muted; not emailing {Count} alert(s)", machine.Name, worthEmailing.Count);
            return;
        }
        notifier.Enqueue(worthEmailing);
    }

    /// <summary>"Disk free (C:) is 4.2 % - critical at or below 5 %".</summary>
    public static string Describe(SeriesKey key, double value, Threshold? t, AlertLevel level)
    {
        string what = $"{MetricCatalog.Label(key)} is {Format.Value(value, MetricCatalog.Unit(key.Metric))}";
        if (t is null || level == AlertLevel.Ok) return what;
        double? limit = level == AlertLevel.Critical ? t.Critical : t.Warning;
        string op = t.Direction == ThresholdDirection.Above ? "at or above" : "at or below";
        return $"{what} ({level.ToString().ToLowerInvariant()} {op} {Format.Value(limit ?? 0, MetricCatalog.Unit(key.Metric))})";
    }
}
