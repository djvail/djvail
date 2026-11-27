using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace VailITDashboard.Data;

/// <summary>Machines and their readings: saving reports, and reading them back for the pages.</summary>
public sealed class MachineStore(Database db, IOptions<DashboardOptions> options)
{
    private const string MachineColumns = """
        machine_id, machine_name, display_name, group_name, server_type_hint, tags, agent_version, edition, os,
        logical_processors, total_memory_mb, drives_json, last_boot_utc, first_seen, last_seen, last_sample_ts,
        notes, muted_until
        """;

    // ------------------------------------------------------------------ saving reports

    /// <summary>
    /// Saves one report in a single transaction: creates or updates the machine, then upserts every
    /// reading on (machine, metric, instance, ts) so a resent batch never counts twice.
    /// </summary>
    /// <returns>The newest sample timestamp now stored for the machine, and whether the machine is new.</returns>
    public (long AcceptedThrough, bool IsNew) SaveReport(MonitorIdentity id, IReadOnlyList<SampleDto> samples)
    {
        string machineId = id.MachineId.ToString("D");
        long now = UnixTime.Now;
        long newest = samples.Count == 0 ? 0 : samples.Max(s => UnixTime.From(s.TimestampUtc));

        using var c = db.Open();
        using var tx = c.BeginTransaction();

        bool isNew;
        using (var check = c.Command("SELECT count(*) FROM machines WHERE machine_id = $id", tx).With("$id", machineId))
            isNew = (long)check.ExecuteScalar()! == 0;

        string group = string.IsNullOrWhiteSpace(id.ServerType) ? options.Value.DefaultGroup : id.ServerType.Trim();

        // group_name and display_name are only set on insert: after that the dashboard's values win.
        using (var cmd = c.Command("""
            INSERT INTO machines (machine_id, machine_name, group_name, server_type_hint, tags, agent_version, edition, os,
                                  logical_processors, total_memory_mb, drives_json, last_boot_utc, first_seen, last_seen, last_sample_ts)
            VALUES ($id, $name, $group, $hint, $tags, $ver, $ed, $os, $cpus, $mem, $drives, $boot, $now, $now, $newest)
            ON CONFLICT(machine_id) DO UPDATE SET
                machine_name = excluded.machine_name, server_type_hint = excluded.server_type_hint, tags = excluded.tags,
                agent_version = excluded.agent_version, edition = excluded.edition, os = excluded.os,
                logical_processors = excluded.logical_processors, total_memory_mb = excluded.total_memory_mb,
                drives_json = excluded.drives_json, last_boot_utc = excluded.last_boot_utc, last_seen = excluded.last_seen,
                last_sample_ts = max(coalesce(last_sample_ts, 0), coalesce(excluded.last_sample_ts, 0))
            """, tx))
        {
            cmd.With("$id", machineId).With("$name", Clean(id.MachineName, machineId)).With("$group", group)
               .With("$hint", id.ServerType ?? "").With("$tags", string.Join(",", id.Tags ?? []))
               .With("$ver", id.AgentVersion ?? "").With("$ed", id.Edition ?? "").With("$os", id.Os ?? "")
               .With("$cpus", id.LogicalProcessors).With("$mem", id.TotalMemoryMb)
               .With("$drives", JsonSerializer.Serialize(id.Drives ?? []))
               .With("$boot", id.LastBootUtc == default ? null : UnixTime.From(id.LastBootUtc))
               .With("$now", now).With("$newest", newest == 0 ? null : newest);
            cmd.ExecuteNonQuery();
        }

        using var insert = c.Command("""
            INSERT INTO readings (machine_id, metric, instance, ts, value) VALUES ($id, $m, $i, $ts, $v)
            ON CONFLICT DO UPDATE SET value = excluded.value
            """, tx);
        using var latest = c.Command("""
            INSERT INTO latest (machine_id, metric, instance, ts, value) VALUES ($id, $m, $i, $ts, $v)
            ON CONFLICT DO UPDATE SET ts = excluded.ts, value = excluded.value WHERE excluded.ts >= latest.ts
            """, tx);
        var p = new[] { "$id", "$m", "$i", "$ts", "$v" };
        foreach (var cmd in new[] { insert, latest })
            foreach (var name in p) cmd.Parameters.Add(new SqliteParameter(name, null));

        foreach (var sample in samples)
        {
            long ts = UnixTime.From(sample.TimestampUtc);
            foreach (var r in sample.Readings)
            {
                foreach (var cmd in new[] { insert, latest })
                {
                    cmd.Parameters["$id"].Value = machineId;
                    cmd.Parameters["$m"].Value = r.Metric;
                    cmd.Parameters["$i"].Value = r.Instance ?? "";
                    cmd.Parameters["$ts"].Value = ts;
                    cmd.Parameters["$v"].Value = r.Value;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        long accepted;
        using (var q = c.Command("SELECT coalesce(last_sample_ts, 0) FROM machines WHERE machine_id = $id", tx).With("$id", machineId))
            accepted = (long)q.ExecuteScalar()!;

        tx.Commit();
        return (accepted, isNew);
    }

    private static string Clean(string? name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

    // ------------------------------------------------------------------ reading machines

    public List<Machine> GetAll()
    {
        using var c = db.Open();
        using var cmd = c.Command($"SELECT {MachineColumns} FROM machines ORDER BY group_name, coalesce(display_name, machine_name)");
        return ReadMachines(cmd);
    }

    public Machine? Get(string machineId)
    {
        using var c = db.Open();
        using var cmd = c.Command($"SELECT {MachineColumns} FROM machines WHERE machine_id = $id").With("$id", machineId);
        return ReadMachines(cmd).FirstOrDefault();
    }

    public List<string> GetGroups()
    {
        using var c = db.Open();
        using var cmd = c.Command("SELECT DISTINCT group_name FROM machines ORDER BY group_name");
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static List<Machine> ReadMachines(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var list = new List<Machine>();
        while (r.Read())
        {
            list.Add(new Machine
            {
                MachineId = r.GetString(0), MachineName = r.GetString(1), DisplayName = r.GetStringOrNull(2),
                GroupName = r.GetString(3), ServerTypeHint = r.GetString(4), Tags = r.GetString(5),
                AgentVersion = r.GetString(6), Edition = r.GetString(7), Os = r.GetString(8),
                LogicalProcessors = r.GetInt32(9), TotalMemoryMb = r.GetInt64(10), DrivesJson = r.GetString(11),
                LastBootUtc = r.GetInt64OrNull(12), FirstSeen = r.GetInt64(13), LastSeen = r.GetInt64(14),
                LastSampleTs = r.GetInt64OrNull(15), Notes = r.GetString(16), MutedUntil = r.GetInt64OrNull(17),
            });
        }
        return list;
    }

    // ------------------------------------------------------------------ editing machines

    public void Update(string machineId, string? displayName, string group, string notes)
    {
        using var c = db.Open();
        using var cmd = c.Command("UPDATE machines SET display_name = $d, group_name = $g, notes = $n WHERE machine_id = $id")
            .With("$id", machineId).With("$d", string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim())
            .With("$g", string.IsNullOrWhiteSpace(group) ? options.Value.DefaultGroup : group.Trim()).With("$n", notes ?? "");
        cmd.ExecuteNonQuery();
    }

    /// <summary>Stops emails for a machine until the given time (null = unmute). Alerts are still recorded.</summary>
    public void SetMute(string machineId, long? until)
    {
        using var c = db.Open();
        using var cmd = c.Command("UPDATE machines SET muted_until = $u WHERE machine_id = $id").With("$id", machineId).With("$u", until);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Removes a machine and everything recorded about it (for decommissioned servers).</summary>
    public void Delete(string machineId)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        foreach (var sql in new[]
        {
            "DELETE FROM readings WHERE machine_id = $id", "DELETE FROM latest WHERE machine_id = $id",
            "DELETE FROM alert_state WHERE machine_id = $id", "DELETE FROM alerts WHERE machine_id = $id",
            "DELETE FROM thresholds WHERE scope = 'machine' AND scope_value = $id",
            "UPDATE api_keys SET revoked = coalesce(revoked, strftime('%s','now')) WHERE machine_id = $id",
            "DELETE FROM machines WHERE machine_id = $id",
        })
        {
            using var cmd = c.Command(sql, tx).With("$id", machineId);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ------------------------------------------------------------------ readings

    /// <summary>The newest value of every series, for every machine (one query for the overview page).</summary>
    public Dictionary<string, List<LatestReading>> GetLatestForAll()
    {
        using var c = db.Open();
        using var cmd = c.Command("SELECT machine_id, metric, instance, ts, value FROM latest");
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<string, List<LatestReading>>();
        while (r.Read())
        {
            if (!map.TryGetValue(r.GetString(0), out var list)) map[r.GetString(0)] = list = [];
            list.Add(new LatestReading(r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetDouble(4)));
        }
        return map;
    }

    public List<LatestReading> GetLatest(string machineId)
    {
        using var c = db.Open();
        using var cmd = c.Command("SELECT metric, instance, ts, value FROM latest WHERE machine_id = $id").With("$id", machineId);
        using var r = cmd.ExecuteReader();
        var list = new List<LatestReading>();
        while (r.Read()) list.Add(new LatestReading(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetDouble(3)));
        return list;
    }

    /// <summary>
    /// One series for a chart, averaged into buckets so a week of minute readings is still a few hundred points.
    /// </summary>
    public List<ChartPoint> GetSeries(string machineId, string metric, string instance, long fromTs, long toTs, int bucketSeconds)
    {
        bucketSeconds = Math.Max(1, bucketSeconds);
        using var c = db.Open();
        using var cmd = c.Command("""
            SELECT (ts / $b) * $b AS bucket, avg(value)
            FROM readings
            WHERE machine_id = $id AND metric = $m AND instance = $i AND ts >= $from AND ts <= $to
            GROUP BY bucket ORDER BY bucket
            """).With("$id", machineId).With("$m", metric).With("$i", instance).With("$from", fromTs).With("$to", toTs).With("$b", bucketSeconds);
        using var r = cmd.ExecuteReader();
        var list = new List<ChartPoint>();
        while (r.Read()) list.Add(new ChartPoint(r.GetInt64(0), r.GetDouble(1)));
        return list;
    }

    /// <summary>Deletes readings older than KeepReadingsDays. Returns how many rows went.</summary>
    public int DeleteOldReadings()
    {
        long cutoff = UnixTime.Now - (long)TimeSpan.FromDays(Math.Max(1, options.Value.KeepReadingsDays)).TotalSeconds;
        using var c = db.Open();
        using var cmd = c.Command("DELETE FROM readings WHERE ts < $cut").With("$cut", cutoff);
        return cmd.ExecuteNonQuery();
    }
}
