using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MiniDesk.Models;
using MiniDesk.Services;

namespace MiniDesk;

public partial class CreateGroupWindow : Window
{
    public sealed record IconChoice(string Name, string Glyph, string Accent);
    public sealed record CreationResult(string Name, string Glyph, string Accent, string? UploadPath,
        GroupDisplayState State, CollapseStyle CollapseStyle, bool ShowOnDesktop);

    private bool _useUpload;
    private string? _uploadPath;
    public CreationResult? Result { get; private set; }

    public CreateGroupWindow()
    {
        InitializeComponent();
        var l = LocalizationService.Current;
        IconChoices.ItemsSource = new[]
        {
            new IconChoice(l.Get("Default_Music"), "\uE8D6", "#F54474"),
            new IconChoice(l.Get("Default_Development"), "\uE943", "#2675F5"),
            new IconChoice(l.Get("Default_Communication"), "\uE8BD", "#28B978"),
            new IconChoice(l.Get("Default_Office"), "\uE821", "#5D62F4"),
            new IconChoice(l.Get("Default_TemporaryFiles"), "\uE8B7", "#F2B84B"),
            new IconChoice(l.Get("Icon_Browser"), "\uE774", "#2675F5"),
            new IconChoice(l.Get("Icon_Game"), "\uE7FC", "#6F5BF5"),
            new IconChoice(l.Get("Icon_Picture"), "\uEB9F", "#F05A70"),
            new IconChoice(l.Get("Icon_Document"), "\uE8A5", "#1FB0DA"),
            new IconChoice(l.Get("Icon_Download"), "\uE896", "#20B878"),
            new IconChoice(l.Get("Icon_Favorite"), "\uE734", "#FF7545"),
            new IconChoice(l.Get("Icon_Custom"), "\uE712", "#8A96A8")
        };
        IconChoices.SelectedIndex = 0;
    }

    private IconChoice SelectedIcon => IconChoices.SelectedItem as IconChoice ??
        new IconChoice(LocalizationService.Current.Get("Icon_Custom"), "\uE712", "#1769F7");

    private void GroupNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CreateButton is null) return;
        NamePlaceholder.Visibility = string.IsNullOrEmpty(GroupNameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(GroupNameBox.Text);
        UpdatePreview();
    }

    private void IconChoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IconChoices.SelectedItem is null) return;
        _useUpload = false;
        UpdatePreview();
    }

    private void SystemTab_Click(object sender, RoutedEventArgs e)
    {
        IconChoices.Visibility = Visibility.Visible;
        UploadPanel.Visibility = Visibility.Collapsed;
        SystemTabIndicator.Background = Brushes.White;
        UploadTabIndicator.Background = Brushes.Transparent;
        SetTabColors(true);
        _useUpload = false;
        UpdatePreview();
    }

    private void UploadTab_Click(object sender, RoutedEventArgs e)
    {
        IconChoices.Visibility = Visibility.Collapsed;
        UploadPanel.Visibility = Visibility.Visible;
        SystemTabIndicator.Background = Brushes.Transparent;
        UploadTabIndicator.Background = Brushes.White;
        SetTabColors(false);
        _useUpload = !string.IsNullOrWhiteSpace(_uploadPath);
        UpdatePreview();
    }

    private void SetTabColors(bool systemSelected)
    {
        foreach (var text in FindVisualChildren<TextBlock>(SystemTabButton))
            text.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(systemSelected ? "#1769F7" : "#526681"));
        foreach (var text in FindVisualChildren<TextBlock>(UploadTabButton))
            text.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(systemSelected ? "#526681" : "#1769F7"));
    }

    private void ChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Current.Get("Upload_SelectTitle"),
            Filter = LocalizationService.Current.Get("Upload_Filter"),
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) SetUploadedFile(dialog.FileName);
    }

    private void UploadPanel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void UploadPanel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) SetUploadedFile(paths[0]);
    }

    internal bool SetUploadedFile(string path)
    {
        if (!GroupIconStorageService.TryValidate(path, out var error))
        {
            UploadError.Text = error;
            UploadFileName.Text = string.Empty;
            return false;
        }
        var preview = GroupIconStorageService.LoadPreview(path);
        if (preview is null)
        {
            UploadError.Text = LocalizationService.Current.Get("Upload_PreviewFailed");
            return false;
        }
        _uploadPath = path;
        _useUpload = true;
        UploadPreviewImage.Source = preview;
        UploadEmptyGlyph.Visibility = Visibility.Collapsed;
        UploadFileName.Text = Path.GetFileName(path);
        UploadError.Text = string.Empty;
        UpdatePreview();
        return true;
    }

    private void DefaultStateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (!IsInitialized) return;
        var choice = SelectedIcon;
        PreviewName.Text = string.IsNullOrWhiteSpace(GroupNameBox.Text) ? choice.Name : GroupNameBox.Text.Trim();
        PreviewGlyph.Text = choice.Glyph;
        PreviewGlyph.Tag = choice.Accent;
        PreviewImage.Source = _useUpload && _uploadPath is not null ? GroupIconStorageService.LoadPreview(_uploadPath) : null;
        PreviewGlyph.Visibility = PreviewImage.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = GroupNameBox.Text.Trim();
        if (name.Length == 0) return;
        var choice = SelectedIcon;
        var stateTag = (DefaultStateCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var state = stateTag == "Expanded" ? GroupDisplayState.Expanded : GroupDisplayState.Collapsed;
        var style = stateTag == "IconOnly" ? CollapseStyle.IconOnly : CollapseStyle.HorizontalCapsule;
        Result = new CreationResult(name, choice.Glyph, choice.Accent,
            _useUpload ? _uploadPath : null, state, style, ShowOnDesktopCheck.IsChecked == true);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
