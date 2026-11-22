using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using PCCare.Models;
using PCCare.Services;

namespace PCCare.ViewModels;

public sealed class UpdateViewModel : ObservableObject
{
    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCCare");

    private static readonly string StatePath = Path.Combine(StateDirectory, "update-state.json");
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly UpdateService _updateService = new();
    private UpdateInfo? _pending;
    private bool _isChecking;
    private bool _isDownloading;
    private bool _isBannerVisible;
    private double _progress;
    private string _latestVersionText = string.Empty;
    private string _message = string.Empty;

    public UpdateViewModel()
    {
        CheckCommand = new AsyncRelayCommand(() => CheckAsync(true), () => !IsChecking && !IsDownloading);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsDownloading && _pending is not null);
        DismissCommand = new RelayCommand(Dismiss, () => !IsDownloading);
    }

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand DismissCommand { get; }

    public event EventHandler<string>? Notified;
    public event EventHandler<string>? Failed;

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                OnPropertyChanged(nameof(CheckLinkText));
                OnPropertyChanged(nameof(CheckLinkEnabled));
                RefreshCommands();
            }
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                OnPropertyChanged(nameof(PrimaryText));
                OnPropertyChanged(nameof(IsProgressVisible));
                OnPropertyChanged(nameof(CheckLinkEnabled));
                RefreshCommands();
            }
        }
    }

    public bool IsBannerVisible
    {
        get => _isBannerVisible;
        private set
        {
            if (SetProperty(ref _isBannerVisible, value))
            {
                OnPropertyChanged(nameof(CheckLinkText));
            }
        }
    }

    public bool IsProgressVisible => IsDownloading;

    public bool CheckLinkEnabled => !IsChecking && !IsDownloading;

    public double Progress
    {
        get => _progress;
        private set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(PrimaryText));
            }
        }
    }

    public string LatestVersionText
    {
        get => _latestVersionText;
        private set
        {
            if (SetProperty(ref _latestVersionText, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string Title => string.IsNullOrEmpty(LatestVersionText)
        ? "Update available"
        : $"PC Care {LatestVersionText} is available";

    public string PrimaryText => IsDownloading ? $"Downloading {Progress:0}%" : "Update now";

    public string CheckLinkText => IsChecking
        ? "Checking for updates..."
        : _pending is not null ? "Update available" : "Check for updates";

    public string NotesToolTip => _pending?.Notes ?? string.Empty;

    public async Task CheckOnStartupAsync()
    {
        var state = LoadState();
        if (state.LastCheckUtc > DateTime.UtcNow - CheckInterval)
        {
            return;
        }

        await CheckAsync(false);
    }

    private async Task CheckAsync(bool manual)
    {
        if (IsChecking || IsDownloading)
        {
            return;
        }

        IsChecking = true;
        var state = LoadState();

        try
        {
            var info = await _updateService.CheckAsync();
            state.LastCheckUtc = DateTime.UtcNow;
            SaveState(state);

            if (info is null)
            {
                _pending = null;
                IsBannerVisible = false;
                OnPropertyChanged(nameof(CheckLinkText));

                if (manual)
                {
                    Notified?.Invoke(this, "You are running the latest version.");
                }

                return;
            }

            if (!manual &&
                string.Equals(state.SkippedVersion, info.Version.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _pending = info;
            LatestVersionText = "v" + info.Version.ToString(3);
            Message = $"You have v{UpdateService.CurrentVersion.ToString(3)}. The new build is ready to install.";
            IsBannerVisible = true;
            OnPropertyChanged(nameof(CheckLinkText));
            OnPropertyChanged(nameof(NotesToolTip));
            RefreshCommands();

            if (manual)
            {
                Notified?.Invoke(this, $"Version {LatestVersionText} is available.");
            }
            else
            {
                Notified?.Invoke(this, $"PC Care {LatestVersionText} is available.");
            }
        }
        catch
        {
            if (manual)
            {
                Failed?.Invoke(this, "Could not check for updates. Check your connection.");
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task InstallAsync()
    {
        if (_pending is null || IsDownloading)
        {
            return;
        }

        if (!UpdateService.IsInstalledCopy)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _pending.DownloadUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                Failed?.Invoke(this, "Could not open the download page.");
            }

            IsBannerVisible = false;
            return;
        }

        IsDownloading = true;
        Progress = 0;

        try
        {
            var progress = new Progress<double>(value => Progress = value);
            var installerPath = await _updateService.DownloadAsync(_pending, progress);

            if (_updateService.LaunchInstaller(installerPath))
            {
                Application.Current.Shutdown();
                return;
            }

            Failed?.Invoke(this, "The installer could not be started.");
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, $"Update failed: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private void Dismiss()
    {
        IsBannerVisible = false;

        if (_pending is not null)
        {
            var state = LoadState();
            state.SkippedVersion = _pending.Version.ToString();
            SaveState(state);
        }
    }

    private void RefreshCommands()
    {
        CheckCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
        DismissCommand.RaiseCanExecuteChanged();
    }

    private static UpdateState LoadState()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return new UpdateState();
            }

            return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(StatePath)) ?? new UpdateState();
        }
        catch
        {
            return new UpdateState();
        }
    }

    private static void SaveState(UpdateState state)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // Losing the throttle state is harmless.
        }
    }

    private sealed class UpdateState
    {
        public DateTime LastCheckUtc { get; set; }
        public string? SkippedVersion { get; set; }
    }
}
