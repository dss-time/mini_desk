using System.Windows;

namespace MiniDesk;

public partial class TextPromptWindow : Window
{
    public string Result => Input.Text;

    public TextPromptWindow(string title, string label, string value)
    {
        InitializeComponent();
        Heading.Text = title;
        Label.Text = label;
        Input.Text = value;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Input.Text)) return;
        DialogResult = true;
    }
}
