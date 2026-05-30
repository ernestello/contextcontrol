// CC-DESC: Tracks one in-flight local chat request for prompt progress rows.

using Avalonia.Threading;

namespace ContextControl.Workbench.ViewModels;

public sealed class ChatRequestProgressViewModel : ObservableObject
{
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly DispatcherTimer _elapsedTimer;
    private string _status = "Loading model...";
    private string _sizeLabel = "0 output tok";
    private string _speedLabel = "speed pending";
    private string _elapsedLabel = "0.0s";
    private double _value;
    private bool _isIndeterminate = true;

    public ChatRequestProgressViewModel(string sessionId, string title)
    {
        SessionId = sessionId ?? "";
        Title = title ?? "";
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _elapsedTimer.Tick += (_, _) => RefreshElapsed();
        RefreshElapsed();
        _elapsedTimer.Start();
    }

    public string SessionId { get; }

    public string Title { get; }

    public double ElapsedSeconds => Math.Max(0, (DateTime.UtcNow - _startedUtc).TotalSeconds);

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

    public void RefreshElapsed()
    {
        ElapsedLabel = FormatElapsed(ElapsedSeconds);
        if (SpeedLabel.StartsWith("speed pending", StringComparison.OrdinalIgnoreCase)
            || SpeedLabel.StartsWith("pending ", StringComparison.OrdinalIgnoreCase))
        {
            SpeedLabel = $"pending {ElapsedLabel}";
        }
    }

    public void StopElapsedTimer()
    {
        RefreshElapsed();
        _elapsedTimer.Stop();
    }

    private static string FormatElapsed(double seconds)
    {
        return seconds < 60
            ? $"{seconds:0.0}s"
            : $"{(int)(seconds / 60)}m {seconds % 60:00.0}s";
    }
}
