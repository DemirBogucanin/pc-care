using System.Security.Principal;
using System.Windows.Threading;
using PCCare.Helpers;
using PCCare.Models;
using PCCare.Services;

namespace PCCare.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _toastTimer;
    private string _currentPage = "Dashboard";
    private double _healthScore = 100;
    private string _healthLabel = "Excellent";
    private string _healthDescription = "Your system is ready to go.";
    private string _healthColorHex = "#3DDC97";
    private string _toastMessage = string.Empty;
    private bool _isToastVisible;
    private bool _toastIsError;
    private bool _initialized;

    public MainViewModel()
    {
        var performanceService = new PerformanceService();
        var cleanerService = new CleanerService();
        var startupService = new StartupService();
        var driverService = new DriverService();

        Cleaner = new CleanerViewModel(cleanerService);
        Startup = new StartupViewModel(startupService);
        Drivers = new DriversViewModel(driverService);
        Performance = new PerformanceViewModel(performanceService);

        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is string page)
            {
                Navigate(page);
            }
        });
        OptimizeCommand = new AsyncRelayCommand(OptimizeAsync, () => !Cleaner.IsBusy);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _toastTimer.Tick += (_, _) => HideToast();

        Cleaner.ScanCompleted += (_, _) =>
        {
            UpdateHealth();
            OnPropertyChanged(nameof(ReclaimableText));
            OnPropertyChanged(nameof(HasCleanableItems));
        };
        Cleaner.CleanupCompleted += OnCleanupCompleted;
        Cleaner.Failed += (_, message) => ShowToast(message, true);
        Cleaner.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(CleanerViewModel.IsBusy) or
                nameof(CleanerViewModel.IsScanning) or
                nameof(CleanerViewModel.IsCleaning))
            {
                OnPropertyChanged(nameof(OptimizeButtonText));
                OnPropertyChanged(nameof(IsOptimizing));
                OptimizeCommand.RaiseCanExecuteChanged();
            }

            if (args.PropertyName is nameof(CleanerViewModel.SelectedBytes))
            {
                OnPropertyChanged(nameof(ReclaimableText));
                OnPropertyChanged(nameof(HasCleanableItems));
                UpdateHealth();
            }
        };

        Startup.Changed += (_, message) => ShowToast(message, false);
        Startup.Failed += (_, message) => ShowToast(message, true);
        Startup.EntriesUpdated += (_, _) =>
        {
            UpdateHealth();
            OnPropertyChanged(nameof(StartupSummaryText));
        };
        Startup.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(StartupViewModel.EnabledCount))
            {
                OnPropertyChanged(nameof(StartupSummaryText));
                UpdateHealth();
            }
        };

        Drivers.Changed += (_, message) => ShowToast(message, false);
        Drivers.Failed += (_, message) => ShowToast(message, true);
        Drivers.DevicesUpdated += (_, _) => UpdateHealth();

        Performance.SnapshotUpdated += (_, _) =>
        {
            UpdateHealth();
            OnPropertyChanged(nameof(StorageSummaryText));
            OnPropertyChanged(nameof(StorageFreePercentText));
        };

        Greeting = GetGreeting();
        TodayText = DateTime.Now.ToString("dddd, MMMM d");
        UpdateHealth();
    }

    public CleanerViewModel Cleaner { get; }
    public StartupViewModel Startup { get; }
    public DriversViewModel Drivers { get; }
    public PerformanceViewModel Performance { get; }
    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand OptimizeCommand { get; }

    public string CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsDashboardPage));
                OnPropertyChanged(nameof(IsCleanerPage));
                OnPropertyChanged(nameof(IsStartupPage));
                OnPropertyChanged(nameof(IsDriversPage));
                OnPropertyChanged(nameof(IsPerformancePage));
            }
        }
    }

    public bool IsDashboardPage => CurrentPage == "Dashboard";
    public bool IsCleanerPage => CurrentPage == "Cleaner";
    public bool IsStartupPage => CurrentPage == "Startup";
    public bool IsDriversPage => CurrentPage == "Drivers";
    public bool IsPerformancePage => CurrentPage == "Performance";

    public string Greeting { get; }
    public string TodayText { get; }
    public string AppVersion => "Version 1.1.0";

    public bool IsElevated
    {
        get
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
    }

    public string PrivilegeText => IsElevated ? "Administrator" : "Standard user";

    public double HealthScore
    {
        get => _healthScore;
        private set
        {
            if (SetProperty(ref _healthScore, value))
            {
                OnPropertyChanged(nameof(HealthScoreText));
            }
        }
    }

    public string HealthScoreText => $"{HealthScore:0}";
    public string HealthLabel => _healthLabel;
    public string HealthDescription => _healthDescription;
    public string HealthColorHex => _healthColorHex;

    public string OptimizeButtonText => Cleaner.IsCleaning
        ? "Optimizing..."
        : Cleaner.IsScanning ? "Analyzing..." : "Optimize PC";

    public bool IsOptimizing => Cleaner.IsBusy;
    public bool HasCleanableItems => Cleaner.SelectedBytes > 0;
    public string ReclaimableText => Cleaner.SelectedBytes > 0
        ? $"{Formatters.Bytes(Cleaner.SelectedBytes)} ready to clean"
        : Cleaner.HasScanned ? "No unnecessary files found" : "Analyzing your PC...";

    public string StorageSummaryText =>
        $"{Formatters.Bytes(Performance.DiskFreeBytes)} free of {Formatters.Bytes(Performance.DiskTotalBytes)}";

    public string StorageFreePercentText => $"{Performance.DiskFreePercent:0}%";
    public string StartupSummaryText => Startup.SummaryText;

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public bool ToastIsError
    {
        get => _toastIsError;
        private set
        {
            if (SetProperty(ref _toastIsError, value))
            {
                OnPropertyChanged(nameof(ToastAccentHex));
                OnPropertyChanged(nameof(ToastGlyph));
            }
        }
    }

    public string ToastGlyph => ToastIsError ? "\uE783" : "\uE73E";
    public string ToastAccentHex => ToastIsError ? "#F06E7A" : "#3DDC97";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Performance.Start();

        try
        {
            await Task.WhenAll(
                Cleaner.ScanAsync(),
                Startup.LoadAsync(),
                Drivers.ScanAsync());
        }
        catch (Exception ex)
        {
            ShowToast($"Startup check failed: {ex.Message}", true);
        }

        UpdateHealth();
    }

    public void Stop()
    {
        _toastTimer.Stop();
        Performance.Stop();
    }

    private void Navigate(string page)
    {
        if (page is "Dashboard" or "Cleaner" or "Startup" or "Drivers" or "Performance")
        {
            CurrentPage = page;
        }
    }

    private async Task OptimizeAsync()
    {
        if (Cleaner.IsBusy)
        {
            return;
        }

        if (!Cleaner.Categories.Any(category => category.IsSelected))
        {
            Navigate("Cleaner");
            ShowToast("Choose at least one cleanup location to continue.", true);
            return;
        }

        if (!Cleaner.HasScanned)
        {
            await Cleaner.ScanAsync();
        }

        await Cleaner.CleanSelectedAsync("optimize");
    }

    private void OnCleanupCompleted(object? sender, CleanupCompletedEventArgs e)
    {
        var verb = e.Context == "optimize" ? "Optimization" : "Cleanup";
        if (e.FreedBytes > 0)
        {
            var files = e.DeletedCount == 1 ? "1 file" : $"{e.DeletedCount:N0} files";
            ShowToast($"{verb} complete · {Formatters.Bytes(e.FreedBytes)} freed ({files})");
        }
        else
        {
            ShowToast("Everything is already clean. No files needed to be removed.");
        }

        OnPropertyChanged(nameof(ReclaimableText));
        OnPropertyChanged(nameof(HasCleanableItems));
        UpdateHealth();
    }

    private void UpdateHealth()
    {
        var score = 100.0;

        if (Performance.CpuPercent > 25)
        {
            score -= Math.Min(14, (Performance.CpuPercent - 25) * 0.24);
        }

        if (Performance.RamPercent > 55)
        {
            score -= Math.Min(14, (Performance.RamPercent - 55) * 0.32);
        }

        if (Performance.DiskPercent > 75)
        {
            score -= Math.Min(18, (Performance.DiskPercent - 75) * 0.58);
        }

        if (Cleaner.SelectedBytes > 0)
        {
            score -= Math.Min(9, Cleaner.SelectedBytes / 1024.0 / 1024 / 1024 * 0.55);
        }

        if (Startup.EnabledCount > 7)
        {
            score -= Math.Min(8, (Startup.EnabledCount - 7) * 1.15);
        }

        if (Drivers.ProblemCount > 0)
        {
            score -= Math.Min(9, Drivers.ProblemCount * 4.5);
        }

        var rounded = Math.Clamp(Math.Round(score), 45, 100);
        HealthScore = rounded;

        var label = rounded >= 90 ? "Excellent"
            : rounded >= 75 ? "Good"
            : rounded >= 60 ? "Fair"
            : "Needs attention";

        var color = rounded >= 90 ? "#3DDC97"
            : rounded >= 75 ? "#6EA8FF"
            : rounded >= 60 ? "#F4B860"
            : "#F06E7A";

        var description = Cleaner.SelectedBytes > 1024 * 1024 * 512
            ? $"{Formatters.Bytes(Cleaner.SelectedBytes)} of temporary data can be cleaned."
            : Performance.CpuPercent > 65
                ? "CPU activity is high. Close heavy apps for a boost."
                : Performance.RamPercent > 80
                    ? "Memory is nearly full. Restarting heavy apps may help."
                    : Performance.DiskPercent > 85
                        ? "Your system drive is getting full."
                        : Drivers.ProblemCount > 0
                            ? $"{Drivers.ProblemCount} device(s) need a driver check."
                            : rounded >= 90
                                ? "Your PC is clean, fast, and well maintained."
                                : "A quick optimization will keep things fast.";

        if (label != _healthLabel)
        {
            _healthLabel = label;
            OnPropertyChanged(nameof(HealthLabel));
        }

        if (color != _healthColorHex)
        {
            _healthColorHex = color;
            OnPropertyChanged(nameof(HealthColorHex));
        }

        if (description != _healthDescription)
        {
            _healthDescription = description;
            OnPropertyChanged(nameof(HealthDescription));
        }
    }

    private void ShowToast(string message, bool isError = false)
    {
        ToastMessage = message;
        ToastIsError = isError;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Interval = isError ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(4.5);
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        IsToastVisible = false;
    }

    private static string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
    }
}
