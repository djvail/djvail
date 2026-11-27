using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using VailITDashboard.Alerts;

namespace VailITDashboard.Api;

/// <summary>
/// The HTTP API the monitors (and the external heartbeat checker) call. Everything is under /api/v1 so a
/// later breaking change can live at /api/v2 while old monitors keep working.
/// </summary>
public static class ReportingEndpoints
{
    public const int MaxBodyBytes = 1024 * 1024;
    public const string RateLimitPolicy = "per-key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapReportingApi(this WebApplication app)
    {
        // The monitors authenticate with API keys, the heartbeat needs none, so the pages' sign-in doesn't apply here.
        var api = app.MapGroup("/api/v1").AllowAnonymous();

        api.MapPost("/reports", PostReport).RequireRateLimiting(RateLimitPolicy).DisableAntiforgery();
        api.MapGet("/monitors/me", GetMe).RequireRateLimiting(RateLimitPolicy);
        api.MapGet("/health", GetHealth);
        // Same data as the overview page, so it needs the same sign-in as the pages.
        app.MapGet("/api/v1/machines", GetMachines).RequireAuthorization();

        // Same as /api/v1/health, for tools that expect the conventional path.
        app.MapGet("/health", GetHealth).AllowAnonymous();
    }

    // POST /api/v1/reports -------------------------------------------------------------------

    private static async Task<IResult> PostReport(
        HttpContext http, ApiKeyStore keys, MachineStore machines, AlertEngine engine,
        IOptions<DashboardOptions> options, ILoggerFactory logs)
    {
        var log = logs.CreateLogger("VailITDashboard.Api.Reports");

        var key = keys.Check(http.Request.Headers[ReportingApi.ApiKeyHeader]);
        if (!key.Valid) return Results.Problem("Missing, unknown or revoked API key.", statusCode: 401);

        // Cap the body (after gzip is undone) so one bad client can't fill the server's memory.
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = MaxBodyBytes;
        if (http.Request.ContentLength > MaxBodyBytes) return Results.Problem("Report too large; send fewer samples.", statusCode: 413);

        MonitorReport? report;
        try
        {
            report = await JsonSerializer.DeserializeAsync<MonitorReport>(http.Request.Body, Json, http.RequestAborted);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.Problem("Report too large; send fewer samples.", statusCode: 413);
        }
        catch (JsonException ex)
        {
            return Results.Problem($"Body is not a valid report: {ex.Message}", statusCode: 400);
        }

        var error = Validate(report);
        if (error is not null) return Results.Problem(error, statusCode: 400);
        var monitor = report!.Monitor;
        string machineId = monitor.MachineId.ToString("D");

        if (key.BoundMachineId is not null && !string.Equals(key.BoundMachineId, machineId, StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning("Key {KeyId} belongs to {Bound} but was used by {MachineId} ({Name}); rejected",
                key.KeyId, key.BoundMachineId, machineId, monitor.MachineName);
            return Results.Problem("This API key belongs to a different machine.", statusCode: 401);
        }

        var (accepted, isNew) = machines.SaveReport(monitor, report.Samples);
        keys.MarkUsed(key.KeyId, machineId);
        if (isNew) log.LogInformation("New machine {Name} ({MachineId})", monitor.MachineName, machineId);

        var machine = machines.Get(machineId)!;
        try
        {
            engine.EvaluateReport(machine, report.Samples);
        }
        catch (Exception ex)
        {
            // The readings are saved; a bug in the alert code shouldn't make the monitor resend them.
            log.LogError(ex, "Checking thresholds failed for {Name}", machine.Name);
        }

        return Results.Ok(new ReportAck
        {
            AcceptedThroughUtc = UnixTime.ToUtc(accepted),
            ReportIntervalSeconds = (int)options.Value.ReportInterval.TotalSeconds,
            ServerTimeUtc = DateTime.UtcNow,
        });
    }

    /// <summary>Returns a reason the report is unusable, or null if it is fine.</summary>
    internal static string? Validate(MonitorReport? r)
    {
        if (r is null) return "Body is empty.";
        if (r.SchemaVersion != ReportingApi.SchemaVersion) return $"Unsupported schemaVersion {r.SchemaVersion}; this dashboard understands {ReportingApi.SchemaVersion}.";
        if (r.Monitor is null) return "monitor is required.";
        if (r.Monitor.MachineId == Guid.Empty) return "monitor.machineId is required.";
        if (r.Samples is null || r.Samples.Count == 0) return "samples must contain at least one sample.";
        if (r.Samples.Count > ReportingApi.MaxSamplesPerReport) return $"At most {ReportingApi.MaxSamplesPerReport} samples per report.";

        var latestAllowed = DateTime.UtcNow.AddHours(1);
        foreach (var s in r.Samples)
        {
            if (s.TimestampUtc == default) return "Every sample needs a timestampUtc.";
            if (s.TimestampUtc.ToUniversalTime() > latestAllowed) return $"Sample timestamp {s.TimestampUtc:O} is in the future; check the server's clock.";
            if (s.Readings is null) return "Every sample needs a readings list.";
            foreach (var reading in s.Readings)
            {
                if (string.IsNullOrWhiteSpace(reading.Metric)) return "Every reading needs a metric name.";
                if (double.IsNaN(reading.Value) || double.IsInfinity(reading.Value)) return $"Reading {reading.Metric} has no numeric value.";
            }
        }
        return null;
    }

    // GET /api/v1/monitors/me ----------------------------------------------------------------

    private static IResult GetMe(HttpContext http, ApiKeyStore keys, MachineStore machines, IOptions<DashboardOptions> options)
    {
        var key = keys.Check(http.Request.Headers[ReportingApi.ApiKeyHeader]);
        if (!key.Valid) return Results.Problem("Missing, unknown or revoked API key.", statusCode: 401);

        var m = key.BoundMachineId is null ? null : machines.Get(key.BoundMachineId);
        return Results.Ok(new MonitorInfo
        {
            MachineId = m is null ? Guid.Empty : Guid.Parse(m.MachineId),
            DisplayName = m?.Name ?? "",
            Group = m?.GroupName ?? "",
            ReportIntervalSeconds = (int)options.Value.ReportInterval.TotalSeconds,
        });
    }

    // GET /api/v1/health ---------------------------------------------------------------------

    /// <summary>For the external heartbeat checker: 200 when the site and its database answer, 503 otherwise.</summary>
    private static IResult GetHealth(HttpContext http, Database db, IOptions<DashboardOptions> options, ILoggerFactory logs)
    {
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            using var c = db.Open();
            using var cmd = c.Command("SELECT count(*) FROM machines WHERE last_seen >= $since")
                .With("$since", UnixTime.Now - (long)options.Value.OfflineAfter.TotalSeconds);
            long online = (long)cmd.ExecuteScalar()!;
            return Results.Ok(new { status = "ok", db = "ok", monitorsOnline = online, timeUtc = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            logs.CreateLogger("VailITDashboard.Api.Health").LogError(ex, "Health check failed");
            return Results.Json(new { status = "unhealthy", db = "error", timeUtc = DateTime.UtcNow }, statusCode: 503);
        }
    }

    // GET /api/v1/machines -------------------------------------------------------------------

    /// <summary>Every machine's current status as JSON (for scripts, or a wall display of your own).</summary>
    private static IResult GetMachines(StatusService status) =>
        Results.Ok(status.GetAll().Select(s => new
        {
            machineId = s.Machine.MachineId,
            name = s.Machine.Name,
            group = s.Machine.GroupName,
            health = s.Health.ToString(),
            muted = s.Muted,
            openAlerts = s.OpenAlerts,
            topProblem = s.TopProblem,
            cpuPct = s.Cpu,
            memoryUsedPct = s.Memory,
            lastSeenUtc = UnixTime.ToUtc(s.Machine.LastSeen),
        }));
}
