using System.Windows;
using System.Windows.Controls;
using MiniDesk.Models;
using MiniDesk.Services;

namespace MiniDesk;

public partial class AddToGroupWindow : Window
{
    private readonly Func<CategoryGroup, Task> _addToGroup;
    private readonly Func<Task<CategoryGroup?>> _createGroup;

    public AddToGroupWindow(string path, WorkspaceConfig config, Func<CategoryGroup, Task> addToGroup,
        Func<Task<CategoryGroup?>> createGroup)
    {
        InitializeComponent();
        _addToGroup = addToGroup;
        _createGroup = createGroup;
        GroupList.ItemsSource = config.Groups;
        FileNameText.Text = Directory.Exists(path) ? new DirectoryInfo(path).Name : System.IO.Path.GetFileName(path);
        FilePathText.Text = path;
        FileIcon.Source = ShellIconService.GetIcon(path);
    }

    private async void Group_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CategoryGroup group }) return;
        await _addToGroup(group);
        DialogResult = true;
    }

    private async void CreateAndAdd_Click(object sender, RoutedEventArgs e)
    {
        var group = await _createGroup();
        if (group is null) return;
        await _addToGroup(group);
        DialogResult = true;
    }
}
