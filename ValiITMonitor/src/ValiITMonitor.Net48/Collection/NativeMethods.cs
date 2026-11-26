using System;
using System.Runtime.InteropServices;

namespace ValiITMonitor.Collection
{
    /// <summary>
    /// Direct calls into Windows - the cheapest way to read CPU and memory.
    /// Classic [DllImport]: the marshalling code is generated at run time.
    /// (The .NET 10 edition uses [LibraryImport], which generates it at compile time.)
    /// </summary>
    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        [StructLayout(LayoutKind.Sequential)]
        internal struct PerformanceInformation
        {
            public int Size;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public int HandleCount;
            public int ProcessCount;
            public int ThreadCount;
        }

        [DllImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPerformanceInfo(ref PerformanceInformation info, int size);

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessorPerformanceInformation
        {
            public long IdleTime;
            public long KernelTime;
            public long UserTime;
            public long DpcTime;
            public long InterruptTime;
            public int InterruptCount;
            private int _padding;
        }

        internal const int SystemProcessorPerformanceInformation = 8;

        [DllImport("ntdll.dll")]
        internal static extern int NtQuerySystemInformation(
            int infoClass,
            [Out] ProcessorPerformanceInformation[] buffer,
            int length,
            out int returnLength);

        /// <summary>Milliseconds since boot. Environment.TickCount is 32-bit and wraps after 49.7 days.</summary>
        [DllImport("kernel32.dll")]
        internal static extern ulong GetTickCount64();
    }
}
