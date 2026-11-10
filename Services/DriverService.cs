using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using PCCare.Helpers;
using PCCare.Models;

namespace PCCare.Services;

public sealed class DriverService
{
    private readonly List<object> _cachedUpdates = [];
    private object? _updateSession;

    public DriverScanResult ScanDevices()
    {
        var signedDrivers = LoadSignedDrivers();
        var devices = new List<DriverDeviceInfo>();

        using var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT Name, PNPClass, Manufacturer, DeviceID, ConfigManagerErrorCode, Status, Present FROM Win32_PnPEntity");

        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var present = ReadBool(item["Present"], true);
                if (!present)
                {
                    continue;
                }

                var deviceId = ReadString(item["DeviceID"]);
                var name = ReadString(item["Name"]);
                var errorCode = (int)ReadUInt(item["ConfigManagerErrorCode"]);
                var hasProblem = errorCode != 0;

                if (string.IsNullOrWhiteSpace(name) && !hasProblem)
                {
                    continue;
                }

                signedDrivers.TryGetValue(deviceId, out var driver);
                var className = ReadString(item["PNPClass"]);
                var manufacturer = ReadString(item["Manufacturer"]);
                var provider = driver?.Provider ?? manufacturer;
                var version = driver?.Version ?? "Not reported";
                var dateText = driver?.Date is DateTime date
                    ? date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
                    : "Unknown date";

                devices.Add(new DriverDeviceInfo
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "Unknown device" : name,
                    ClassName = string.IsNullOrWhiteSpace(className) ? "Unknown class" : className,
                    Manufacturer = string.IsNullOrWhiteSpace(manufacturer) ? "Unknown" : manufacturer,
                    DeviceId = string.IsNullOrWhiteSpace(deviceId) ? "Unknown" : deviceId,
                    HasProblem = hasProblem,
                    ProblemText = GetProblemText(errorCode),
                    DriverVersion = version,
                    DriverDateText = dateText,
                    Provider = string.IsNullOrWhiteSpace(provider) ? "Microsoft" : provider
                });
            }
        }

        var ordered = devices
            .OrderByDescending(device => device.HasProblem)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new DriverScanResult
        {
            Devices = ordered,
            ProblemDevices = ordered.Where(device => device.HasProblem).ToList(),
            ScannedAt = DateTime.Now
        };
    }

    public IReadOnlyList<DriverUpdateInfo> SearchDriverUpdates()
    {
        ClearUpdateCache();

        try
        {
            var session = CreateComObject("Microsoft.Update.Session");
            _updateSession = session;

            dynamic dynamicSession = session;
            dynamic searcher = dynamicSession.CreateUpdateSearcher();
            searcher.Online = true;
            dynamic result = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
            dynamic updates = result.Updates;

            var found = new List<DriverUpdateInfo>();
            var count = Convert.ToInt32(updates.Count, CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                dynamic update = updates.Item(index);
                var title = ReadComString(() => update.Title, "Driver update");
                var provider = ReadComString(() => update.DriverProvider, "Unknown provider");
                var driverClass = ReadComString(() => update.DriverClass, "Driver");
                var driverModel = ReadComString(() => update.DriverModel, string.Empty);
                var dateText = ReadComDate(() => update.DriverVerDate);
                var size = Convert.ToInt64(update.MaxDownloadSize, CultureInfo.InvariantCulture);
                var sizeText = Formatters.Bytes(size);
                var details = string.IsNullOrWhiteSpace(driverModel)
                    ? $"{driverClass}  |  {provider}  |  {dateText}  |  {sizeText}"
                    : $"{driverModel}  |  {provider}  |  {dateText}  |  {sizeText}";

                found.Add(new DriverUpdateInfo
                {
                    Title = title,
                    Provider = provider,
                    DriverClass = driverClass,
                    DriverModel = driverModel,
                    DateText = dateText,
                    SizeText = sizeText,
                    SizeBytes = size,
                    DetailsText = details,
                    Payload = update
                });

                _cachedUpdates.Add(update);
            }

            return found;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or TypeInitializationException)
        {
            throw new InvalidOperationException(
                $"Windows Update could not be queried. {ex.Message}",
                ex);
        }
    }

    public DriverInstallResult DownloadAndInstall(
        IReadOnlyList<DriverUpdateInfo> updates,
        Action<string>? reportStatus = null)
    {
        if (updates.Count == 0)
        {
            return new DriverInstallResult { InstalledCount = 0, FailedCount = 0, RebootRequired = false };
        }

        if (!IsElevated())
        {
            throw new InvalidOperationException(
                "Administrator permission is required to install driver updates.");
        }

        if (_updateSession is null)
        {
            throw new InvalidOperationException(
                "Check for driver updates before downloading them.");
        }

        try
        {
            dynamic session = _updateSession;
            dynamic collection = CreateComObject("Microsoft.Update.UpdateColl");

            foreach (var updateInfo in updates)
            {
                dynamic update = updateInfo.Payload
                    ?? throw new InvalidOperationException("A driver update is no longer available.");

                if (!Convert.ToBoolean(update.EulaAccepted, CultureInfo.InvariantCulture))
                {
                    update.AcceptEula();
                }

                collection.Add(update);
            }

            reportStatus?.Invoke($"Downloading {updates.Count} driver update(s)...");
            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = collection;
            dynamic downloadResult = downloader.Download();
            var downloadCode = Convert.ToInt32(downloadResult.ResultCode, CultureInfo.InvariantCulture);
            if (downloadCode is not (2 or 3))
            {
                throw new InvalidOperationException(
                    $"Driver download failed (result {downloadCode}, HRESULT 0x{Convert.ToInt32(downloadResult.HResult):X8}).");
            }

            reportStatus?.Invoke("Installing driver updates...");
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = collection;
            dynamic installResult = installer.Install();
            var installCode = Convert.ToInt32(installResult.ResultCode, CultureInfo.InvariantCulture);
            if (installCode is not (2 or 3))
            {
                throw new InvalidOperationException(
                    $"Driver installation failed (result {installCode}, HRESULT 0x{Convert.ToInt32(installResult.HResult):X8}).");
            }

            var installed = 0;
            var failed = 0;
            var rebootRequired = Convert.ToBoolean(installResult.RebootRequired, CultureInfo.InvariantCulture);

            for (var index = 0; index < updates.Count; index++)
            {
                dynamic updateResult = installResult.GetUpdateResult(index);
                var resultCode = Convert.ToInt32(updateResult.ResultCode, CultureInfo.InvariantCulture);
                if (resultCode == 2)
                {
                    installed++;
                }
                else
                {
                    failed++;
                }

                rebootRequired |= Convert.ToBoolean(updateResult.RebootRequired, CultureInfo.InvariantCulture);
            }

            ClearUpdateCache();

            return new DriverInstallResult
            {
                InstalledCount = installed,
                FailedCount = failed,
                RebootRequired = rebootRequired
            };
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Windows Update could not install the drivers. {ex.Message}",
                ex);
        }
    }

    private static Dictionary<string, DriverDetails> LoadSignedDrivers()
    {
        var result = new Dictionary<string, DriverDetails>(StringComparer.OrdinalIgnoreCase);

        using var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT DeviceID, DriverVersion, DriverDate, DriverProviderName FROM Win32_PnPSignedDriver");

        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item)
            {
                var deviceId = ReadString(item["DeviceID"]);
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                result[deviceId] = new DriverDetails(
                    ReadString(item["DriverVersion"]) is { Length: > 0 } version ? version : "Not reported",
                    ReadDate(item["DriverDate"]),
                    ReadString(item["DriverProviderName"]) is { Length: > 0 } provider ? provider : "Unknown");
            }
        }

        return result;
    }

    private static object CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: false)
            ?? throw new InvalidOperationException($"{progId} is not available on this version of Windows.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"{progId} could not be started.");
    }

    private static string ReadComString(Func<object?> getter, string fallback)
    {
        try
        {
            var value = getter();
            return value is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : fallback;
        }
        catch (COMException)
        {
            return fallback;
        }
    }

    private static string ReadComDate(Func<object?> getter)
    {
        try
        {
            return getter() is DateTime date
                ? date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
                : "Unknown date";
        }
        catch (COMException)
        {
            return "Unknown date";
        }
    }

    private static string ReadString(object? value) => value is string text ? text.Trim() : string.Empty;

    private static bool ReadBool(object? value, bool fallback)
    {
        try
        {
            return value is null ? fallback : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static uint ReadUInt(object? value)
    {
        try
        {
            return value is null ? 0 : Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime? ReadDate(object? value)
    {
        if (value is DateTime date)
        {
            return date;
        }

        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(text);
        }
        catch
        {
            return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private void ClearUpdateCache()
    {
        foreach (var update in _cachedUpdates)
        {
            ReleaseComObject(update);
        }

        _cachedUpdates.Clear();
        ReleaseComObject(_updateSession);
        _updateSession = null;
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is null || !Marshal.IsComObject(instance))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(instance);
        }
        catch
        {
        }
    }

    private static string GetProblemText(int errorCode) => errorCode switch
    {
        1 => "This device is not configured correctly.",
        3 => "The driver is corrupted or missing.",
        10 => "The device cannot start.",
        12 => "The device cannot find enough free resources.",
        14 => "A restart is required before this device works.",
        18 => "The drivers must be reinstalled.",
        19 => "The registry configuration for this device is invalid.",
        21 => "Windows is removing this device.",
        22 => "This device is disabled.",
        24 => "This device is not present, is not working properly, or does not have all its drivers installed.",
        28 => "The drivers for this device are not installed.",
        31 => "This device is not working properly.",
        32 => "The driver for this device is disabled.",
        37 => "The driver could not be initialized.",
        39 => "The driver is corrupted or missing.",
        43 => "Windows stopped this device after it reported a problem.",
        45 => "This device is not currently connected.",
        _ => $"Windows reported a device problem (code {errorCode})."
    };

    private sealed record DriverDetails(string Version, DateTime? Date, string Provider);
}

