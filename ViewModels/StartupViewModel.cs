using System.Collections.ObjectModel;
using PCCare.Models;
using PCCare.Services;

namespace PCCare.ViewModels;

public sealed class StartupViewModel : ObservableObject
{
    private readonly StartupService _startupService;
    private bool _isLoading;
    private bool _isChanging;
    private bool _hasLoaded;
    private int _enabledCount;
    private int _totalCount;

    public StartupViewModel(StartupService startupService)
    {
        _startupService = startupService;
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading && !IsChanging);
        ToggleStartupCommand = new AsyncRelayCommand(ToggleAsync, _ => !IsLoading && !IsChanging);
    }

    public ObservableCollection<StartupEntry> Entries { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ToggleStartupCommand { get; }

    public event EventHandler<string>? Failed;
    public event EventHandler<string>? Changed;
    public event EventHandler? EntriesUpdated;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RefreshCommands();
            }
        }
    }

    public bool IsChanging
    {
        get => _isChanging;
        private set
        {
            if (SetProperty(ref _isChanging, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                RefreshCommands();
            }
        }
    }

    public bool IsBusy => IsLoading || IsChanging;

    public bool HasLoaded
    {
        get => _hasLoaded;
        private set
        {
            if (SetProperty(ref _hasLoaded, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool IsEmpty => HasLoaded && !HasEntries;

    public int EnabledCount
    {
        get => _enabledCount;
        private set
        {
            if (SetProperty(ref _enabledCount, value))
            {
                OnPropertyChanged(nameof(EnabledCountText));
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(TotalCountText));
                OnPropertyChanged(nameof(HasEntries));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public bool HasEntries => TotalCount > 0;
    public string EnabledCountText => EnabledCount.ToString();
    public string TotalCountText => TotalCount.ToString();
    public string SummaryText => TotalCount == 0
        ? "No startup apps found"
        : $"{EnabledCount} of {TotalCount} apps start with Windows";

    public async Task LoadAsync()
    {
        if (IsLoading || IsChanging)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var entries = await Task.Run(() => _startupService.LoadStartupEntries());
            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }

            TotalCount = Entries.Count;
            EnabledCount = Entries.Count(entry => entry.IsEnabled);
            HasLoaded = true;
            EntriesUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            HasLoaded = true;
            Failed?.Invoke(this, $"Startup apps could not be loaded: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ToggleAsync(object? parameter)
    {
        if (parameter is not StartupEntry entry || entry.IsBusy)
        {
            return;
        }

        IsChanging = true;
        entry.IsBusy = true;
        var targetState = entry.IsEnabled;

        try
        {
            await Task.Run(() => _startupService.SetEnabled(entry, targetState));
            Changed?.Invoke(
                this,
                targetState
                    ? $"{entry.Name} will start with Windows"
                    : $"{entry.Name} will no longer start with Windows");
            await ReloadSilentlyAsync();
        }
        catch (Exception ex)
        {
            entry.IsEnabled = !targetState;
            Failed?.Invoke(this, ex.Message);
        }
        finally
        {
            entry.IsBusy = false;
            IsChanging = false;
        }
    }

    private async Task ReloadSilentlyAsync()
    {
        var entries = await Task.Run(() => _startupService.LoadStartupEntries());
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }

        TotalCount = Entries.Count;
        EnabledCount = Entries.Count(entry => entry.IsEnabled);
        HasLoaded = true;
        EntriesUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshCommands()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        ToggleStartupCommand.RaiseCanExecuteChanged();
    }
}
