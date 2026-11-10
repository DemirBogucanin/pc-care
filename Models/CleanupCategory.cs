using PCCare.Helpers;
using PCCare.ViewModels;

namespace PCCare.Models;

public sealed class CleanupCategory : ObservableObject
{
    private bool _isSelected;
    private long _sizeBytes;
    private int _itemCount;
    private bool _isScanned;

    public CleanupCategory(CleanupDefinition definition)
    {
        Definition = definition;
        _isSelected = definition.SelectedByDefault;
    }

    public CleanupDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;
    public string Glyph => Definition.Glyph;
    public string AccentHex => Definition.AccentHex;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public long SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (SetProperty(ref _sizeBytes, value))
            {
                OnPropertyChanged(nameof(SizeText));
            }
        }
    }

    public int ItemCount
    {
        get => _itemCount;
        set
        {
            if (SetProperty(ref _itemCount, value))
            {
                OnPropertyChanged(nameof(ItemCountText));
            }
        }
    }

    public bool IsScanned
    {
        get => _isScanned;
        set
        {
            if (SetProperty(ref _isScanned, value))
            {
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(ItemCountText));
            }
        }
    }

    public string SizeText => IsScanned ? Formatters.Bytes(SizeBytes) : "—";
    public string ItemCountText => !IsScanned ? "Not scanned" : ItemCount == 1 ? "1 item" : $"{ItemCount:N0} items";
}
