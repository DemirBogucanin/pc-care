using System.Collections.ObjectModel;
using System.Windows.Threading;
using PCCare.Models;
using PCCare.Services;

namespace PCCare.ViewModels;

public sealed class DriversViewModel : ObservableObject
{
    private readonly DriverService _driverService;
    private readonly Dispatcher _dispatcher;
    private bool _isScanning;
    private bool _isChecking;
    private bool _isInstalling;
    private bool _hasLoaded;
    private bool _hasCheckedUpdates;
    private bool _rebootRequired;
    private int _deviceCount;
    private int _problemCount;
    private int _updateCount;
    private string _statusText = "Scanning your devices...";

    public DriversViewModel(DriverService driverService)
    {
        _driverService = driverService;
        _dispatcher = Dispatcher.CurrentDispatcher;
        RefreshCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync, () => !IsBusy);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy && UpdateCount > 0);
    }

    public ObservableCollection<DriverDeviceInfo> ProblemDevices { get; } = [];
    public ObservableCollection<DriverUpdateInfo> Updates { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }

    public event EventHandler<string>? Failed;
    public event EventHandler<string>? Changed;
    public event EventHandler? DevicesUpdated;

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnBusyChanged();
            }
        }
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                OnBusyChanged();
            }
        }
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetProperty(ref _isInstalling, value))
            {
                OnBusyChanged();
            }
        }
    }

    public bool IsBusy => IsScanning || IsChecking || IsInstalling;

    public bool HasLoaded
    {
        get => _hasLoaded;
        private set
        {
            if (SetProperty(ref _hasLoaded, value))
            {
                OnPropertyChanged(nameof(HealthHeadline));
                OnPropertyChanged(nameof(HealthDescription));
                OnPropertyChanged(nameof(ShowHealthyState));
            }
        }
    }

    public bool HasCheckedUpdates
    {
        get => _hasCheckedUpdates;
        private set
        {
            if (SetProperty(ref _hasCheckedUpdates, value))
            {
                OnPropertyChanged(nameof(ShowNoUpdates));
                OnPropertyChanged(nameof(ShowNotChecked));
                OnPropertyChanged(nameof(HasUpdates));
                OnPropertyChanged(nameof(UpdateCountText));
                OnPropertyChanged(nameof(UpdateSummaryText));
            }
        }
    }

    public bool RebootRequired
    {
        get => _rebootRequired;
        private set
        {
            if (SetProperty(ref _rebootRequired, value))
            {
                OnPropertyChanged(nameof(RebootText));
                OnPropertyChanged(nameof(ShowRebootNotice));
            }
        }
    }

    public int DeviceCount
    {
        get => _deviceCount;
        private set
        {
            if (SetProperty(ref _deviceCount, value))
            {
                OnPropertyChanged(nameof(DeviceCountText));
            }
        }
    }

    public int ProblemCount
    {
        get => _problemCount;
        private set
        {
            if (SetProperty(ref _problemCount, value))
            {
                OnPropertyChanged(nameof(ProblemCountText));
                OnPropertyChanged(nameof(HasProblems));
                OnPropertyChanged(nameof(AttentionAccentHex));
                OnPropertyChanged(nameof(HealthHeadline));
                OnPropertyChanged(nameof(HealthDescription));
                OnPropertyChanged(nameof(ShowHealthyState));
            }
        }
    }

    public int UpdateCount
    {
        get => _updateCount;
        private set
        {
            if (SetProperty(ref _updateCount, value))
            {
                OnPropertyChanged(nameof(UpdateCountText));
                OnPropertyChanged(nameof(HasUpdates));
                OnPropertyChanged(nameof(UpdateAccentHex));
                OnPropertyChanged(nameof(ShowNoUpdates));
                OnPropertyChanged(nameof(UpdateSummaryText));
                OnPropertyChanged(nameof(InstallButtonText));
                InstallCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DeviceCountText => DeviceCount.ToString();
    public string ProblemCountText => ProblemCount.ToString();
    public string UpdateCountText => HasCheckedUpdates ? UpdateCount.ToString() : "—";
    public bool HasProblems => ProblemCount > 0;
    public bool HasUpdates => UpdateCount > 0;
    public bool ShowNoUpdates => HasCheckedUpdates && UpdateCount == 0 && !IsBusy;
    public bool ShowNotChecked => !HasCheckedUpdates && !IsBusy;
    public bool ShowHealthyState => HasLoaded && ProblemCount == 0;
    public string AttentionAccentHex => ProblemCount > 0 ? "#F4B860" : "#3DDC97";
    public string UpdateAccentHex => UpdateCount > 0 ? "#6699FF" : "#3DDC97";

    public string HealthHeadline => !HasLoaded
        ? "Scanning devices"
        : ProblemCount == 0
            ? "All devices are healthy"
            : ProblemCount == 1
                ? "1 device needs attention"
                : $"{ProblemCount} devices need attention";

    public string HealthDescription => ProblemCount == 0
        ? "Windows did not report any device or driver problems."
        : "Review the devices below, then install the available driver updates.";

    public string UpdateSummaryText => !HasCheckedUpdates
        ? "Not checked yet"
        : UpdateCount == 0
            ? "No driver updates available"
            : UpdateCount == 1
                ? "1 driver update available"
                : $"{UpdateCount} driver updates available";

    public string CheckButtonText => IsChecking ? "Checking..." : "Check for updates";
    public string InstallButtonText => IsInstalling ? "Installing..." : "Download & install";
    public string RebootText => RebootRequired
        ? "A restart is required to finish installing the driver updates."
        : string.Empty;
    public bool ShowRebootNotice => RebootRequired;

    public async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsScanning = true;
        StatusText = "Scanning devices and installed drivers...";
        try
        {
            var result = await Task.Run(_driverService.ScanDevices);
            ApplyDeviceScan(result);
            StatusText = $"Device scan complete. {DeviceCount} devices checked at {DateTime.Now:HH:mm}.";
        }
        catch (Exception ex)
        {
            HasLoaded = true;
            StatusText = "Device scan could not be completed.";
            Failed?.Invoke(this, $"Driver scan failed: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task CheckUpdatesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsChecking = true;
        HasCheckedUpdates = false;
        ReplaceUpdates([]);
        RebootRequired = false;
        StatusText = "Scanning devices and searching Windows Update...";
        try
        {
            var scan = await Task.Run(_driverService.ScanDevices);
            ApplyDeviceScan(scan);

            StatusText = "Searching Microsoft Update for driver packages...";
            var updates = await Task.Run(_driverService.SearchDriverUpdates);
            ReplaceUpdates(updates);
            HasCheckedUpdates = true;
            RebootRequired = false;

            StatusText = updates.Count == 0
                ? $"No driver updates available. Checked at {DateTime.Now:HH:mm}."
                : $"{updates.Count} driver updates found at {DateTime.Now:HH:mm}.";

            Changed?.Invoke(this, UpdateSummaryText + ".");
        }
        catch (Exception ex)
        {
            HasCheckedUpdates = true;
            StatusText = "Windows Update could not be checked.";
            Failed?.Invoke(this, $"Driver update check failed: {ex.Message}");
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task InstallAsync()
    {
        if (IsBusy || UpdateCount == 0)
        {
            return;
        }

        var updates = Updates.ToList();
        IsInstalling = true;
        StatusText = "Preparing driver downloads...";

        try
        {
            var result = await Task.Run(() => _driverService.DownloadAndInstall(
                updates,
                message => _dispatcher.Invoke(() => StatusText = message)));

            RebootRequired = result.RebootRequired;
            ReplaceUpdates([]);
            HasCheckedUpdates = true;
            UpdateCount = 0;

            var scan = await Task.Run(_driverService.ScanDevices);
            ApplyDeviceScan(scan);

            if (result.InstalledCount > 0)
            {
                var message = result.FailedCount == 0
                    ? $"{result.InstalledCount} driver update(s) installed."
                    : $"{result.InstalledCount} driver update(s) installed, {result.FailedCount} failed.";
                if (result.RebootRequired)
                {
                    message += " Restart Windows to finish.";
                }

                StatusText = message;
                Changed?.Invoke(this, message);
            }
            else
            {
                StatusText = "No driver updates were installed.";
                Failed?.Invoke(this, "Windows Update did not install any of the selected driver updates.");
            }
        }
        catch (Exception ex)
        {
            StatusText = "Driver installation could not be completed.";
            Failed?.Invoke(this, $"Driver installation failed: {ex.Message}");
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void ApplyDeviceScan(DriverScanResult result)
    {
        ProblemDevices.Clear();
        foreach (var device in result.ProblemDevices)
        {
            ProblemDevices.Add(device);
        }

        DeviceCount = result.Devices.Count;
        ProblemCount = result.ProblemDevices.Count;
        HasLoaded = true;
        DevicesUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceUpdates(IReadOnlyList<DriverUpdateInfo> updates)
    {
        Updates.Clear();
        foreach (var update in updates)
        {
            Updates.Add(update);
        }

        UpdateCount = updates.Count;
    }

    private void OnBusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowNoUpdates));
        OnPropertyChanged(nameof(ShowNotChecked));
        OnPropertyChanged(nameof(CheckButtonText));
        OnPropertyChanged(nameof(InstallButtonText));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        CheckUpdatesCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
    }
}
