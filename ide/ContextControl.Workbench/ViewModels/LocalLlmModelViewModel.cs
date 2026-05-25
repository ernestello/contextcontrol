// CC-DESC: Presents a local Ollama model candidate with fit, install, and pull state.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class LocalLlmModelViewModel(LocalLlmCatalogModel model) : ObservableObject
{
    private bool _isInstalled;
    private bool _isRecommended;
    private bool _isPulling;
    private bool _hasDetectedThinking;
    private string _fitLabel = "Detecting fit";
    private string _fitDetail = "";

    public LocalLlmCatalogModel Model { get; } = model;
    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;
    public string DownloadSize => Model.DownloadSize;
    public string License => Model.License;
    public string MinimumRequirement => Model.MinimumRequirement;
    public string AdvertisedContext => Model.AdvertisedContext;
    public string ComfortableContext => Model.ComfortableContext;
    public string SourceBudget => Model.SourceBudget;
    public string ExpectedSpeed => Model.ExpectedSpeed;
    public string PracticalUse => Model.PracticalUse;
    public string PullCommand => Model.PullCommand;
    public bool SupportsThinking => Id.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
        || Id.Contains("think", StringComparison.OrdinalIgnoreCase)
        || _hasDetectedThinking;
    public string ThinkingLabel => SupportsThinking ? "Thinking" : "No think tag";

    public bool IsInstalled
    {
        get => _isInstalled;
        private set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(InstallLabel));
                OnPropertyChanged(nameof(CanPull));
                OnPropertyChanged(nameof(PullButtonLabel));
            }
        }
    }

    public bool IsRecommended
    {
        get => _isRecommended;
        private set => SetProperty(ref _isRecommended, value);
    }

    public bool IsPulling
    {
        get => _isPulling;
        set
        {
            if (SetProperty(ref _isPulling, value))
            {
                OnPropertyChanged(nameof(CanPull));
                OnPropertyChanged(nameof(PullButtonLabel));
            }
        }
    }

    public string FitLabel
    {
        get => _fitLabel;
        private set => SetProperty(ref _fitLabel, value);
    }

    public string FitDetail
    {
        get => _fitDetail;
        private set => SetProperty(ref _fitDetail, value);
    }

    public string InstallLabel => IsInstalled ? "Installed" : "Not installed";

    public bool CanPull => !IsInstalled && !IsPulling;

    public string PullButtonLabel => IsInstalled
        ? "Ready"
        : IsPulling ? "Downloading" : "Download";

    public void MarkThinkingDetected()
    {
        if (_hasDetectedThinking)
        {
            return;
        }

        _hasDetectedThinking = true;
        OnPropertyChanged(nameof(SupportsThinking));
        OnPropertyChanged(nameof(ThinkingLabel));
    }

    public void ApplyState(bool isInstalled, LocalLlmHardwareProfile hardware)
    {
        IsInstalled = isInstalled;
        var fit = CalculateFit(Model, hardware);
        IsRecommended = fit.IsRecommended;
        FitLabel = fit.Label;
        FitDetail = fit.Detail;
    }

    private static ModelFit CalculateFit(LocalLlmCatalogModel model, LocalLlmHardwareProfile hardware)
    {
        if (hardware.MaxGpuMemoryGiB is not { } vram)
        {
            return model.WorksOnCpu
                ? new ModelFit(true, "CPU-safe", "GPU memory is unknown; this model can still run locally on CPU.")
                : new ModelFit(false, "GPU unknown", "GPU memory is unknown; install smaller CPU-safe models first.");
        }

        if (vram >= model.RecommendedVramGiB)
        {
            return new ModelFit(true, "Recommended", $"Detected GPU has about {vram:0.#} GB VRAM.");
        }

        if (vram >= model.MinimumVramGiB)
        {
            return new ModelFit(model.RecommendedVramGiB <= 4, "Usable", $"Detected GPU has about {vram:0.#} GB VRAM; keep context near 4K.");
        }

        if (model.WorksOnCpu && model.MinimumVramGiB <= 0)
        {
            return new ModelFit(true, "CPU fallback", $"Detected GPU has about {vram:0.#} GB VRAM; CPU mode is still practical.");
        }

        return new ModelFit(false, "Not ideal", $"Detected GPU has about {vram:0.#} GB VRAM; this model may offload to CPU.");
    }

    private sealed record ModelFit(bool IsRecommended, string Label, string Detail);
}
