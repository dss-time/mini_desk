using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace MiniDesk.Models;

public enum GroupDisplayState { Expanded, Collapsed, Minimized }
public enum CollapseStyle { HorizontalCapsule, VerticalIcon, IconOnly }

public sealed class CategoryGroup : ObservableObject
{
    private string _name = string.Empty;
    private string _glyph = "\uE7C3";
    private string _accent = "#1769F7";
    private string _iconPath = string.Empty;
    private double _left = 180;
    private double _top = 120;
    private double _width = 400;
    private double _height = 260;
    private GroupDisplayState _state;
    private CollapseStyle _collapseStyle = CollapseStyle.HorizontalCapsule;
    private bool _isEnabled = true;
    private string _monitorDeviceName = string.Empty;
    private double? _monitorOffsetX;
    private double? _monitorOffsetY;
    private double _savedDpiX = 96;
    private double _savedDpiY = 96;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Glyph { get => _glyph; set => SetProperty(ref _glyph, value); }
    public string Accent { get => _accent; set => SetProperty(ref _accent, value); }
    public string IconPath { get => _iconPath; set => SetProperty(ref _iconPath, value); }
    public double Left { get => _left; set => SetProperty(ref _left, value); }
    public double Top { get => _top; set => SetProperty(ref _top, value); }
    public double Width { get => _width; set => SetProperty(ref _width, value); }
    public double Height { get => _height; set => SetProperty(ref _height, value); }
    public GroupDisplayState State
    {
        get => _state;
        set { if (SetProperty(ref _state, value)) Notify(nameof(StateIndex)); }
    }
    [JsonIgnore]
    public int StateIndex { get => (int)State; set => State = (GroupDisplayState)value; }
    public CollapseStyle CollapseStyle { get => _collapseStyle; set => SetProperty(ref _collapseStyle, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public string MonitorDeviceName { get => _monitorDeviceName; set => SetProperty(ref _monitorDeviceName, value); }
    // Monitor-local device-independent coordinates. Left/Top remain serialized for
    // backward compatibility with configurations created before schema version 5.
    public double? MonitorOffsetX { get => _monitorOffsetX; set => SetProperty(ref _monitorOffsetX, value); }
    public double? MonitorOffsetY { get => _monitorOffsetY; set => SetProperty(ref _monitorOffsetY, value); }
    public double SavedDpiX { get => _savedDpiX; set => SetProperty(ref _savedDpiX, value); }
    public double SavedDpiY { get => _savedDpiY; set => SetProperty(ref _savedDpiY, value); }
    public ObservableCollection<DesktopItem> Items { get; set; } = [];
    public int ItemCount => Items.Count;
    public void NotifyCount() => Notify(nameof(ItemCount));
}
