using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using ValiITMonitor.Collection;
using ValiITMonitor.Processing;

namespace ValiITMonitor.Storage
{
    /// <summary>A pre-calculated summary of one metric over one period.</summary>
    public sealed class RollupRow
    {
        public RollupRow(MetricKey key, double min, double avg, double max, double p95, int count)
        {
            Key = key;
            Min = min;
            Avg = avg;
            Max = max;
            P95 = p95;
            Count = count;
        }

        public MetricKey Key { get; }
        public double Min { get; }
        public double Avg { get; }
        public double Max { get; }
        public double P95 { get; }
        public int Count { get; }
    }

    /// <summary>One stored reading of one series.</summary>
    public struct SamplePoint
    {
        public SamplePoint(long ts, double value)
        {
            Ts = ts;
            Value = value;
        }

        public long Ts { get; }
        public double Value { get; }
    }

    /// <summary>
    /// All database access - identical design to the .NET 10 edition (same tables, same file
    /// format: you can point either edition at the other's database).
    ///
    /// Microsoft.Data.Sqlite also runs on .NET Framework. The classic alternative,
    /// System.Data.SQLite, works almost the same way (SQLiteConnection instead of SqliteConnection).
    /// </summary>
    public sealed class MetricStore
    {
        private const int SchemaVersion = 1;
        private const string DateFormat = "yyyy-MM-dd";

        private readonly string _connectionString;
        private readonly ConcurrentDictionary<MetricKey, long> _ids = new ConcurrentDictionary<MetricKey, long>();
        private readonly ConcurrentDictionary<long, MetricKey> _keys = new ConcurrentDictionary<long, MetricKey>();

        public MetricStore(string databasePath)
        {
            DatabasePath = databasePath;
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath));
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                DefaultTimeout = 30,
            }.ToString();
            Initialize();
        }

        public string DatabasePath { get; }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private void Initialize()
        {
            using (var db = Open())
            {
                Execute(db, "PRAGMA auto_vacuum = INCREMENTAL;");
                Execute(db, "PRAGMA journal_mode = WAL;");
                Execute(db, @"
                    CREATE TABLE IF NOT EXISTS metrics (
                        id        INTEGER PRIMARY KEY,
                        name      TEXT NOT NULL,
                        instance  TEXT NOT NULL DEFAULT '',
                        UNIQUE (name, instance));

                    CREATE TABLE IF NOT EXISTS samples (
                        ts        INTEGER NOT NULL,
                        metric_id INTEGER NOT NULL,
                        value     REAL    NOT NULL,
                        PRIMARY KEY (ts, metric_id)) WITHOUT ROWID;

                    CREATE TABLE IF NOT EXISTS rollups (
                        period    INTEGER NOT NULL,
                        start     TEXT    NOT NULL,
                        metric_id INTEGER NOT NULL,
                        min REAL NOT NULL, avg REAL NOT NULL, max REAL NOT NULL, p95 REAL NOT NULL,
                        count     INTEGER NOT NULL,
                        PRIMARY KEY (period, start, metric_id)) WITHOUT ROWID;

                    CREATE TABLE IF NOT EXISTS task_state (
                        name      TEXT PRIMARY KEY,
                        last_run  TEXT NOT NULL);

                    CREATE TABLE IF NOT EXISTS report_log (
                        period    INTEGER NOT NULL,
                        start     TEXT    NOT NULL,
                        file      TEXT    NOT NULL,
                        created   TEXT    NOT NULL,
                        PRIMARY KEY (period, start));");
                Execute(db, "PRAGMA user_version = " + SchemaVersion + ";");

                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, name, instance FROM metrics";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            Remember(r.GetInt64(0), new MetricKey(r.GetString(1), r.GetString(2)));
                    }
                }
            }
        }

        private void Remember(long id, MetricKey key)
        {
            _ids[key] = id;
            _keys[id] = key;
        }

        private long GetOrCreateMetricId(SqliteConnection db, SqliteTransaction tx, MetricKey key)
        {
            long id;
            if (_ids.TryGetValue(key, out id)) return id;

            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO metrics (name, instance) VALUES ($n, $i)
                    ON CONFLICT (name, instance) DO UPDATE SET name = excluded.name
                    RETURNING id;";
                cmd.Parameters.AddWithValue("$n", key.Metric);
                cmd.Parameters.AddWithValue("$i", key.Instance);
                id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            Remember(id, key);
            return id;
        }

        private static string Day(DateTime date)
        {
            return date.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        // ================================================================ samples

        public void WriteSamples(long unixTime, ICollection<Reading> readings)
        {
            WriteSamples(new[] { new KeyValuePair<long, ICollection<Reading>>(unixTime, readings) });
        }

        /// <summary>Writes many readings in a single transaction.</summary>
        public void WriteSamples(IEnumerable<KeyValuePair<long, ICollection<Reading>>> batch)
        {
            using (var db = Open())
            using (var tx = db.BeginTransaction())
            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR REPLACE INTO samples (ts, metric_id, value) VALUES ($ts, $id, $v)";
                var pTs = cmd.Parameters.Add("$ts", SqliteType.Integer);
                var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
                var pV = cmd.Parameters.Add("$v", SqliteType.Real);
                cmd.Prepare();

                foreach (var item in batch)
                {
                    foreach (var reading in item.Value)
                    {
                        if (double.IsNaN(reading.Value) || double.IsInfinity(reading.Value)) continue;
                        pTs.Value = item.Key;
                        pId.Value = GetOrCreateMetricId(db, tx, new MetricKey(reading.Metric, reading.Instance));
                        pV.Value = reading.Value;
                        cmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
        }

        public Dictionary<MetricKey, List<SamplePoint>> GetSamples(long fromInclusive, long toExclusive)
        {
            var result = new Dictionary<MetricKey, List<SamplePoint>>();
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT ts, metric_id, value FROM samples WHERE ts >= $a AND ts < $b ORDER BY ts";
                cmd.Parameters.AddWithValue("$a", fromInclusive);
                cmd.Parameters.AddWithValue("$b", toExclusive);

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        MetricKey key;
                        if (!_keys.TryGetValue(r.GetInt64(1), out key)) continue;
                        List<SamplePoint> list;
                        if (!result.TryGetValue(key, out list))
                        {
                            list = new List<SamplePoint>(1500);
                            result[key] = list;
                        }
                        list.Add(new SamplePoint(r.GetInt64(0), r.GetDouble(2)));
                    }
                }
            }

            return result;
        }

        public long? GetEarliestSampleTime()
        {
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT MIN(ts) FROM samples";
                var value = cmd.ExecuteScalar();
                return value is long ? (long?)value : null;
            }
        }

        public int DeleteSamplesBefore(long unixTime)
        {
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM samples WHERE ts < $t";
                cmd.Parameters.AddWithValue("$t", unixTime);
                return cmd.ExecuteNonQuery();
            }
        }

        // ================================================================ rollups

        public bool RollupExists(PeriodType period, DateTime start)
        {
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT 1 FROM rollups WHERE period = $p AND start = $s LIMIT 1";
                cmd.Parameters.AddWithValue("$p", (int)period);
                cmd.Parameters.AddWithValue("$s", Day(start));
                return cmd.ExecuteScalar() != null;
            }
        }

        public void SaveRollups(PeriodType period, DateTime start, IEnumerable<RollupRow> rows)
        {
            using (var db = Open())
            using (var tx = db.BeginTransaction())
            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO rollups (period, start, metric_id, min, avg, max, p95, count)
                    VALUES ($p, $s, $id, $min, $avg, $max, $p95, $c)";
                cmd.Parameters.AddWithValue("$p", (int)period);
                cmd.Parameters.AddWithValue("$s", Day(start));
                var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
                var pMin = cmd.Parameters.Add("$min", SqliteType.Real);
                var pAvg = cmd.Parameters.Add("$avg", SqliteType.Real);
                var pMax = cmd.Parameters.Add("$max", SqliteType.Real);
                var pP95 = cmd.Parameters.Add("$p95", SqliteType.Real);
                var pC = cmd.Parameters.Add("$c", SqliteType.Integer);

                foreach (var row in rows)
                {
                    pId.Value = GetOrCreateMetricId(db, tx, row.Key);
                    pMin.Value = row.Min;
                    pAvg.Value = row.Avg;
                    pMax.Value = row.Max;
                    pP95.Value = row.P95;
                    pC.Value = row.Count;
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        public SortedDictionary<DateTime, List<RollupRow>> GetRollups(PeriodType period, DateTime from, DateTime to)
        {
            var result = new SortedDictionary<DateTime, List<RollupRow>>();
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT start, metric_id, min, avg, max, p95, count FROM rollups
                    WHERE period = $p AND start >= $a AND start < $b ORDER BY start";
                cmd.Parameters.AddWithValue("$p", (int)period);
                cmd.Parameters.AddWithValue("$a", Day(from));
                cmd.Parameters.AddWithValue("$b", Day(to));

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        MetricKey key;
                        if (!_keys.TryGetValue(r.GetInt64(1), out key)) continue;
                        var start = DateTime.ParseExact(r.GetString(0), DateFormat, CultureInfo.InvariantCulture);
                        List<RollupRow> list;
                        if (!result.TryGetValue(start, out list))
                        {
                            list = new List<RollupRow>();
                            result[start] = list;
                        }
                        list.Add(new RollupRow(key, r.GetDouble(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetInt32(6)));
                    }
                }
            }

            return result;
        }

        public DateTime? GetEarliestRollupStart(PeriodType period)
        {
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT MIN(start) FROM rollups WHERE period = $p";
                cmd.Parameters.AddWithValue("$p", (int)period);
                var s = cmd.ExecuteScalar() as string;
                if (s == null) return null;
                return DateTime.ParseExact(s, DateFormat, CultureInfo.InvariantCulture);
            }
        }

        public int DeleteRollupsBefore(PeriodType period, DateTime start)
        {
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM rollups WHERE period = $p AND start < $s";
                cmd.Parameters.AddWithValue("$p", (int)period);
                cmd.Parameters.AddWithValue("$s", Day(start));
                return cmd.ExecuteNonQuery();
            }
        }

        // ================================================================ task state + report log

        public DateTime? GetLastRun(string task)
        {
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT last_run FROM task_state WHERE name = $n";
                cmd.Parameters.AddWithValue("$n", task);
                var s = cmd.ExecuteScalar() as string;
                if (s == null) return null;
                return DateTime.Parse(s, CultureInfo.InvariantCulture);
            }
        }

        public void SetLastRun(string task, DateTime when)
        {
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "INSERT OR REPLACE INTO task_state (name, last_run) VALUES ($n, $t)";
                cmd.Parameters.AddWithValue("$n", task);
                cmd.Parameters.AddWithValue("$t", when.ToString("s", CultureInfo.InvariantCulture));
                cmd.ExecuteNonQuery();
            }
        }

        public bool IsReportLogged(PeriodType period, DateTime start)
        {
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT 1 FROM report_log WHERE period = $p AND start = $s";
                cmd.Parameters.AddWithValue("$p", (int)period);
                cmd.Parameters.AddWithValue("$s", Day(start));
                return cmd.ExecuteScalar() != null;
            }
        }

        public void LogReport(PeriodType period, DateTime start, string file)
        {
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "INSERT OR REPLACE INTO report_log (period, start, file, created) VALUES ($p, $s, $f, $c)";
                cmd.Parameters.AddWithValue("$p", (int)period);
                cmd.Parameters.AddWithValue("$s", Day(start));
                cmd.Parameters.AddWithValue("$f", file);
                cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
                cmd.ExecuteNonQuery();
            }
        }

        public void Compact()
        {
            using (var db = Open())
            {
                Execute(db, "PRAGMA incremental_vacuum;");
                Execute(db, "PRAGMA wal_checkpoint(TRUNCATE);");
                Execute(db, "PRAGMA optimize;");
            }
        }

        private static void Execute(SqliteConnection db, string sql)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }
    }
}
