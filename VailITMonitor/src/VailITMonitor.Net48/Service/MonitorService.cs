using System;
using System.ServiceProcess;
using System.Threading;
using VailITMonitor.Collection;
using VailITMonitor.Configuration;
using VailITMonitor.Dashboard;
using VailITMonitor.Processing;
using VailITMonitor.Storage;

namespace VailITMonitor.Service
{
    /// <summary>
    /// The classic .NET Framework Windows service: derive from ServiceBase, start your work in
    /// OnStart (which must return quickly), stop it in OnStop.
    ///
    /// System.Threading.Timer objects do the work:
    ///   - the sampling timer takes a reading every SampleIntervalSeconds
    ///   - the maintenance timer runs summaries/reports/cleanup every MaintenanceIntervalMinutes
    ///   - the reporting timer (only when Reporting.Enabled) sends readings to VailITDashboard.
    ///     It is a one-shot timer re-armed after each cycle, because the wait varies (it backs off
    ///     during an outage), and that also means two cycles can never overlap.
    ///
    /// (The .NET 10 edition replaces all of this with two BackgroundService classes and the host.)
    /// </summary>
    public sealed class MonitorService : ServiceBase
    {
        public const string Name = "VailITMonitor";

        private readonly MonitorSettings _settings;
        private readonly MetricCollector _collector;
        private readonly MetricStore _store;
        private readonly MaintenanceService _maintenance;
        private readonly DashboardReporter _reporter;   // null when reporting is off
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

        private Timer _samplingTimer;
        private Timer _maintenanceTimer;
        private Timer _reportingTimer;
        private bool _introduced;
        private int _maintenanceRunning;   // 0/1 flag used with Interlocked so passes never overlap

        public MonitorService(MonitorSettings settings, MetricCollector collector, MetricStore store, MaintenanceService maintenance,
            DashboardReporter reporter)
        {
            ServiceName = Name;
            CanStop = true;
            CanShutdown = true;
            CanPauseAndContinue = false;
            AutoLog = true;   // writes "service started/stopped" to the Application event log

            _settings = settings;
            _collector = collector;
            _store = store;
            _maintenance = maintenance;
            _reporter = reporter;
        }

        /// <summary>Lets Program.cs run the same code in a console window for testing.</summary>
        public void StartInConsole()
        {
            OnStart(new string[0]);
        }

        public void StopInConsole()
        {
            OnStop();
        }

        protected override void OnStart(string[] args)
        {
            Log.Info("Sampling every " + _settings.SampleInterval.TotalSeconds + "s into " + _store.DatabasePath);
            _collector.Prime();

            // First tick lines up with the clock (e.g. on the minute), then repeats every interval.
            var interval = _settings.SampleInterval;
            long ticks = interval.Ticks;
            var firstDelay = TimeSpan.FromTicks(ticks - (DateTime.Now.Ticks % ticks));
            _samplingTimer = new Timer(OnSamplingTick, null, firstDelay, interval);

            var maintenanceEvery = TimeSpan.FromMinutes(Math.Max(1, _settings.MaintenanceIntervalMinutes));
            _maintenanceTimer = new Timer(OnMaintenanceTick, null, TimeSpan.FromMinutes(1), maintenanceEvery);

            if (_reporter != null)
                _reportingTimer = new Timer(OnReportingTick, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }

        protected override void OnStop()
        {
            // Disposing a Timer stops future callbacks.
            if (_samplingTimer != null) _samplingTimer.Dispose();
            if (_maintenanceTimer != null) _maintenanceTimer.Dispose();
            _stopping.Cancel();
            if (_reportingTimer != null) _reportingTimer.Dispose();
            _collector.Dispose();
            Log.Info("Stopped.");
        }

        protected override void OnShutdown()
        {
            OnStop();
        }

        private void OnSamplingTick(object state)
        {
            try
            {
                var readings = _collector.Collect();
                _store.WriteSamples(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), readings);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to take or save a reading", ex);
            }
        }

        private void OnMaintenanceTick(object state)
        {
            // A Timer callback can fire again while the previous one is still running; skip if busy.
            if (Interlocked.Exchange(ref _maintenanceRunning, 1) == 1) return;
            try
            {
                _maintenance.RunOnce(DateTime.Now);
            }
            catch (Exception ex)
            {
                Log.Error("Maintenance pass failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _maintenanceRunning, 0);
            }
        }
        private void OnReportingTick(object state)
        {
            TimeSpan next = TimeSpan.FromMinutes(1);
            try
            {
                if (!_introduced)
                {
                    _introduced = true;
                    _reporter.Introduce();
                }
                next = _reporter.RunCycle(_stopping.Token);
            }
            catch (Exception ex)
            {
                // A bug or a database hiccup: log it and carry on; sampling is unaffected.
                Log.Error("Reporting cycle failed", ex);
            }

            if (_stopping.IsCancellationRequested) return;
            try
            {
                _reportingTimer.Change(next < TimeSpan.Zero ? TimeSpan.Zero : next, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Stopped while this cycle was running.
            }
        }
    }
}
