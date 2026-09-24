namespace MiniDesk.Models;

public enum DesktopVisibilityMode { None, HiddenAttribute, ManagedShortcut }

public sealed class DesktopItem : ObservableObject
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private double _x;
    private double _y;
    private DesktopVisibilityMode _visibilityMode;
    private string? _originalDesktopPath;
    private int _originalFileAttributes;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Path { get => _path; set => SetProperty(ref _path, value); }
    public double X { get => _x; set => SetProperty(ref _x, value); }
    public double Y { get => _y; set => SetProperty(ref _y, value); }
    public DesktopVisibilityMode VisibilityMode { get => _visibilityMode; set => SetProperty(ref _visibilityMode, value); }
    public string? OriginalDesktopPath { get => _originalDesktopPath; set => SetProperty(ref _originalDesktopPath, value); }
    public int OriginalFileAttributes { get => _originalFileAttributes; set => SetProperty(ref _originalFileAttributes, value); }
}
