using System;
using System.ServiceProcess;
using System.Threading;
using ValiITMonitor.Collection;
using ValiITMonitor.Configuration;
using ValiITMonitor.Processing;
using ValiITMonitor.Storage;

namespace ValiITMonitor.Service
{
    /// <summary>
    /// The classic .NET Framework Windows service: derive from ServiceBase, start your work in
    /// OnStart (which must return quickly), stop it in OnStop.
    ///
    /// Two System.Threading.Timer objects do the work:
    ///   - the sampling timer takes a reading every SampleIntervalSeconds
    ///   - the maintenance timer runs summaries/reports/cleanup every MaintenanceIntervalMinutes
    ///
    /// (The .NET 10 edition replaces all of this with two BackgroundService classes and the host.)
    /// </summary>
    public sealed class MonitorService : ServiceBase
    {
        public const string Name = "ValiITMonitor";

        private readonly MonitorSettings _settings;
        private readonly MetricCollector _collector;
        private readonly MetricStore _store;
        private readonly MaintenanceService _maintenance;

        private Timer _samplingTimer;
        private Timer _maintenanceTimer;
        private int _maintenanceRunning;   // 0/1 flag used with Interlocked so passes never overlap

        public MonitorService(MonitorSettings settings, MetricCollector collector, MetricStore store, MaintenanceService maintenance)
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
        }

        protected override void OnStop()
        {
            // Disposing a Timer stops future callbacks.
            if (_samplingTimer != null) _samplingTimer.Dispose();
            if (_maintenanceTimer != null) _maintenanceTimer.Dispose();
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
    }
}
