using System;
using System.Diagnostics;

namespace ValiITMonitor
{
    /// <summary>
    /// Minimal logging: the Windows Event Log (Application log, source "ValiITMonitor")
    /// plus the console when running interactively.
    ///
    /// .NET Framework has no built-in logging abstraction like .NET 10's ILogger&lt;T&gt;,
    /// so this small static class plays that role.
    /// </summary>
    public static class Log
    {
        public const string Source = "ValiITMonitor";
        public static bool Verbose = true;
        private static bool _eventLogBroken;

        public static void Info(string message)
        {
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " info: " + message);
            if (Verbose) WriteEvent(message, EventLogEntryType.Information);
        }

        public static void Warn(string message)
        {
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " warn: " + message);
            WriteEvent(message, EventLogEntryType.Warning);
        }

        public static void Error(string message, Exception ex)
        {
            string text = ex == null ? message : message + Environment.NewLine + ex;
            Console.Error.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " fail: " + text);
            WriteEvent(text, EventLogEntryType.Error);
        }

        private static void WriteEvent(string message, EventLogEntryType type)
        {
            if (_eventLogBroken || Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            try
            {
                // The source is registered by the install script (creating it needs admin rights).
                if (message.Length > 30000) message = message.Substring(0, 30000);
                EventLog.WriteEntry(Source, message, type);
            }
            catch (Exception)
            {
                _eventLogBroken = true;   // e.g. source not registered and not running as admin
            }
        }
    }
}
