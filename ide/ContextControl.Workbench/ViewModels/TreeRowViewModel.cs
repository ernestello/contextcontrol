using Avalonia;

namespace ContextControl.Workbench.ViewModels;

public sealed class TreeRowViewModel : ObservableObject
{
    private const double RailStep = 9.0;
    private const double RailInset = 6.0;
    private const double BranchTail = 12.0;
    private const double ToggleSize = 11.0;

    private TreeRowViewModel(
        ProjectNodeViewModel? node,
        string connectorText)
    {
        Node = node;
        ConnectorText = connectorText;
    }

    private bool _isSelected;

    public ProjectNodeViewModel? Node { get; }
    public string ConnectorText { get; }
    public string ConnectorDisplayText => ConnectorText.Replace(" ", "\u00A0");
    public bool IsSpacer => false;
    public bool HasNode => Node is not null;
    public bool IsFolder => Node?.IsFolder == true;
    public bool IsFile => Node?.IsFile == true;
    public bool IsExternal => Node?.IsExternal == true;
    public bool IsCurrent => Node?.IsCurrent == true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(ShowCurrentHighlight));
            }
        }
    }

    public bool ShowCurrentHighlight => IsCurrent && !IsSelected;
    public bool IsRegularFolder => Node?.IsRegularFolder == true;
    public bool HasChildren => Node?.HasChildren == true;
    public bool HasExpandedChildren => Node?.HasExpandedChildren == true;
    public bool ShowDisclosure => HasNode && HasChildren;
    public bool CanIncludeExternal => Node?.CanIncludeExternal == true;
    public int Depth => Node?.Depth ?? 0;
    public bool IsLast => Node?.IsLast == true;
    public IReadOnlyList<bool> AncestorContinues => Node?.AncestorContinues ?? [];
    public string NodeBadgeText => Node?.NodeBadgeText ?? "";
    public string FileCountLabel => Node?.FileCountLabel ?? "";
    public string DirectoryStatsLabel => Node?.DirectoryStatsLabel ?? "";
    public bool ShowFileCountLabel => HasNode && IsRegularFolder && !string.IsNullOrWhiteSpace(FileCountLabel);
    public string LocMetricLabel => Node?.LocMetricLabel ?? "";
    public bool ShowLocMetricLabel => HasNode && !IsExternal && !string.IsNullOrWhiteSpace(LocMetricLabel);
    public string NodeTypeLabel => Node?.NodeRoleLabel ?? "";
    public bool ShowNodeTypeLabel => HasNode && IsExternal && !string.IsNullOrWhiteSpace(NodeTypeLabel) && !CanIncludeExternal;
    public string DisplayName => Node?.DisplayName ?? "";
    public string VersionLabel => Node?.VersionLabel ?? "";
    public bool ShowVersionLabel => HasNode && IsFile && !IsExternal && !CanIncludeExternal;
    public double RowHeight => IsFile ? 15.5 : 17.0;
    public double RailWidth => RailInset + Math.Max(0, Depth) * RailStep + BranchTail;
    public double ArrowAngle => Node?.ArrowAngle ?? 0.0;
    public Thickness ToggleMargin => new(Math.Max(0, RailInset + Math.Max(0, Depth) * RailStep - ToggleSize * 0.5), 0, 0, 0);

    public static TreeRowViewModel ForNode(ProjectNodeViewModel node)
    {
        return new TreeRowViewModel(node, node.ConnectorText);
    }

    public void RefreshCurrentState()
    {
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(ShowCurrentHighlight));
    }

    public void RefreshNodeMetrics()
    {
        OnPropertyChanged(nameof(IsExternal));
        OnPropertyChanged(nameof(IsRegularFolder));
        OnPropertyChanged(nameof(CanIncludeExternal));
        OnPropertyChanged(nameof(FileCountLabel));
        OnPropertyChanged(nameof(ShowFileCountLabel));
        OnPropertyChanged(nameof(LocMetricLabel));
        OnPropertyChanged(nameof(ShowLocMetricLabel));
        OnPropertyChanged(nameof(DirectoryStatsLabel));
        OnPropertyChanged(nameof(NodeTypeLabel));
        OnPropertyChanged(nameof(ShowNodeTypeLabel));
        OnPropertyChanged(nameof(VersionLabel));
        OnPropertyChanged(nameof(ShowVersionLabel));
    }

    public void RefreshExpansionState()
    {
        OnPropertyChanged(nameof(HasChildren));
        OnPropertyChanged(nameof(ShowDisclosure));
        OnPropertyChanged(nameof(HasExpandedChildren));
        OnPropertyChanged(nameof(ArrowAngle));
    }
}
