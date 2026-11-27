using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace VailITDashboard.Data;

/// <summary>
/// Thresholds at three levels: Global (everyone), Group (e.g. every "SQL" server) and Machine.
/// The most specific one wins; see <see cref="Alerts.ThresholdResolver"/>.
/// </summary>
public sealed class ThresholdStore(Database db, IOptions<DashboardOptions> options, ILogger<ThresholdStore> log)
{
    private const string SeededKey = "thresholds.seeded";

    // The full list is small and read on every report, so keep a copy in memory and refresh it on change.
    private List<Threshold>? _cache;
    private readonly Lock _lock = new();

    public IReadOnlyList<Threshold> GetAll()
    {
        lock (_lock)
        {
            return _cache ??= Load();
        }
    }

    public Threshold? Get(long id) => GetAll().FirstOrDefault(t => t.Id == id);

    public IEnumerable<Threshold> ForScope(ThresholdScope scope, string scopeValue) =>
        GetAll().Where(t => t.Scope == scope && string.Equals(t.ScopeValue, scopeValue, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => MetricCatalog.SortOrder(t.Metric)).ThenBy(t => t.Instance);

    /// <summary>Adds or replaces the threshold for (scope, metric, instance).</summary>
    public void Save(Threshold t)
    {
        using (var c = db.Open())
        using (var cmd = c.Command("""
            INSERT INTO thresholds (scope, scope_value, metric, instance, direction, warning, critical, enabled)
            VALUES ($s, $sv, $m, $i, $d, $w, $c, $e)
            ON CONFLICT(scope, scope_value, metric, instance) DO UPDATE SET
                direction = excluded.direction, warning = excluded.warning, critical = excluded.critical, enabled = excluded.enabled
            """))
        {
            cmd.With("$s", ScopeName(t.Scope)).With("$sv", t.Scope == ThresholdScope.Global ? "" : t.ScopeValue)
               .With("$m", t.Metric.Trim()).With("$i", (t.Instance ?? "*").Trim()).With("$d", t.Direction.ToString())
               .With("$w", t.Warning).With("$c", t.Critical).With("$e", t.Enabled ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        Invalidate();
    }

    public void Delete(long id)
    {
        using (var c = db.Open())
        using (var cmd = c.Command("DELETE FROM thresholds WHERE id = $id").With("$id", id))
            cmd.ExecuteNonQuery();
        Invalidate();
    }

    /// <summary>The first time the dashboard runs, copy DefaultThresholds from appsettings.json in as Global thresholds.
    /// After that the database is the source of truth and you edit thresholds on the Thresholds page.</summary>
    public void SeedDefaultsOnce()
    {
        if (db.GetSetting(SeededKey) is not null) return;
        foreach (var d in options.Value.DefaultThresholds)
        {
            Save(new Threshold
            {
                Scope = ThresholdScope.Global, Metric = d.Metric, Instance = d.Instance, Direction = d.Direction,
                Warning = d.Warning, Critical = d.Critical, Enabled = true,
            });
        }
        db.SetSetting(SeededKey, DateTime.UtcNow.ToString("O"));
        log.LogInformation("Added {Count} default thresholds", options.Value.DefaultThresholds.Count);
    }

    private void Invalidate()
    {
        lock (_lock) _cache = null;
    }

    private List<Threshold> Load()
    {
        using var c = db.Open();
        using var cmd = c.Command("SELECT id, scope, scope_value, metric, instance, direction, warning, critical, enabled FROM thresholds");
        using var r = cmd.ExecuteReader();
        var list = new List<Threshold>();
        while (r.Read())
        {
            list.Add(new Threshold
            {
                Id = r.GetInt64(0), Scope = ParseScope(r.GetString(1)), ScopeValue = r.GetString(2), Metric = r.GetString(3),
                Instance = r.GetString(4), Direction = Enum.Parse<ThresholdDirection>(r.GetString(5)),
                Warning = r.GetDoubleOrNull(6), Critical = r.GetDoubleOrNull(7), Enabled = r.GetInt64(8) != 0,
            });
        }
        return list;
    }

    private static string ScopeName(ThresholdScope s) => s.ToString().ToLowerInvariant();
    private static ThresholdScope ParseScope(string s) => Enum.Parse<ThresholdScope>(s, ignoreCase: true);
}
