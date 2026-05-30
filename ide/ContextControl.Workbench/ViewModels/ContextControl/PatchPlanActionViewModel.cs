// CC-DESC: Presents one ccReplace patch-plan action with compact diff counts.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class PatchPlanActionViewModel(PatchPlanActionSummary action) : ObservableObject
{
    public string Target { get; } = action.FileLabel;
    public string Part { get; } = action.PartLabel;
    public string Mode { get; } = action.Mode;
    public string Status { get; } = action.StatusLabel;
    public string AddedLabel { get; } = action.AddedLabel;
    public string RemovedLabel { get; } = action.RemovedLabel;
    public string DuplicateAction { get; } = action.DuplicateAction;
    public bool IsDuplicate { get; } = action.IsDuplicate;

    public string Summary => string.IsNullOrWhiteSpace(Part)
        ? $"{Target} [{Status}]"
        : $"{Target} :: {Part} [{Status}]";
}
