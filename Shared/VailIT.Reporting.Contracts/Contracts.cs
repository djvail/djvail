// The reporting contract between VailITMonitor and VailITDashboard (schema version 1).
// JSON is camelCase (System.Text.Json with JsonSerializerDefaults.Web) and times are UTC.
// Design notes: see the "VailIT Monitor Reporting API Design" doc.
using System;
using System.Collections.Generic;

namespace VailIT.Reporting.Contracts
{
    public static class ReportingApi
    {
        public const int SchemaVersion = 1;
        public const string ReportsPath = "/api/v1/reports";
        public const string MePath = "/api/v1/monitors/me";
        public const string HealthPath = "/api/v1/health";
        public const string ApiKeyHeader = "X-Api-Key";
        public const int MaxSamplesPerReport = 60;
    }

    /// <summary>POST /api/v1/reports body: who the machine is, plus one or more samples.</summary>
    public sealed class MonitorReport
    {
        public int SchemaVersion { get; set; } = ReportingApi.SchemaVersion;
        public DateTime SentAtUtc { get; set; }
        public MonitorIdentity Monitor { get; set; } = new MonitorIdentity();
        public List<SampleDto> Samples { get; set; } = new List<SampleDto>();
    }

    public sealed class MonitorIdentity
    {
        /// <summary>Written once by the installer. The dashboard's primary key for a machine.</summary>
        public Guid MachineId { get; set; }
        public string MachineName { get; set; } = "";
        /// <summary>Hint only: used to pick a new machine's first group on the dashboard.</summary>
        public string ServerType { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();
        public string AgentVersion { get; set; } = "";
        public string Edition { get; set; } = "";        // "Net10" or "Net48"
        public string Os { get; set; } = "";
        public int LogicalProcessors { get; set; }
        public long TotalMemoryMb { get; set; }
        public List<DriveDto> Drives { get; set; } = new List<DriveDto>();
        public DateTime LastBootUtc { get; set; }
    }

    public sealed class DriveDto
    {
        public string Name { get; set; } = "";
        public double SizeGb { get; set; }
    }

    public sealed class SampleDto
    {
        public DateTime TimestampUtc { get; set; }
        public List<ReadingDto> Readings { get; set; } = new List<ReadingDto>();
    }

    public sealed class ReadingDto
    {
        /// <summary>A VailITMonitor metric name, e.g. "cpu.total_pct" or "disk.free_pct".</summary>
        public string Metric { get; set; } = "";
        /// <summary>"" when the metric has one series; otherwise e.g. "C:", "_Total", "3".</summary>
        public string Instance { get; set; } = "";
        public double Value { get; set; }
    }

    /// <summary>200 response to a report. The monitor uses AcceptedThroughUtc as its resend watermark.</summary>
    public sealed class ReportAck
    {
        public DateTime AcceptedThroughUtc { get; set; }
        public int ReportIntervalSeconds { get; set; }
        public DateTime ServerTimeUtc { get; set; }
    }

    /// <summary>GET /api/v1/monitors/me response.</summary>
    public sealed class MonitorInfo
    {
        public Guid MachineId { get; set; }
        public string DisplayName { get; set; } = "";
        public string Group { get; set; } = "";
        public int ReportIntervalSeconds { get; set; }
    }
}
