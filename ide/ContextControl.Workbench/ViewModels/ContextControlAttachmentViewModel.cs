// CC-DESC: Represents a prompt attachment prepared by the Context Control workflow.

namespace ContextControl.Workbench.ViewModels;

public sealed class ContextControlAttachmentViewModel(string label, string path, string kind) : ObservableObject
{
    private static readonly (string Background, string Border, string Foreground)[] ExtensionPalette =
    [
        ("#203A5A", "#2B5E8C", "#D9ECFF"),
        ("#2E4A2D", "#4D7A46", "#DEFFD9"),
        ("#4A3123", "#7D5238", "#FFE8D9"),
        ("#49304A", "#7A4D7A", "#F6D9FF"),
        ("#2C4748", "#4A7C7F", "#D9FBFF"),
        ("#47332B", "#765747", "#FFEEDF"),
        ("#3B3B2A", "#686845", "#FFFED8")
    ];

    private string _label = label ?? "";
    private string _path = path ?? "";
    private string _kind = kind ?? "";
    private bool _includeInPrompt = IsAutoIncludedKind(kind ?? "");

    public string Label
    {
        get => _label;
        private set
        {
            if (SetProperty(ref _label, value ?? ""))
            {
                OnPropertyChanged(nameof(FileName));
            }
        }
    }

    public string Path
    {
        get => _path;
        private set
        {
            if (SetProperty(ref _path, value ?? ""))
            {
                OnPropertyChanged(nameof(FileName));
            }
        }
    }

    public string Kind
    {
        get => _kind;
        private set => SetProperty(ref _kind, value ?? "");
    }

    public string FileName => string.IsNullOrWhiteSpace(Path)
        ? Label
        : System.IO.Path.GetFileName(Path);

    public string ExtensionTagText => ResolveExtensionKey(Path, Label);

    public string DisplayTitle => string.IsNullOrWhiteSpace(FileName) ? Label : FileName;

    public bool IncludeInPrompt
    {
        get => _includeInPrompt;
        set
        {
            if (SetProperty(ref _includeInPrompt, value))
            {
                OnPropertyChanged(nameof(IncludeLabel));
            }
        }
    }

    public string IncludeLabel => IncludeInPrompt ? "Included" : "Path only";

    public string ExtensionTagBackground => ResolveExtensionPalette(ExtensionTagText).Background;

    public string ExtensionTagBorder => ResolveExtensionPalette(ExtensionTagText).Border;

    public string ExtensionTagForeground => ResolveExtensionPalette(ExtensionTagText).Foreground;

    public void Update(string label, string path, string kind)
    {
        Label = label;
        Path = path;
        Kind = kind;
        IncludeInPrompt = IsAutoIncludedKind(kind);
        OnPropertyChanged(nameof(ExtensionTagText));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ExtensionTagBackground));
        OnPropertyChanged(nameof(ExtensionTagBorder));
        OnPropertyChanged(nameof(ExtensionTagForeground));
    }

    private static string ResolveExtensionKey(string path, string label)
    {
        var extension = System.IO.Path.GetExtension(path ?? "")
            .TrimStart('.')
            .Trim();
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = System.IO.Path.GetExtension(label ?? "")
                .TrimStart('.')
                .Trim();
        }

        if (string.IsNullOrWhiteSpace(extension))
        {
            return "FILE";
        }

        return extension.Length > 6
            ? extension[..6].ToUpperInvariant()
            : extension.ToUpperInvariant();
    }

    private static (string Background, string Border, string Foreground) ResolveExtensionPalette(string extensionKey)
    {
        var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(extensionKey ?? "FILE");
        var index = Math.Abs(hash) % ExtensionPalette.Length;
        return ExtensionPalette[index];
    }

    private static bool IsAutoIncludedKind(string kind)
    {
        return string.Equals(kind, "dir", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "code", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "patch", StringComparison.OrdinalIgnoreCase);
    }
}
