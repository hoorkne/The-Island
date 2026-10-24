using System;
using System.Runtime.InteropServices;

namespace RioIsland;

internal sealed class NativeMetrics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Size;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private ulong _idle, _total;
    private bool _hasBaseline;

    private static ulong ToUInt64(FileTime fileTime) => ((ulong)fileTime.High << 32) | fileTime.Low;

    public double CpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong nextIdle = ToUInt64(idle);
        ulong nextTotal = ToUInt64(kernel) + ToUInt64(user);
        if (!_hasBaseline)
        {
            _idle = nextIdle;
            _total = nextTotal;
            _hasBaseline = true;
            return 0;
        }
        ulong deltaTotal = nextTotal >= _total ? nextTotal - _total : 0;
        ulong deltaIdle = nextIdle >= _idle ? nextIdle - _idle : 0;
        _total = nextTotal;
        _idle = nextIdle;
        return deltaTotal == 0 ? 0 : Math.Clamp((double)(deltaTotal - Math.Min(deltaIdle, deltaTotal)) / deltaTotal * 100, 0, 100);
    }

    public (double Percent, double UsedGb, double TotalGb) RamUsage()
    {
        MemoryStatus status = new() { Size = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return (0, 0, 0);
        double total = status.TotalPhys / 1024.0 / 1024 / 1024;
        double used = (status.TotalPhys - status.AvailPhys) / 1024.0 / 1024 / 1024;
        return (Math.Clamp(used / total * 100, 0, 100), used, total);
    }
}
