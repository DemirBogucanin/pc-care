using System.Collections.ObjectModel;
using System.ComponentModel;
using PCCare.Helpers;
using PCCare.Models;
using PCCare.Services;

namespace PCCare.ViewModels;

public sealed class CleanupCompletedEventArgs : EventArgs
{
    public required long FreedBytes { get; init; }
    public required int DeletedCount { get; init; }
    public required DateTime CompletedAt { get; init; }
    public required string Context { get; init; }
}

public sealed class CleanerViewModel : ObservableObject
{
    private readonly CleanerService _cleanerService;
    private bool _isScanning;
    private bool _isCleaning;
    private double _progress;
    private string _statusText = "Ready to scan";
    private long _selectedBytes;
    private long _lastFreedBytes;
    private int _lastDeletedCount;
    private DateTime? _lastCleanTime;
    private long _liveFreedBytes;

    public CleanerViewModel(CleanerService cleanerService)
    {
        _cleanerService = cleanerService;
        foreach (var definition in _cleanerService.CreateDefinitions())
        {
            var category = new CleanupCategory(definition);
            category.PropertyChanged += OnCategoryPropertyChanged;
            Categories.Add(category);
        }

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        CleanCommand = new AsyncRelayCommand(
            () => CleanSelectedAsync("cleaner"),
            () => !IsBusy && Categories.Any(category => category.IsSelected));
        SelectAllCommand = new RelayCommand(SelectAll, () => !IsBusy);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy);
    }

    public ObservableCollection<CleanupCategory> Categories { get; } = [];
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }

    public event EventHandler? ScanCompleted;
    public event EventHandler<CleanupCompletedEventArgs>? CleanupCompleted;
    public event EventHandler<string>? Failed;

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(BusyLabel));
                RefreshCommands();
            }
        }
    }

    public bool IsCleaning
    {
        get => _isCleaning;
        private set
        {
            if (SetProperty(ref _isCleaning, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(BusyLabel));
                RefreshCommands();
            }
        }
    }

    public bool IsBusy => IsScanning || IsCleaning;
    public string BusyLabel => IsCleaning ? "Optimizing" : IsScanning ? "Scanning" : "Ready";

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public long SelectedBytes
    {
        get => _selectedBytes;
        private set
        {
            if (SetProperty(ref _selectedBytes, value))
            {
                OnPropertyChanged(nameof(SelectedSizeText));
            }
        }
    }

    public string SelectedSizeText => Formatters.Bytes(SelectedBytes);
    public string SelectedSummary =>
        $"{Categories.Count(category => category.IsSelected)} of {Categories.Count} locations selected";

    public bool HasScanned => Categories.Any(category => category.IsScanned);
    public bool HasItemsToClean => Categories.Any(category => category.IsSelected && category.SizeBytes > 0);

    public long LastFreedBytes
    {
        get => _lastFreedBytes;
        private set
        {
            if (SetProperty(ref _lastFreedBytes, value))
            {
                OnPropertyChanged(nameof(LastFreedText));
            }
        }
    }

    public int LastDeletedCount
    {
        get => _lastDeletedCount;
        private set
        {
            if (SetProperty(ref _lastDeletedCount, value))
            {
                OnPropertyChanged(nameof(LastDeletedText));
            }
        }
    }

    public DateTime? LastCleanTime
    {
        get => _lastCleanTime;
        private set
        {
            if (SetProperty(ref _lastCleanTime, value))
            {
                OnPropertyChanged(nameof(LastCleanText));
                OnPropertyChanged(nameof(HasLastClean));
            }
        }
    }

    public bool HasLastClean => LastCleanTime.HasValue;
    public string LastFreedText => Formatters.Bytes(LastFreedBytes);
    public string LastDeletedText => LastDeletedCount == 1 ? "1 file" : $"{LastDeletedCount:N0} files";
    public string LastCleanText => LastCleanTime.HasValue
        ? $"{LastFreedText} freed · {LastCleanTime.Value:HH:mm}"
        : "No cleanup yet";

    public long LiveFreedBytes
    {
        get => _liveFreedBytes;
        private set
        {
            if (SetProperty(ref _liveFreedBytes, value))
            {
                OnPropertyChanged(nameof(LiveFreedText));
            }
        }
    }

    public string LiveFreedText => Formatters.Bytes(LiveFreedBytes);

    public async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsScanning = true;
        Progress = 0;
        StatusText = "Analyzing safe cleanup locations...";
        LiveFreedBytes = 0;

        try
        {
            for (var index = 0; index < Categories.Count; index++)
            {
                var category = Categories[index];
                Progress = index * 100.0 / Categories.Count;
                StatusText = $"Scanning {category.Name.ToLowerInvariant()}...";

                var result = await Task.Run(() => _cleanerService.ScanCategory(category.Definition));
                category.SizeBytes = result.SizeBytes;
                category.ItemCount = result.ItemCount;
                category.IsScanned = true;

                Progress = (index + 1) * 100.0 / Categories.Count;
            }

            StatusText = "Scan complete";
            Progress = 100;
            RaiseCategoryTotals();
            OnPropertyChanged(nameof(HasScanned));
            OnPropertyChanged(nameof(HasItemsToClean));
            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = "Scan could not be completed";
            Failed?.Invoke(this, $"Scan failed: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task CleanSelectedAsync(string context)
    {
        if (IsBusy)
        {
            return;
        }

        var selected = Categories.Where(category => category.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Failed?.Invoke(this, "Select at least one cleanup location first.");
            return;
        }

        if (!HasScanned)
        {
            await ScanAsync();
        }

        IsCleaning = true;
        Progress = 0;
        LiveFreedBytes = 0;
        StatusText = "Preparing cleanup...";
        long totalFreed = 0;
        var totalDeleted = 0;

        try
        {
            var weights = selected
                .Select(category => Math.Max(1L, category.SizeBytes))
                .ToList();
            var totalWeight = (double)weights.Sum();
            double completedWeight = 0;

            for (var index = 0; index < selected.Count; index++)
            {
                var category = selected[index];
                var categoryWeight = weights[index];
                var basePercent = completedWeight / totalWeight * 100;
                var spanPercent = categoryWeight / totalWeight * 100;
                var scanned = new CleanupScanResult(category.SizeBytes, category.ItemCount);
                var categoryFreedStart = totalFreed;

                var reporter = new Progress<CleanupProgress>(progress =>
                {
                    Progress = Math.Min(99.5, basePercent + spanPercent * progress.Percent / 100);
                    StatusText = progress.Message;
                    LiveFreedBytes = categoryFreedStart + progress.FreedBytes;
                });

                var result = await Task.Run(
                    () => _cleanerService.CleanCategory(category.Definition, scanned, reporter));

                totalFreed += result.FreedBytes;
                totalDeleted += result.DeletedCount;
                category.SizeBytes = result.RemainingBytes;
                category.ItemCount = result.RemainingCount;
                category.IsScanned = true;

                completedWeight += categoryWeight;
                Progress = completedWeight / totalWeight * 100;
                LiveFreedBytes = totalFreed;
            }

            LastFreedBytes = totalFreed;
            LastDeletedCount = totalDeleted;
            LastCleanTime = DateTime.Now;
            LiveFreedBytes = totalFreed;
            Progress = 100;
            StatusText = totalFreed > 0
                ? $"Cleanup complete · {Formatters.Bytes(totalFreed)} freed"
                : "Everything is already clean";

            RaiseCategoryTotals();
            OnPropertyChanged(nameof(HasItemsToClean));

            CleanupCompleted?.Invoke(this, new CleanupCompletedEventArgs
            {
                FreedBytes = totalFreed,
                DeletedCount = totalDeleted,
                CompletedAt = LastCleanTime.Value,
                Context = context
            });
        }
        catch (Exception ex)
        {
            StatusText = "Cleanup could not be completed";
            Failed?.Invoke(this, $"Cleanup failed: {ex.Message}");
        }
        finally
        {
            IsCleaning = false;
        }
    }

    private void SelectAll()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = true;
        }
    }

    private void ClearSelection()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = false;
        }
    }

    private void OnCategoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupCategory.IsSelected) or nameof(CleanupCategory.SizeBytes) or nameof(CleanupCategory.IsScanned))
        {
            RaiseCategoryTotals();
        }
    }

    private void RaiseCategoryTotals()
    {
        SelectedBytes = Categories.Where(category => category.IsSelected).Sum(category => category.SizeBytes);
        OnPropertyChanged(nameof(SelectedSummary));
        OnPropertyChanged(nameof(HasItemsToClean));
        CleanCommand.RaiseCanExecuteChanged();
    }

    private void RefreshCommands()
    {
        ScanCommand.RaiseCanExecuteChanged();
        CleanCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
    }
}
