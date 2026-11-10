using System.Windows.Threading;
using PCCare.Helpers;
using PCCare.Models;
using PCCare.Services;

namespace PCCare.ViewModels;

public sealed class PerformanceViewModel : ObservableObject
{
    private readonly PerformanceService _performanceService;
    private readonly DispatcherTimer _timer;
    private double _cpuPercent;
    private double _ramPercent;
    private double _diskPercent;
    private long _totalRamBytes;
    private long _usedRamBytes;
    private long _diskTotalBytes;
    private long _diskFreeBytes;
    private string _cpuName = "Detecting processor...";
    private int _logicalCores;
    private string _osName = "Windows";
    private string _uptimeText = "—";

    public PerformanceViewModel(PerformanceService performanceService)
    {
        _performanceService = performanceService;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _timer.Tick += (_, _) => Refresh();
    }

    public event EventHandler? SnapshotUpdated;

    public double CpuPercent
    {
        get => _cpuPercent;
        private set
        {
            if (SetProperty(ref _cpuPercent, value))
            {
                OnPropertyChanged(nameof(CpuPercentText));
                OnPropertyChanged(nameof(CpuDetailText));
            }
        }
    }

    public double RamPercent
    {
        get => _ramPercent;
        private set
        {
            if (SetProperty(ref _ramPercent, value))
            {
                OnPropertyChanged(nameof(RamPercentText));
                OnPropertyChanged(nameof(RamDetailText));
            }
        }
    }

    public double DiskPercent
    {
        get => _diskPercent;
        private set
        {
            if (SetProperty(ref _diskPercent, value))
            {
                OnPropertyChanged(nameof(DiskPercentText));
                OnPropertyChanged(nameof(DiskDetailText));
                OnPropertyChanged(nameof(DiskFreePercent));
            }
        }
    }

    public long TotalRamBytes
    {
        get => _totalRamBytes;
        private set => SetProperty(ref _totalRamBytes, value);
    }

    public long UsedRamBytes
    {
        get => _usedRamBytes;
        private set => SetProperty(ref _usedRamBytes, value);
    }

    public long DiskTotalBytes
    {
        get => _diskTotalBytes;
        private set
        {
            if (SetProperty(ref _diskTotalBytes, value))
            {
                OnPropertyChanged(nameof(DiskDetailText));
                OnPropertyChanged(nameof(DiskTotalText));
            }
        }
    }

    public long DiskFreeBytes
    {
        get => _diskFreeBytes;
        private set
        {
            if (SetProperty(ref _diskFreeBytes, value))
            {
                OnPropertyChanged(nameof(DiskDetailText));
                OnPropertyChanged(nameof(DiskFreeText));
            }
        }
    }

    public string CpuName
    {
        get => _cpuName;
        private set => SetProperty(ref _cpuName, value);
    }

    public int LogicalCores
    {
        get => _logicalCores;
        private set
        {
            if (SetProperty(ref _logicalCores, value))
            {
                OnPropertyChanged(nameof(LogicalCoresText));
            }
        }
    }

    public string OsName
    {
        get => _osName;
        private set => SetProperty(ref _osName, value);
    }

    public string UptimeText
    {
        get => _uptimeText;
        private set => SetProperty(ref _uptimeText, value);
    }

    public string CpuPercentText => $"{CpuPercent:0}%";
    public string RamPercentText => $"{RamPercent:0}%";
    public string DiskPercentText => $"{DiskPercent:0}%";
    public string CpuDetailText => $"{Environment.ProcessorCount} logical processors";
    public string RamDetailText => $"{Formatters.Bytes(UsedRamBytes)} of {Formatters.Bytes(TotalRamBytes)}";
    public string DiskDetailText => $"{Formatters.Bytes(DiskFreeBytes)} free of {Formatters.Bytes(DiskTotalBytes)}";
    public string DiskTotalText => Formatters.Bytes(DiskTotalBytes);
    public string DiskFreeText => Formatters.Bytes(DiskFreeBytes);
    public double DiskFreePercent => Math.Max(0, 100 - DiskPercent);
    public string LogicalCoresText => $"{LogicalCores} threads";

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void Refresh()
    {
        try
        {
            Apply(_performanceService.GetSnapshot());
        }
        catch
        {
            // A temporary WMI/kernel issue should not interrupt the dashboard.
        }
    }

    private void Apply(PerformanceSnapshot snapshot)
    {
        CpuPercent = snapshot.CpuPercent;
        RamPercent = snapshot.RamPercent;
        DiskPercent = snapshot.DiskUsedPercent;
        TotalRamBytes = snapshot.TotalRamBytes;
        UsedRamBytes = snapshot.UsedRamBytes;
        DiskTotalBytes = snapshot.DiskTotalBytes;
        DiskFreeBytes = snapshot.DiskFreeBytes;
        CpuName = snapshot.CpuName;
        LogicalCores = snapshot.LogicalCores;
        OsName = snapshot.OsName;
        UptimeText = Formatters.Uptime(snapshot.Uptime);
        SnapshotUpdated?.Invoke(this, EventArgs.Empty);
    }
}
