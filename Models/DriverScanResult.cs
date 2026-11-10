namespace PCCare.Models;

public sealed class DriverScanResult
{
    public required IReadOnlyList<DriverDeviceInfo> Devices { get; init; }
    public required IReadOnlyList<DriverDeviceInfo> ProblemDevices { get; init; }
    public required DateTime ScannedAt { get; init; }
}

public sealed class DriverInstallResult
{
    public required int InstalledCount { get; init; }
    public required int FailedCount { get; init; }
    public required bool RebootRequired { get; init; }
}
