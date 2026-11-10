using PCCare.ViewModels;

namespace PCCare.Models;

public enum StartupSourceKind
{
    RegistryRun,
    StartupFolder
}

public sealed class StartupEntry : ObservableObject
{
    private bool _isEnabled;
    private bool _isBusy;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Location { get; init; }
    public required string AccentHex { get; init; }
    public required StartupSourceKind SourceKind { get; init; }

    public string? RegistryHive { get; init; }
    public string? RegistryKeyPath { get; init; }
    public string? RegistryValueName { get; init; }
    public bool Registry32Bit { get; init; }
    public string? FilePath { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string Initial
    {
        get
        {
            var trimmed = Name.Trim();
            return trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : "?";
        }
    }
}
