namespace ContextControl.Workbench.ViewModels;

public sealed class ThemeOptionViewModel(string key, string name, string description)
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public string Description { get; } = description;
}
