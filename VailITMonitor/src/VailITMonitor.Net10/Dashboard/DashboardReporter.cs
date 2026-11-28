using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VailIT.Reporting.Contracts;

namespace VailITMonitor.Dashboard;

/// <summary>
/// Sends readings to VailITDashboard. Readings are already saved in the local database, so the
/// samples table is the outbox: each cycle sends everything newer than the last time the
/// dashboard acknowledged, and an outage only delays readings, never loses them.
///
/// One call to <see cref="RunCycleAsync"/> = one cycle; it returns how long to wait before the next.
/// </summary>
public sealed class DashboardReporter(
    MetricStore store,
    DashboardClient client,
    MachineInfo machine,
    IOptions<MonitorOptions> options,
    ILogger<DashboardReporter> log)
{
    /// <summary>task_state row holding the Unix time of the newest sample the dashboard has accepted.</summary>
    public const string WatermarkState = "report.last_ack_ts";

    // The dashboard allows 10 requests a minute per key, so catch-up batches are spaced 7 s apart.
    private static readonly TimeSpan CatchUpPause = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan UnauthorizedRetry = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ClockSkewWarning = TimeSpan.FromMinutes(2);

    private readonly MonitorOptions _options = options.Value;
    private TimeSpan _interval = TimeSpan.FromSeconds(Math.Max(options.Value.Reporting.IntervalSeconds, (int)options.Value.SampleInterval.TotalSeconds));
    private int _batchSize = ReportingApi.MaxSamplesPerReport;
    private int _failures;
    private DateTime _lastFailureLogged = DateTime.MinValue;

    public TimeSpan Interval => _interval;

    /// <summary>Checks the key at start-up and logs how the dashboard knows this server.</summary>
    public async Task IntroduceAsync(CancellationToken ct)
    {
        if (!client.IsHttps)
            log.LogWarning("Reporting to {Url} over plain HTTP: the API key is sent unencrypted. Use https:// outside a lab.", client.BaseUri);

        var (result, info) = await client.GetMeAsync(ct);
        if (result.Outcome == SendOutcome.Ok && info is not null)
        {
            if (string.IsNullOrEmpty(info.DisplayName))
                log.LogInformation("Reporting to {Url} as a new machine (machine id {MachineId})", client.BaseUri, machine.MachineId);
            else
                log.LogInformation("Reporting to {Url} as \"{Name}\" (group \"{Group}\", machine id {MachineId})",
                    client.BaseUri, info.DisplayName, info.Group, machine.MachineId);
            ApplyInterval(info.ReportIntervalSeconds);
        }
        else if (result.Outcome == SendOutcome.Unauthorized)
        {
            log.LogWarning("The dashboard at {Url} rejected this server's API key ({Message}). Readings are kept locally; " +
                "add or re-issue the key in the dashboard, then run \"VailITMonitor.exe connect\".", client.BaseUri, result.Message);
        }
        else
        {
            log.LogInformation("Reporting to {Url} (machine id {MachineId}); the dashboard did not answer yet: {Message}",
                client.BaseUri, machine.MachineId, Explain(result));
        }
    }

    public async Task<TimeSpan> RunCycleAsync(CancellationToken ct)
    {
        long watermark = ReadWatermark();

        while (true)
        {
            var batches = store.GetSampleBatchesAfter(watermark, _batchSize);
            if (batches.Count == 0) return NextRegularDelay();

            var report = new MonitorReport
            {
                SentAtUtc = DateTime.UtcNow,
                Monitor = machine.Build(),
                Samples = batches.Select(b => new SampleDto
                {
                    TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(b.Ts).UtcDateTime,
                    Readings = b.Readings.Select(r => new ReadingDto { Metric = r.Metric, Instance = r.Instance, Value = r.Value }).ToList(),
                }).ToList(),
            };
            long lastInBatch = batches[^1].Ts;

            var (result, ack) = await client.SendReportAsync(report, ct);
            switch (result.Outcome)
            {
                case SendOutcome.Ok:
                    RecordRecovery();
                    long accepted = new DateTimeOffset(ack!.AcceptedThroughUtc.ToUniversalTime()).ToUnixTimeSeconds();
                    // Never go backwards, and never get stuck if the dashboard reports no progress.
                    watermark = accepted > watermark && accepted <= lastInBatch ? accepted : lastInBatch;
                    SaveWatermark(watermark);
                    ApplyInterval(ack.ReportIntervalSeconds);
                    CheckClock(ack.ServerTimeUtc);
                    if (batches.Count < _batchSize) return NextRegularDelay();
                    log.LogDebug("Catching up: sent up to {Time:u}", DateTimeOffset.FromUnixTimeSeconds(watermark));
                    await Task.Delay(CatchUpPause, ct);
                    break;

                case SendOutcome.BadRequest:
                    // One bad batch must not block everything behind it.
                    log.LogWarning("The dashboard refused {Count} sample(s) up to {Time:u} and they were skipped: {Message}",
                        batches.Count, DateTimeOffset.FromUnixTimeSeconds(lastInBatch), result.Message);
                    watermark = lastInBatch;
                    SaveWatermark(watermark);
                    break;

                case SendOutcome.TooLarge when _batchSize > 1:
                    _batchSize = Math.Max(1, _batchSize / 2);
                    log.LogInformation("The dashboard said the report was too large; now sending {Count} sample(s) per report", _batchSize);
                    break;

                case SendOutcome.TooLarge:
                    log.LogWarning("A single sample at {Time:u} is too large for the dashboard and was skipped", DateTimeOffset.FromUnixTimeSeconds(lastInBatch));
                    watermark = lastInBatch;
                    SaveWatermark(watermark);
                    break;

                case SendOutcome.Unauthorized:
                    RecordFailure(result, LogLevel.Warning);
                    return UnauthorizedRetry;

                case SendOutcome.Busy:
                    log.LogDebug("Dashboard busy; waiting {Seconds}s", result.RetryAfter!.Value.TotalSeconds);
                    return Clamp(result.RetryAfter!.Value, TimeSpan.FromSeconds(5), MaxBackoff);

                default:
                    RecordFailure(result, LogLevel.Warning);
                    return Backoff(_failures);
            }
        }
    }

    // ---------------------------------------------------------------- watermark

    private long ReadWatermark()
    {
        if (long.TryParse(store.GetState(WatermarkState), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts)) return ts;

        // First run: send only recent history, not the whole local database.
        long start = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(0, _options.Reporting.InitialBacklogMinutes)).ToUnixTimeSeconds();
        SaveWatermark(start);
        return start;
    }

    private void SaveWatermark(long ts) => store.SetState(WatermarkState, ts.ToString(CultureInfo.InvariantCulture));

    // ---------------------------------------------------------------- timing

    private void ApplyInterval(int seconds)
    {
        if (seconds <= 0) return;
        var wanted = TimeSpan.FromSeconds(Math.Max(seconds, _options.SampleInterval.TotalSeconds));
        if (wanted == _interval) return;
        log.LogInformation("The dashboard asked for a report every {Seconds}s", wanted.TotalSeconds);
        _interval = wanted;
    }

    /// <summary>A few seconds after the next interval boundary, so the newest reading is already saved.</summary>
    private TimeSpan NextRegularDelay()
    {
        long ticks = _interval.Ticks;
        var untilBoundary = TimeSpan.FromTicks(ticks - DateTime.Now.Ticks % ticks);
        return untilBoundary + TimeSpan.FromSeconds(5);
    }

    /// <summary>1, 2, 4, 8 then 15 minutes, +/- 20% so many servers don't all retry at once.</summary>
    private static TimeSpan Backoff(int failures)
    {
        double minutes = Math.Min(MaxBackoff.TotalMinutes, Math.Pow(2, Math.Max(0, failures - 1)));
        return TimeSpan.FromMinutes(minutes * (0.8 + Random.Shared.NextDouble() * 0.4));
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => value < min ? min : value > max ? max : value;

    private void CheckClock(DateTime serverTimeUtc)
    {
        var skew = DateTime.UtcNow - serverTimeUtc.ToUniversalTime();
        if (skew.Duration() > ClockSkewWarning)
            log.LogWarning("This server's clock is {Seconds:N0}s {Direction} the dashboard's; it may show as late or stale. Check Windows time sync.",
                skew.Duration().TotalSeconds, skew > TimeSpan.Zero ? "ahead of" : "behind");
    }

    // ---------------------------------------------------------------- logging without flooding

    private void RecordFailure(SendResult result, LogLevel level)
    {
        _failures++;
        // Log the first failure, then at most once an hour while the outage lasts.
        if (_failures == 1 || DateTime.UtcNow - _lastFailureLogged > TimeSpan.FromHours(1))
        {
            log.Log(level, "Could not report to the dashboard ({Message}). Readings are kept and will be sent later.", Explain(result));
            _lastFailureLogged = DateTime.UtcNow;
        }
    }

    private void RecordRecovery()
    {
        if (_failures > 0)
            log.LogInformation("Reporting to the dashboard again after {Count} failed attempt(s)", _failures);
        _failures = 0;
    }

    private static string Explain(SendResult r) =>
        r.StatusCode == 0 ? r.Message : $"HTTP {r.StatusCode}{(r.Message.Length > 0 ? ": " + r.Message : "")}";
}
