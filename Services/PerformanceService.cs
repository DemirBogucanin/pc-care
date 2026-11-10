using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PCCare.Models;

namespace PCCare.Services;

public sealed class PerformanceService
{
    private readonly object _sync = new();
    private readonly string _cpuName;
    private readonly string _osName;
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasCpuBaseline;

    public PerformanceService()
    {
        _cpuName = ReadProcessorName();
        _osName = ReadOsName();
    }

    public PerformanceSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var cpu = ReadCpuUsage();

            var memory = new MemoryStatusEx();
            memory.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
            GlobalMemoryStatusEx(ref memory);

            var usedRam = (long)(memory.TotalPhysical - memory.AvailablePhysical);
            var ramPercent = memory.TotalPhysical > 0
                ? usedRam * 100.0 / memory.TotalPhysical
                : 0;

            long diskTotal = 0;
            long diskFree = 0;
            try
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    diskTotal = drive.TotalSize;
                    diskFree = drive.AvailableFreeSpace;
                }
            }
            catch
            {
                // Disk information is optional; the rest of the dashboard remains usable.
            }

            var diskUsedPercent = diskTotal > 0 ? (diskTotal - diskFree) * 100.0 / diskTotal : 0;

            return new PerformanceSnapshot(
                Math.Clamp(cpu, 0, 100),
                Math.Clamp(ramPercent, 0, 100),
                Math.Clamp(diskUsedPercent, 0, 100),
                (long)memory.TotalPhysical,
                usedRam,
                diskTotal,
                diskFree,
                _cpuName,
                Environment.ProcessorCount,
                _osName,
                TimeSpan.FromMilliseconds(Environment.TickCount64));
        }
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleValue = idle.ToUInt64();
        var kernelValue = kernel.ToUInt64();
        var userValue = user.ToUInt64();

        if (!_hasCpuBaseline)
        {
            _previousIdle = idleValue;
            _previousKernel = kernelValue;
            _previousUser = userValue;
            _hasCpuBaseline = true;
            return 0;
        }

        var idleDelta = idleValue - _previousIdle;
        var kernelDelta = kernelValue - _previousKernel;
        var userDelta = userValue - _previousUser;
        var totalDelta = kernelDelta + userDelta;

        _previousIdle = idleValue;
        _previousKernel = kernelValue;
        _previousUser = userValue;

        if (totalDelta == 0)
        {
            return 0;
        }

        return (totalDelta - idleDelta) * 100.0 / totalDelta;
    }

    private static string ReadProcessorName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var name = key?.GetValue("ProcessorNameString") as string;
            return string.IsNullOrWhiteSpace(name)
                ? $"{Environment.ProcessorCount}-thread processor"
                : name.Trim();
        }
        catch
        {
            return $"{Environment.ProcessorCount}-thread processor";
        }
    }

    private static string ReadOsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var displayVersion = key?.GetValue("DisplayVersion") as string;
            var buildText = key?.GetValue("CurrentBuildNumber") as string;

            if (int.TryParse(buildText, out var build) && build >= 22000)
            {
                product = product.Replace("Windows 10", "Windows 11");
            }

            return string.IsNullOrWhiteSpace(displayVersion)
                ? product
                : $"{product} · {displayVersion}";
        }
        catch
        {
            return "Windows";
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
