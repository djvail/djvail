using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using VailITDashboard.Alerts;

namespace VailITDashboard.Data;

/// <summary>Alert history (what went wrong and when) plus the per-series state the alert rules need.</summary>
public sealed class AlertStore(Database db, IOptions<DashboardOptions> options)
{
    private const string AlertColumns = "id, machine_id, metric, instance, level, peak_level, value, threshold, message, opened, closed, acknowledged, acknowledged_by";

    // ------------------------------------------------------------------ rule state

    public Dictionary<SeriesKey, AlertState> GetStates(SqliteConnection c, SqliteTransaction tx, string machineId)
    {
        using var cmd = c.Command("SELECT metric, instance, active_level, change_count, last_notified, alert_id, last_ts FROM alert_state WHERE machine_id = $id", tx)
            .With("$id", machineId);
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<SeriesKey, AlertState>();
        while (r.Read())
            map[new SeriesKey(r.GetString(0), r.GetString(1))] =
                new AlertState((AlertLevel)r.GetInt32(2), r.GetInt32(3), r.GetInt64OrNull(4), r.GetInt64OrNull(5), r.GetInt64OrNull(6));
        return map;
    }

    public void SaveState(SqliteConnection c, SqliteTransaction tx, string machineId, SeriesKey key, AlertState s)
    {
        if (s == AlertState.Initial)
        {
            using var del = c.Command("DELETE FROM alert_state WHERE machine_id = $id AND metric = $m AND instance = $i", tx)
                .With("$id", machineId).With("$m", key.Metric).With("$i", key.Instance);
            del.ExecuteNonQuery();
            return;
        }
        using var cmd = c.Command("""
            INSERT INTO alert_state (machine_id, metric, instance, active_level, change_count, last_notified, alert_id, last_ts)
            VALUES ($id, $m, $i, $l, $n, $ln, $a, $ts)
            ON CONFLICT DO UPDATE SET active_level = excluded.active_level, change_count = excluded.change_count,
                                      last_notified = excluded.last_notified, alert_id = excluded.alert_id, last_ts = excluded.last_ts
            """, tx).With("$id", machineId).With("$m", key.Metric).With("$i", key.Instance).With("$l", (int)s.Active)
                    .With("$n", s.ChangeCount).With("$ln", s.LastNotified).With("$a", s.AlertId).With("$ts", s.LastTs);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ alert rows

    public long OpenAlert(SqliteConnection c, SqliteTransaction tx, string machineId, SeriesKey key, AlertLevel level, double? value, double? threshold, string message, long now)
    {
        using var cmd = c.Command($"""
            INSERT INTO alerts (machine_id, metric, instance, level, peak_level, value, threshold, message, opened)
            VALUES ($id, $m, $i, $l, $l, $v, $t, $msg, $now) RETURNING id
            """, tx).With("$id", machineId).With("$m", key.Metric).With("$i", key.Instance).With("$l", (int)level)
                    .With("$v", value).With("$t", threshold).With("$msg", message).With("$now", now);
        return (long)cmd.ExecuteScalar()!;
    }

    public void UpdateAlert(SqliteConnection c, SqliteTransaction tx, long alertId, AlertLevel level, double? value, double? threshold, string message)
    {
        using var cmd = c.Command("""
            UPDATE alerts SET level = $l, peak_level = max(peak_level, $l), value = $v, threshold = $t, message = $msg WHERE id = $id
            """, tx).With("$id", alertId).With("$l", (int)level).With("$v", value).With("$t", threshold).With("$msg", message);
        cmd.ExecuteNonQuery();
    }

    public void CloseAlert(SqliteConnection c, SqliteTransaction tx, long alertId, long now)
    {
        using var cmd = c.Command("UPDATE alerts SET closed = $now WHERE id = $id AND closed IS NULL", tx).With("$id", alertId).With("$now", now);
        cmd.ExecuteNonQuery();
    }

    public List<Alert> GetOpen(string? machineId = null)
    {
        using var c = db.Open();
        using var cmd = c.Command($"SELECT {AlertColumns} FROM alerts WHERE closed IS NULL {(machineId is null ? "" : "AND machine_id = $id")} ORDER BY level DESC, opened DESC")
            .With("$id", machineId);
        return Read(cmd);
    }

    public List<Alert> GetRecent(int limit, string? machineId = null)
    {
        using var c = db.Open();
        using var cmd = c.Command($"SELECT {AlertColumns} FROM alerts {(machineId is null ? "" : "WHERE machine_id = $id")} ORDER BY opened DESC LIMIT $n")
            .With("$id", machineId).With("$n", limit);
        return Read(cmd);
    }

    public void Acknowledge(long alertId, string by)
    {
        using var c = db.Open();
        using var cmd = c.Command("UPDATE alerts SET acknowledged = $now, acknowledged_by = $by WHERE id = $id AND acknowledged IS NULL")
            .With("$id", alertId).With("$by", by).With("$now", UnixTime.Now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Deletes closed alerts older than KeepAlertsDays.</summary>
    public int DeleteOldAlerts()
    {
        if (options.Value.KeepAlertsDays <= 0) return 0;
        long cutoff = UnixTime.Now - (long)TimeSpan.FromDays(options.Value.KeepAlertsDays).TotalSeconds;
        using var c = db.Open();
        using var cmd = c.Command("DELETE FROM alerts WHERE closed IS NOT NULL AND closed < $cut").With("$cut", cutoff);
        return cmd.ExecuteNonQuery();
    }

    private static List<Alert> Read(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var list = new List<Alert>();
        while (r.Read())
        {
            list.Add(new Alert
            {
                Id = r.GetInt64(0), MachineId = r.GetString(1), Metric = r.GetString(2), Instance = r.GetString(3),
                Level = (AlertLevel)r.GetInt32(4), PeakLevel = (AlertLevel)r.GetInt32(5), Value = r.GetDoubleOrNull(6),
                Threshold = r.GetDoubleOrNull(7), Message = r.GetString(8), Opened = r.GetInt64(9), Closed = r.GetInt64OrNull(10),
                Acknowledged = r.GetInt64OrNull(11), AcknowledgedBy = r.GetStringOrNull(12),
            });
        }
        return list;
    }
}
