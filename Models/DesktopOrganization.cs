namespace MiniDesk.Models;

public sealed record OrganizationTarget(string Key, string Name, string Glyph, string Accent, Guid? GroupId = null)
{
    public override string ToString() => Name;
}

public sealed class OrganizationCandidate : ObservableObject
{
    private OrganizationTarget? _target;
    public required string Path { get; init; }
    public required string Name { get; init; }
    public OrganizationTarget? Target { get => _target; set => SetProperty(ref _target, value); }
}

// Additive, local undo journal. Pending restores are retried before desktop reconciliation.
public sealed class OrganizationBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<OrganizationEntry> Entries { get; set; } = [];
}

public sealed class OrganizationEntry
{
    public Guid GroupId { get; set; }
    public Guid ItemId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public bool RestorePending { get; set; }
}
