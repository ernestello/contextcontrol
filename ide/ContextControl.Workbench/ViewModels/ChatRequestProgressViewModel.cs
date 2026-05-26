// CC-DESC: Tracks one in-flight local chat request for prompt progress rows.

namespace ContextControl.Workbench.ViewModels;

public sealed class ChatRequestProgressViewModel(string sessionId, string title) : ObservableObject
{
    private string _status = "Loading model...";
    private string _sizeLabel = "0 output tok";
    private string _speedLabel = "speed pending";
    private string _elapsedLabel = "";
    private double _value;
    private bool _isIndeterminate = true;

    public string SessionId { get; } = sessionId ?? "";

    public string Title { get; } = title ?? "";

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value ?? "");
    }

    public string SizeLabel
    {
        get => _sizeLabel;
        set => SetProperty(ref _sizeLabel, value ?? "");
    }

    public string SpeedLabel
    {
        get => _speedLabel;
        set => SetProperty(ref _speedLabel, value ?? "");
    }

    public string ElapsedLabel
    {
        get => _elapsedLabel;
        set => SetProperty(ref _elapsedLabel, value ?? "");
    }

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }
}
