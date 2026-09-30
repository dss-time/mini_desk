using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiniDesk.Models;
using MiniDesk.Services;

namespace MiniDesk;

public partial class OrganizeDesktopWindow : Window
{
    public List<OrganizationCandidate> Candidates { get; }
    private sealed record Row(OrganizationCandidate Candidate, List<OrganizationTarget> Targets)
    {
        public ImageSource? Icon => ShellIconService.GetIcon(Candidate.Path);
    }
    public OrganizeDesktopWindow(WorkspaceConfig config, List<OrganizationCandidate> candidates)
    {
        InitializeComponent();
        Candidates = candidates;
        var targets = DesktopOrganizationService.Targets(config).Where(t => t.Key == "existing" || t.GroupId is null).ToList();
        // An explicit desktop destination avoids a blank ComboBox for unrecognized items.
        var desktop = new OrganizationTarget("desktop", LocalizationService.Current.Get("Organize_Desktop"), "", "");
        targets.Insert(0, desktop);
        foreach (var item in candidates)
            item.Target = item.Target is null ? desktop : targets.FirstOrDefault(t => item.Target.GroupId is not null ? t.GroupId == item.Target.GroupId : t.Key == item.Target.Key) ?? desktop;
        Items.ItemsSource = candidates.Select(c => new Row(c, targets)).ToList();
        EmptyHint.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            MaxWidth = area.Width * .94; MaxHeight = area.Height * .9;
            MinWidth = Math.Min(MinWidth, MaxWidth); MinHeight = Math.Min(MinHeight, MaxHeight);
            Width = Math.Min(Width, MaxWidth); Height = Math.Min(Height, MaxHeight);
            UpdateSummary();
        };
    }
    private void TargetChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) UpdateSummary(); }
    private void UpdateSummary()
    {
        int selected = Candidates.Count(c => c.Target is { Key: not "desktop" });
        Summary.Text = LocalizationService.Current.Format("Organize_Count", selected, Candidates.Count - selected);
        ApplyButton.IsEnabled = selected > 0;
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        foreach (var candidate in Candidates.Where(c => c.Target?.Key == "desktop")) candidate.Target = null;
        DialogResult = true;
    }
}
