using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace VailITDashboard.Data;

/// <summary>
/// Owns the SQLite file: creates it, upgrades its tables, and hands out connections.
/// Every other store class asks this one for a connection, so there is one place to change
/// if you ever move to SQL Server (swap SqliteConnection for SqlConnection and fix the SQL).
/// </summary>
/// <remarks>
/// Times are stored as Unix seconds (UTC), the same as VailITMonitor's own database.
/// Machine ids are GUID strings in lower case ("D" format).
/// </remarks>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(IOptions<DashboardOptions> options)
    {
        var o = options.Value;
        Directory.CreateDirectory(o.ResolvedDataFolder);
        DatabasePath = o.DatabasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        // WAL lets the web pages read while a report is being written.
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return c;
    }

    /// <summary>Bump this and add a step to <see cref="Migrations"/> when you change the tables.</summary>
    private static readonly string[] Migrations =
    [
        // Version 1
        """
        CREATE TABLE machines (
            machine_id        TEXT PRIMARY KEY,
            machine_name      TEXT NOT NULL,
            display_name      TEXT NULL,          -- set in the dashboard; NULL = use machine_name
            group_name        TEXT NOT NULL,      -- set from the serverType hint on first report, then the dashboard's
            server_type_hint  TEXT NOT NULL DEFAULT '',
            tags              TEXT NOT NULL DEFAULT '',   -- comma separated
            agent_version     TEXT NOT NULL DEFAULT '',
            edition           TEXT NOT NULL DEFAULT '',
            os                TEXT NOT NULL DEFAULT '',
            logical_processors INTEGER NOT NULL DEFAULT 0,
            total_memory_mb   INTEGER NOT NULL DEFAULT 0,
            drives_json       TEXT NOT NULL DEFAULT '[]',
            last_boot_utc     INTEGER NULL,
            first_seen        INTEGER NOT NULL,
            last_seen         INTEGER NOT NULL,   -- when the last report arrived (dashboard clock)
            last_sample_ts    INTEGER NULL,       -- newest reading timestamp received
            notes             TEXT NOT NULL DEFAULT '',
            muted_until       INTEGER NULL        -- no emails for this machine until then
        );

        CREATE TABLE api_keys (
            id          INTEGER PRIMARY KEY,
            key_hash    TEXT NOT NULL UNIQUE,     -- SHA-256 of the key, hex
            key_prefix  TEXT NOT NULL,            -- first characters, to recognise it in lists
            machine_id  TEXT NULL,                -- NULL until the first report binds it
            label       TEXT NOT NULL DEFAULT '',
            created     INTEGER NOT NULL,
            last_used   INTEGER NULL,
            revoked     INTEGER NULL
        );

        CREATE TABLE readings (
            machine_id  TEXT NOT NULL,
            metric      TEXT NOT NULL,
            instance    TEXT NOT NULL,
            ts          INTEGER NOT NULL,
            value       REAL NOT NULL,
            PRIMARY KEY (machine_id, metric, instance, ts)
        ) WITHOUT ROWID;
        CREATE INDEX ix_readings_ts ON readings (ts);

        CREATE TABLE latest (
            machine_id  TEXT NOT NULL,
            metric      TEXT NOT NULL,
            instance    TEXT NOT NULL,
            ts          INTEGER NOT NULL,
            value       REAL NOT NULL,
            PRIMARY KEY (machine_id, metric, instance)
        ) WITHOUT ROWID;

        CREATE TABLE thresholds (
            id          INTEGER PRIMARY KEY,
            scope       TEXT NOT NULL,            -- 'global', 'group' or 'machine'
            scope_value TEXT NOT NULL DEFAULT '', -- group name or machine id
            metric      TEXT NOT NULL,
            instance    TEXT NOT NULL DEFAULT '*',
            direction   TEXT NOT NULL,            -- 'Above' or 'Below'
            warning     REAL NULL,
            critical    REAL NULL,
            enabled     INTEGER NOT NULL DEFAULT 1,
            UNIQUE (scope, scope_value, metric, instance)
        );

        CREATE TABLE alert_state (
            machine_id      TEXT NOT NULL,
            metric          TEXT NOT NULL,
            instance        TEXT NOT NULL,
            active_level    INTEGER NOT NULL,
            change_count    INTEGER NOT NULL,     -- readings in a row that disagree with active_level
            last_notified   INTEGER NULL,
            alert_id        INTEGER NULL,
            last_ts         INTEGER NULL,         -- newest reading already counted (resends are skipped)
            PRIMARY KEY (machine_id, metric, instance)
        ) WITHOUT ROWID;

        CREATE TABLE alerts (
            id              INTEGER PRIMARY KEY,
            machine_id      TEXT NOT NULL,
            metric          TEXT NOT NULL,
            instance        TEXT NOT NULL,
            level           INTEGER NOT NULL,     -- current level while open
            peak_level      INTEGER NOT NULL,
            value           REAL NULL,
            threshold       REAL NULL,
            message         TEXT NOT NULL,
            opened          INTEGER NOT NULL,
            closed          INTEGER NULL,
            acknowledged    INTEGER NULL,
            acknowledged_by TEXT NULL
        );
        CREATE INDEX ix_alerts_machine ON alerts (machine_id, opened);
        CREATE INDEX ix_alerts_open ON alerts (closed);

        CREATE TABLE settings (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """,
    ];

    /// <summary>Creates or upgrades the tables. Called once at start-up.</summary>
    public void EnsureCreated()
    {
        using var c = Open();
        long version = Scalar<long>(c, "PRAGMA user_version");
        for (int v = (int)version; v < Migrations.Length; v++)
        {
            using var tx = c.BeginTransaction();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = Migrations[v] + $"\nPRAGMA user_version = {v + 1};";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public string? GetSetting(string key)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Quick "is the database answering" check, used by the health endpoint.</summary>
    public bool Ping()
    {
        using var c = Open();
        return Scalar<long>(c, "SELECT count(*) FROM machines") >= 0;
    }

    internal static T Scalar<T>(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
    }
}

/// <summary>Small helpers that keep the SQL code short.</summary>
internal static class SqliteExtensions
{
    public static SqliteCommand Command(this SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        return cmd;
    }

    public static SqliteCommand With(this SqliteCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public static string? GetStringOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public static long? GetInt64OrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    public static double? GetDoubleOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
}

/// <summary>Converts between DateTime and the Unix seconds stored in the database.</summary>
public static class UnixTime
{
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static long From(DateTime time) => new DateTimeOffset(time.Kind == DateTimeKind.Local
        ? time.ToUniversalTime()
        : DateTime.SpecifyKind(time, DateTimeKind.Utc)).ToUnixTimeSeconds();
    public static DateTime ToUtc(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
}
