using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ValiITMonitor.Storage;

/// <summary>A pre-calculated summary of one metric over one period.</summary>
public sealed record RollupRow(MetricKey Key, double Min, double Avg, double Max, double P95, int Count);

/// <summary>
/// All database access. The database is a single SQLite file (no server to install).
///
/// Tables:
///   metrics          id -> (name, instance). Keeps the big samples table small.
///   samples          one row per metric per reading: (ts, metric_id, value). ts = Unix seconds UTC.
///   rollups          one row per metric per period: (period, start, metric_id, min, avg, max, p95, count).
///   task_state       when each cleanup task last ran.
///   report_log       which PDF reports have been produced (so they are never produced twice).
///
/// Open it with any SQLite tool (e.g. "DB Browser for SQLite") to explore the data yourself.
/// </summary>
public sealed class MetricStore
{
    private const int SchemaVersion = 1;
    private const string DateFormat = "yyyy-MM-dd";

    private readonly string _connectionString;
    private readonly ConcurrentDictionary<MetricKey, long> _ids = new();
    private readonly ConcurrentDictionary<long, MetricKey> _keys = new();

    public MetricStore(IOptions<MonitorOptions> options) : this(options.Value.DatabasePath) { }

    public MetricStore(string databasePath)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
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
        using var db = Open();

        // auto_vacuum must be set before the first table is created; it lets us give
        // deleted space back to the disk later with "PRAGMA incremental_vacuum".
        Execute(db, "PRAGMA auto_vacuum = INCREMENTAL;");
        Execute(db, "PRAGMA journal_mode = WAL;");   // readers don't block the writer
        Execute(db, """
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
                PRIMARY KEY (period, start));
            """);
        Execute(db, $"PRAGMA user_version = {SchemaVersion};");

        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, name, instance FROM metrics";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            Remember(r.GetInt64(0), new MetricKey(r.GetString(1), r.GetString(2)));
    }

    private void Remember(long id, MetricKey key)
    {
        _ids[key] = id;
        _keys[id] = key;
    }

    private long GetOrCreateMetricId(SqliteConnection db, SqliteTransaction tx, MetricKey key)
    {
        if (_ids.TryGetValue(key, out var id)) return id;

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO metrics (name, instance) VALUES ($n, $i)
            ON CONFLICT (name, instance) DO UPDATE SET name = excluded.name
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$n", key.Metric);
        cmd.Parameters.AddWithValue("$i", key.Instance);
        id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        Remember(id, key);
        return id;
    }

    // ================================================================ samples

    public void WriteSamples(long unixTime, IReadOnlyCollection<Reading> readings)
        => WriteSamples([(unixTime, readings)]);   // collection expression (C# 12)

    /// <summary>Writes many readings in a single transaction (fast - used by the demo tool).</summary>
    public void WriteSamples(IEnumerable<(long UnixTime, IReadOnlyCollection<Reading> Readings)> batch)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO samples (ts, metric_id, value) VALUES ($ts, $id, $v)";
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Integer);
        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        var pV = cmd.Parameters.Add("$v", SqliteType.Real);
        cmd.Prepare();

        foreach (var (unixTime, readings) in batch)
        {
            foreach (var reading in readings)
            {
                if (double.IsNaN(reading.Value) || double.IsInfinity(reading.Value)) continue;
                pTs.Value = unixTime;
                pId.Value = GetOrCreateMetricId(db, tx, new MetricKey(reading.Metric, reading.Instance));
                pV.Value = reading.Value;
                cmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    /// <summary>All readings between two Unix times, grouped by series, in time order.</summary>
    public Dictionary<MetricKey, List<(long Ts, double Value)>> GetSamples(long fromInclusive, long toExclusive)
    {
        var result = new Dictionary<MetricKey, List<(long, double)>>();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT ts, metric_id, value FROM samples WHERE ts >= $a AND ts < $b ORDER BY ts";
        cmd.Parameters.AddWithValue("$a", fromInclusive);
        cmd.Parameters.AddWithValue("$b", toExclusive);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!_keys.TryGetValue(r.GetInt64(1), out var key)) continue;
            if (!result.TryGetValue(key, out var list))
                result[key] = list = new List<(long, double)>(1500);
            list.Add((r.GetInt64(0), r.GetDouble(2)));
        }

        return result;
    }

    public long? GetEarliestSampleTime()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT MIN(ts) FROM samples";
        return cmd.ExecuteScalar() is long ts ? ts : null;   // "is" pattern with a declaration
    }

    public int DeleteSamplesBefore(long unixTime)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM samples WHERE ts < $t";
        cmd.Parameters.AddWithValue("$t", unixTime);
        return cmd.ExecuteNonQuery();
    }

    // ================================================================ rollups

    public bool RollupExists(PeriodType period, DateTime start)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM rollups WHERE period = $p AND start = $s LIMIT 1";
        cmd.Parameters.AddWithValue("$p", (int)period);
        cmd.Parameters.AddWithValue("$s", start.ToString(DateFormat, CultureInfo.InvariantCulture));
        return cmd.ExecuteScalar() is not null;
    }

    public void SaveRollups(PeriodType period, DateTime start, IEnumerable<RollupRow> rows)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR REPLACE INTO rollups (period, start, metric_id, min, avg, max, p95, count)
            VALUES ($p, $s, $id, $min, $avg, $max, $p95, $c)
            """;
        cmd.Parameters.AddWithValue("$p", (int)period);
        cmd.Parameters.AddWithValue("$s", start.ToString(DateFormat, CultureInfo.InvariantCulture));
        var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
        var pMin = cmd.Parameters.Add("$min", SqliteType.Real);
        var pAvg = cmd.Parameters.Add("$avg", SqliteType.Real);
        var pMax = cmd.Parameters.Add("$max", SqliteType.Real);
        var pP95 = cmd.Parameters.Add("$p95", SqliteType.Real);
        var pC = cmd.Parameters.Add("$c", SqliteType.Integer);

        foreach (var row in rows)
        {
            pId.Value = GetOrCreateMetricId(db, tx, row.Key);
            (pMin.Value, pAvg.Value, pMax.Value, pP95.Value, pC.Value) = (row.Min, row.Avg, row.Max, row.P95, row.Count);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>Rollups whose start date is in [from, to), keyed by start date.</summary>
    public SortedDictionary<DateTime, List<RollupRow>> GetRollups(PeriodType period, DateTime from, DateTime to)
    {
        var result = new SortedDictionary<DateTime, List<RollupRow>>();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT start, metric_id, min, avg, max, p95, count FROM rollups
            WHERE period = $p AND start >= $a AND start < $b ORDER BY start
            """;
        cmd.Parameters.AddWithValue("$p", (int)period);
        cmd.Parameters.AddWithValue("$a", from.ToString(DateFormat, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$b", to.ToString(DateFormat, CultureInfo.InvariantCulture));

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!_keys.TryGetValue(r.GetInt64(1), out var key)) continue;
            var start = DateTime.ParseExact(r.GetString(0), DateFormat, CultureInfo.InvariantCulture);
            if (!result.TryGetValue(start, out var list))
                result[start] = list = [];
            list.Add(new RollupRow(key, r.GetDouble(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetInt32(6)));
        }

        return result;
    }

    public DateTime? GetEarliestRollupStart(PeriodType period)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT MIN(start) FROM rollups WHERE period = $p";
        cmd.Parameters.AddWithValue("$p", (int)period);
        return cmd.ExecuteScalar() is string s
            ? DateTime.ParseExact(s, DateFormat, CultureInfo.InvariantCulture)
            : null;
    }

    public int DeleteRollupsBefore(PeriodType period, DateTime start)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM rollups WHERE period = $p AND start < $s";
        cmd.Parameters.AddWithValue("$p", (int)period);
        cmd.Parameters.AddWithValue("$s", start.ToString(DateFormat, CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery();
    }

    // ================================================================ task state + report log

    public DateTime? GetLastRun(string task)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT last_run FROM task_state WHERE name = $n";
        cmd.Parameters.AddWithValue("$n", task);
        return cmd.ExecuteScalar() is string s ? DateTime.Parse(s, CultureInfo.InvariantCulture) : null;
    }

    public void SetLastRun(string task, DateTime when)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO task_state (name, last_run) VALUES ($n, $t)";
        cmd.Parameters.AddWithValue("$n", task);
        cmd.Parameters.AddWithValue("$t", when.ToString("s", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public bool IsReportLogged(PeriodType period, DateTime start)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM report_log WHERE period = $p AND start = $s";
        cmd.Parameters.AddWithValue("$p", (int)period);
        cmd.Parameters.AddWithValue("$s", start.ToString(DateFormat, CultureInfo.InvariantCulture));
        return cmd.ExecuteScalar() is not null;
    }

    public void LogReport(PeriodType period, DateTime start, string file)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO report_log (period, start, file, created) VALUES ($p, $s, $f, $c)";
        cmd.Parameters.AddWithValue("$p", (int)period);
        cmd.Parameters.AddWithValue("$s", start.ToString(DateFormat, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$f", file);
        cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Hands free pages back to the file system after big deletes.</summary>
    public void Compact()
    {
        using var db = Open();
        Execute(db, "PRAGMA incremental_vacuum;");
        Execute(db, "PRAGMA wal_checkpoint(TRUNCATE);");
        Execute(db, "PRAGMA optimize;");
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
