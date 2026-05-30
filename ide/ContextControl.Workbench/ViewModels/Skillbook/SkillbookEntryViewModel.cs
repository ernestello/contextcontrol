// CC-DESC: Presents a global or project Skillbook instruction entry.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class SkillbookEntryViewModel(SkillbookEntry entry) : ObservableObject
{
    public string Key { get; } = entry.Key;
    public string Title { get; } = entry.Title;
    public string Text { get; } = entry.Text;
    public string Source { get; } = entry.Source;
    public bool Enabled { get; } = entry.Enabled;
    public string SourceLabel => string.Equals(Source, "project", StringComparison.OrdinalIgnoreCase)
        ? "Project"
        : "Global";
    public string Summary => $"{SourceLabel} - {Text.Length:N0} chars";
}
