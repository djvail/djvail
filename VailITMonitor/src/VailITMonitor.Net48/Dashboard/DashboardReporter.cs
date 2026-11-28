using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using VailIT.Reporting.Contracts;
using VailITMonitor.Configuration;
using VailITMonitor.Storage;

namespace VailITMonitor.Dashboard
{
    /// <summary>
    /// Sends readings to VailITDashboard. Readings are already saved in the local database, so the
    /// samples table is the outbox: each cycle sends everything newer than the last time the
    /// dashboard acknowledged, and an outage only delays readings, never loses them.
    ///
    /// One call to RunCycle() = one cycle; it returns how long to wait before the next.
    /// (Same logic as the .NET 10 edition's DashboardReporter, written without async.)
    /// </summary>
    public sealed class DashboardReporter
    {
        /// <summary>task_state row holding the Unix time of the newest sample the dashboard has accepted.</summary>
        public const string WatermarkState = "report.last_ack_ts";

        // The dashboard allows 10 requests a minute per key, so catch-up batches are spaced 7 s apart.
        private static readonly TimeSpan CatchUpPause = TimeSpan.FromSeconds(7);
        private static readonly TimeSpan UnauthorizedRetry = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan ClockSkewWarning = TimeSpan.FromMinutes(2);
        private static readonly Random Jitter = new Random();

        private readonly MetricStore _store;
        private readonly DashboardClient _client;
        private readonly MachineInfo _machine;
        private readonly MonitorSettings _settings;

        private TimeSpan _interval;
        private int _batchSize = ReportingApi.MaxSamplesPerReport;
        private int _failures;
        private DateTime _lastFailureLogged = DateTime.MinValue;

        public DashboardReporter(MetricStore store, DashboardClient client, MachineInfo machine, MonitorSettings settings)
        {
            _store = store;
            _client = client;
            _machine = machine;
            _settings = settings;
            _interval = TimeSpan.FromSeconds(Math.Max(settings.Reporting.IntervalSeconds, settings.SampleInterval.TotalSeconds));
        }

        /// <summary>Checks the key at start-up and logs how the dashboard knows this server.</summary>
        public void Introduce()
        {
            if (!_client.IsHttps)
                Log.Warn("Reporting to " + _client.BaseUri + " over plain HTTP: the API key is sent unencrypted. Use https:// outside a lab.");

            var me = _client.GetMe();
            if (me.Outcome == SendOutcome.Ok && string.IsNullOrEmpty(me.Value.DisplayName))
            {
                Log.Info("Reporting to " + _client.BaseUri + " as a new machine (machine id " + _machine.MachineId + ")");
                ApplyInterval(me.Value.ReportIntervalSeconds);
            }
            else if (me.Outcome == SendOutcome.Ok)
            {
                Log.Info("Reporting to " + _client.BaseUri + " as \"" + me.Value.DisplayName + "\" (group \"" + me.Value.Group +
                         "\", machine id " + _machine.MachineId + ")");
                ApplyInterval(me.Value.ReportIntervalSeconds);
            }
            else if (me.Outcome == SendOutcome.Unauthorized)
            {
                Log.Warn("The dashboard at " + _client.BaseUri + " rejected this server's API key (" + me.Message + "). Readings are kept locally; " +
                         "add or re-issue the key in the dashboard, then run \"VailITMonitor.exe connect\".");
            }
            else
            {
                Log.Info("Reporting to " + _client.BaseUri + " (machine id " + _machine.MachineId + "); the dashboard did not answer yet: " + me.Explain());
            }
        }

        public TimeSpan RunCycle(CancellationToken stopping)
        {
            long watermark = ReadWatermark();

            while (!stopping.IsCancellationRequested)
            {
                var batches = _store.GetSampleBatchesAfter(watermark, _batchSize);
                if (batches.Count == 0) return NextRegularDelay();

                var report = new MonitorReport
                {
                    SentAtUtc = DateTime.UtcNow,
                    Monitor = _machine.Build(),
                    Samples = batches.Select(b => new SampleDto
                    {
                        TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(b.Ts).UtcDateTime,
                        Readings = b.Readings.Select(r => new ReadingDto { Metric = r.Metric, Instance = r.Instance, Value = r.Value }).ToList(),
                    }).ToList(),
                };
                long lastInBatch = batches[batches.Count - 1].Ts;

                var result = _client.SendReport(report);
                switch (result.Outcome)
                {
                    case SendOutcome.Ok:
                        RecordRecovery();
                        long accepted = new DateTimeOffset(result.Value.AcceptedThroughUtc.ToUniversalTime()).ToUnixTimeSeconds();
                        // Never go backwards, and never get stuck if the dashboard reports no progress.
                        watermark = accepted > watermark && accepted <= lastInBatch ? accepted : lastInBatch;
                        SaveWatermark(watermark);
                        ApplyInterval(result.Value.ReportIntervalSeconds);
                        CheckClock(result.Value.ServerTimeUtc);
                        if (batches.Count < _batchSize) return NextRegularDelay();
                        if (stopping.WaitHandle.WaitOne(CatchUpPause)) return TimeSpan.Zero;   // stopping
                        break;

                    case SendOutcome.BadRequest:
                        // One bad batch must not block everything behind it.
                        Log.Warn("The dashboard refused " + batches.Count + " sample(s) up to " + Time(lastInBatch) + " and they were skipped: " + result.Message);
                        watermark = lastInBatch;
                        SaveWatermark(watermark);
                        break;

                    case SendOutcome.TooLarge:
                        if (_batchSize > 1)
                        {
                            _batchSize = Math.Max(1, _batchSize / 2);
                            Log.Info("The dashboard said the report was too large; now sending " + _batchSize + " sample(s) per report");
                        }
                        else
                        {
                            Log.Warn("A single sample at " + Time(lastInBatch) + " is too large for the dashboard and was skipped");
                            watermark = lastInBatch;
                            SaveWatermark(watermark);
                        }
                        break;

                    case SendOutcome.Unauthorized:
                        RecordFailure(result.Explain());
                        return UnauthorizedRetry;

                    case SendOutcome.Busy:
                        return Clamp(result.RetryAfter.Value, TimeSpan.FromSeconds(5), MaxBackoff);

                    default:
                        RecordFailure(result.Explain());
                        return Backoff(_failures);
                }
            }

            return TimeSpan.Zero;
        }

        // ---------------------------------------------------------------- watermark

        private long ReadWatermark()
        {
            long ts;
            if (long.TryParse(_store.GetState(WatermarkState), NumberStyles.Integer, CultureInfo.InvariantCulture, out ts)) return ts;

            // First run: send only recent history, not the whole local database.
            long start = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(0, _settings.Reporting.InitialBacklogMinutes)).ToUnixTimeSeconds();
            SaveWatermark(start);
            return start;
        }

        private void SaveWatermark(long ts)
        {
            _store.SetState(WatermarkState, ts.ToString(CultureInfo.InvariantCulture));
        }

        // ---------------------------------------------------------------- timing

        private void ApplyInterval(int seconds)
        {
            if (seconds <= 0) return;
            var wanted = TimeSpan.FromSeconds(Math.Max(seconds, _settings.SampleInterval.TotalSeconds));
            if (wanted == _interval) return;
            Log.Info("The dashboard asked for a report every " + wanted.TotalSeconds + "s");
            _interval = wanted;
        }

        /// <summary>A few seconds after the next interval boundary, so the newest reading is already saved.</summary>
        private TimeSpan NextRegularDelay()
        {
            long ticks = _interval.Ticks;
            return TimeSpan.FromTicks(ticks - DateTime.Now.Ticks % ticks) + TimeSpan.FromSeconds(5);
        }

        /// <summary>1, 2, 4, 8 then 15 minutes, +/- 20% so many servers don't all retry at once.</summary>
        private static TimeSpan Backoff(int failures)
        {
            double minutes = Math.Min(MaxBackoff.TotalMinutes, Math.Pow(2, Math.Max(0, failures - 1)));
            double factor;
            lock (Jitter) factor = 0.8 + Jitter.NextDouble() * 0.4;
            return TimeSpan.FromMinutes(minutes * factor);
        }

        private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static string Time(long unixTime)
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixTime).ToString("u", CultureInfo.InvariantCulture);
        }

        private void CheckClock(DateTime serverTimeUtc)
        {
            var skew = DateTime.UtcNow - serverTimeUtc.ToUniversalTime();
            if (skew.Duration() > ClockSkewWarning)
                Log.Warn("This server's clock is " + skew.Duration().TotalSeconds.ToString("N0") + "s " + (skew > TimeSpan.Zero ? "ahead of" : "behind") +
                         " the dashboard's; it may show as late or stale. Check Windows time sync.");
        }

        // ---------------------------------------------------------------- logging without flooding

        private void RecordFailure(string message)
        {
            _failures++;
            // Log the first failure, then at most once an hour while the outage lasts.
            if (_failures == 1 || DateTime.UtcNow - _lastFailureLogged > TimeSpan.FromHours(1))
            {
                Log.Warn("Could not report to the dashboard (" + message + "). Readings are kept and will be sent later.");
                _lastFailureLogged = DateTime.UtcNow;
            }
        }

        private void RecordRecovery()
        {
            if (_failures > 0) Log.Info("Reporting to the dashboard again after " + _failures + " failed attempt(s)");
            _failures = 0;
        }
    }
}
