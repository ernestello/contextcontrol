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
    public string SourceLabel => Source.ToLowerInvariant() switch
    {
        "project" => "Project",
        "codex" => "Codex",
        "skillflow" => "Skillflow",
        _ => "Global"
    };
    public int SourceRank => Source.ToLowerInvariant() switch
    {
        "codex" => 0,
        "skillflow" => 1,
        "project" => 2,
        "global" => 3,
        _ => 4
    };
    public string SectionTitle => Source.ToLowerInvariant() switch
    {
        "codex" => "Codex Instructions",
        "skillflow" => "Skillflow",
        "project" => "Project Skillbook",
        "global" => "Global Skillbook",
        _ => "Skillbook"
    };
    public string Summary => $"{SourceLabel} - {Text.Length:N0} chars";
}
