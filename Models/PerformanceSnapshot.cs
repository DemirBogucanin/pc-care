namespace PCCare.Models;

public sealed record PerformanceSnapshot(
    double CpuPercent,
    double RamPercent,
    double DiskUsedPercent,
    long TotalRamBytes,
    long UsedRamBytes,
    long DiskTotalBytes,
    long DiskFreeBytes,
    string CpuName,
    int LogicalCores,
    string OsName,
    TimeSpan Uptime);
