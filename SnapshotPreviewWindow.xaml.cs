using System.Windows;
using MiniDesk.Models;

namespace MiniDesk;

public partial class SnapshotPreviewWindow : Window
{
    public SnapshotPreviewWindow(LayoutSnapshot snapshot)
    {
        InitializeComponent();
        DataContext = snapshot;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
