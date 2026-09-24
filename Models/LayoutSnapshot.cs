namespace MiniDesk.Models;

public sealed class LayoutSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public bool IsAutomatic { get; set; }
    public WorkspaceConfig Layout { get; set; } = new();
    public int GroupCount => Layout.Groups.Count;
    public int ItemCount => Layout.Groups.Sum(group => group.Items.Count);
}
