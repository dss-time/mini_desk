using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MiniDesk.Models;
using MiniDesk.Services;

namespace MiniDesk.Controls;

public partial class CreateGroupPanel : UserControl
{
    public sealed record IconOption(string Name, string Glyph, string Accent, ImageSource? Image);
    public event Action<CreateGroupWindow.CreationResult?>? Completed;
    private Task? _preparation;
    private string? _uploadPath;
    private bool _uploadTab;
    private string? _preparedCulture;
    public bool IsPrepared { get; private set; }

    public CreateGroupPanel() => InitializeComponent();

    public Task PrepareAsync() => _preparation ??= PrepareCoreAsync();
    private async Task PrepareCoreAsync()
    {
        try { await GroupSystemIconService.WarmAsync(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        // Cached reads only: neither opening nor selecting an icon can enter Windows Shell.
        PopulateChoices();
        IsPrepared = true;
        PreparingHint.Visibility = Visibility.Collapsed;
        RefreshAction();
    }

    public void Reset()
    {
        if (IsPrepared && _preparedCulture != LocalizationService.Current.Culture.Name) PopulateChoices();
        NameInput.Clear();
        DisplayState.SelectedIndex = 0;
        ShowOnDesktop.IsChecked = true;
        _uploadPath = null;
        UploadedImage.Source = null;
        UploadTitle.SetResourceReference(TextBlock.TextProperty, "Create_UploadDrop");
        ValidationMessage.Visibility = Visibility.Collapsed;
        if (IsPrepared) IconList.SelectedIndex = 0;
        System_Click(this, new RoutedEventArgs());
        RefreshAction();
    }

    private void PopulateChoices()
    {
        IconList.ItemsSource = CreateGroupWindow.SystemIconChoices().Select(c =>
            new IconOption(c.Name, c.Glyph, c.Accent, GroupSystemIconService.GetCachedIcon(c.Glyph))).ToArray();
        IconList.SelectedIndex = 0;
        _preparedCulture = LocalizationService.Current.Culture.Name;
    }

    public void FocusName() { NameInput.Focus(); Keyboard.Focus(NameInput); }
    private void Name_Changed(object sender, TextChangedEventArgs e)
    {
        if (NameHint is null) return;
        NameHint.Visibility = string.IsNullOrEmpty(NameInput.Text) ? Visibility.Visible : Visibility.Collapsed;
        RefreshAction();
    }
    private void Icon_Changed(object sender, SelectionChangedEventArgs e) { _uploadPath = null; RefreshAction(); }
    private void RefreshAction()
    {
        if (CreateAction is not null)
            CreateAction.IsEnabled = !string.IsNullOrWhiteSpace(NameInput.Text) &&
                (_uploadTab ? _uploadPath is not null : IconList.SelectedItem is IconOption);
    }
    private void System_Click(object sender, RoutedEventArgs e)
    {
        _uploadTab = false;
        IconList.Visibility = Visibility.Visible;
        UploadArea.Visibility = Visibility.Collapsed;
        SoftBorder.SetSelected(SystemIconsButton, true);
        SoftBorder.SetSelected(UploadIconButton, false);
        RefreshAction();
    }
    private void Upload_Click(object sender, RoutedEventArgs e)
    {
        _uploadTab = true;
        IconList.Visibility = Visibility.Collapsed;
        PreparingHint.Visibility = Visibility.Collapsed;
        UploadArea.Visibility = Visibility.Visible;
        SoftBorder.SetSelected(SystemIconsButton, false);
        SoftBorder.SetSelected(UploadIconButton, true);
        RefreshAction();
    }
    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = LocalizationService.Current.Get("Upload_SelectTitle"), Filter = LocalizationService.Current.Get("Upload_Filter") };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) SetUploadedFile(picker.FileName);
    }
    internal bool SetUploadedFile(string path)
    {
        if (!GroupIconStorageService.TryValidate(path, out var error))
        {
            _uploadPath = null;
            UploadedImage.Source = null;
            ValidationMessage.Text = error;
            ValidationMessage.Visibility = Visibility.Visible;
            RefreshAction();
            return false;
        }
        var image = GroupIconStorageService.LoadPreview(path);
        if (image is null) return false;
        _uploadPath = path;
        UploadedImage.Source = image;
        UploadTitle.Text = Path.GetFileName(path);
        ValidationMessage.Visibility = Visibility.Collapsed;
        RefreshAction();
        return true;
    }
    private void Upload_DragOver(object sender, DragEventArgs e)
    { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Upload_Drop(object sender, DragEventArgs e)
    { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) SetUploadedFile(files[0]); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Completed?.Invoke(null);
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!CreateAction.IsEnabled) return;
        var option = IconList.SelectedItem as IconOption;
        var state = (DisplayState.SelectedItem as ComboBoxItem)?.Tag as string;
        Completed?.Invoke(new CreateGroupWindow.CreationResult(NameInput.Text.Trim(), option?.Glyph ?? "\uE8B7", option?.Accent ?? "#5D4AD9",
            _uploadTab ? _uploadPath : null, state == "Expanded" ? GroupDisplayState.Expanded : GroupDisplayState.Collapsed,
            state == "IconOnly" ? CollapseStyle.IconOnly : CollapseStyle.HorizontalCapsule, ShowOnDesktop.IsChecked == true));
    }
    private void Panel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Completed?.Invoke(null); }
        else if (e.Key == Key.Enter && !DisplayState.IsDropDownOpen) { e.Handled = true; Create_Click(sender, e); }
    }
}
