using System.Runtime.InteropServices;

namespace VailITMonitor.Collection;

/// <summary>
/// Direct calls into Windows. These are the cheapest possible way to read CPU and memory:
/// a single function call each, no WMI, no performance-counter machinery.
/// </summary>
/// <remarks>
/// [LibraryImport] (.NET 7+) generates the marshalling code at compile time instead of at run time,
/// which is faster and works with trimming/AOT. The .NET Framework edition uses the classic [DllImport].
/// </remarks>
internal static partial class NativeMethods
{
    /// <summary>System-wide idle/kernel/user time, in 100 ns units, since boot (kernel includes idle).</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PerformanceInformation
    {
        public int Size;
        public nuint CommitTotal;        // all values below are in pages
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;           // bytes per page
        public int HandleCount;
        public int ProcessCount;
        public int ThreadCount;
    }

    /// <summary>Memory, commit charge, process/thread/handle counts - all in one cheap call.</summary>
    [LibraryImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetPerformanceInfo(ref PerformanceInformation info, int size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessorPerformanceInformation
    {
        public long IdleTime;
        public long KernelTime;          // includes idle time
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public int InterruptCount;
        private int _padding;
    }

    internal const int SystemProcessorPerformanceInformation = 8;

    /// <summary>Per-logical-processor times (used for per-core CPU %).</summary>
    [LibraryImport("ntdll.dll")]
    internal static unsafe partial int NtQuerySystemInformation(int infoClass, void* buffer, int length, out int returnLength);
}
