// CC-DESC: Describes a local/edge LLM backend dependency and its runtime status.

using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class LlmBackendDependencyViewModel(
    string id,
    string displayName,
    string category,
    string apiStyle,
    string platforms,
    string purpose,
    string installHint,
    bool isRequired,
    bool isRecommended) : ObservableObject
{
    private const string DepIconBase = "avares://ContextControl.Workbench/Assets/DepIcons/";
    private static readonly Dictionary<string, Bitmap> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object IconCacheGate = new();
    private bool _isReady;
    private bool _isManaged;
    private string _statusLabel = isRequired ? "Required" : "Optional";
    private string _detailLabel = installHint;
    private Bitmap? _iconImage;
    private bool? _iconHasTransparentBackground;

    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public string Category { get; } = category;
    public string ApiStyle { get; } = apiStyle;
    public string Platforms { get; } = platforms;
    public string Purpose { get; } = purpose;
    public string InstallHint { get; } = installHint;
    public bool IsRequired { get; } = isRequired;
    public bool IsRecommended { get; } = isRecommended;
    public string InstallUrl => ResolveInstallUrl(Id);
    public string IconSource => ResolveIconSource(Id);
    public bool IconHasTransparentBackground
    {
        get
        {
            if (_iconHasTransparentBackground is { } cached)
            {
                return cached;
            }

            var value = IconTransparency.HasTransparentBackground(IconSource);
            _iconHasTransparentBackground = value;
            return value;
        }
    }

    public Bitmap? IconImage => _iconImage ??= LoadIcon(IconSource);
    public bool HasSafeAutomaticInstaller =>
        PythonDependencyEnvironment.HasManagedInstaller(Id)
        || NativeDependencyEnvironment.HasManagedInstaller(Id)
        || PackageManagerDependencyEnvironment.HasManagedInstaller(Id)
        || SourceDependencyEnvironment.HasManagedInstaller(Id);
    public string InstallActionLabel => IsReady
        ? CanUninstall ? "Uninstall" : CanForceInstall ? "Force install" : "External"
        : HasSafeAutomaticInstaller ? "Install" : "Manual";
    public bool CanInstall => !IsReady;
    public bool CanUninstall => IsReady && IsManaged && !Id.Equals("ollama", StringComparison.OrdinalIgnoreCase);
    public bool CanForceInstall => false;

    public bool IsReady
    {
        get => _isReady;
        private set => SetProperty(ref _isReady, value);
    }

    public bool IsManaged
    {
        get => _isManaged;
        private set => SetProperty(ref _isManaged, value);
    }

    public string StatusLabel
    {
        get => _statusLabel;
        private set => SetProperty(ref _statusLabel, value);
    }

    public string DetailLabel
    {
        get => _detailLabel;
        private set => SetProperty(ref _detailLabel, value);
    }

    public string PriorityLabel => IsRequired ? "Required" : IsRecommended ? "Recommended" : "Optional";

    public void ApplyStatus(bool isReady, string statusLabel, string detailLabel, bool isManaged = false)
    {
        IsReady = isReady;
        IsManaged = isReady && isManaged;
        StatusLabel = string.IsNullOrWhiteSpace(statusLabel) ? (isReady ? "Ready" : "Not detected") : statusLabel.Trim();
        DetailLabel = string.IsNullOrWhiteSpace(detailLabel) ? InstallHint : detailLabel.Trim();
        OnPropertyChanged(nameof(InstallActionLabel));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(CanForceInstall));
    }

    private static string ResolveInstallUrl(string id)
    {
        return (id ?? "").Trim() switch
        {
            "ollama" => "https://ollama.com/download",
            "llama_cpp_server" => "https://github.com/ggml-org/llama.cpp",
            "lm_studio" => "https://lmstudio.ai/download",
            "koboldcpp" => "https://github.com/LostRuins/koboldcpp/releases",
            "mlx_lm" => "https://github.com/ml-explore/mlx-lm",
            "mlc_llm" => "https://llm.mlc.ai/docs/install/mlc_llm.html",
            "transformers" => "https://huggingface.co/docs/transformers/installation",
            "diffusers" => "https://huggingface.co/docs/diffusers/installation",
            "stable_diffusion_cpp" => "https://github.com/leejet/stable-diffusion.cpp",
            "vllm" => "https://docs.vllm.ai/en/latest/getting_started/installation.html",
            "sglang" => "https://docs.sglang.ai/start/install.html",
            "onnxruntime_genai" => "https://onnxruntime.ai/docs/genai/",
            "openvino_genai" => "https://docs.openvino.ai/latest/openvino-workflow-generative.html",
            "tensorrt_llm" => "https://nvidia.github.io/TensorRT-LLM/installation/",
            "exllamav2_tabbyapi" => "https://github.com/theroyallab/tabbyAPI",
            "bitnet_cpp" => "https://github.com/microsoft/BitNet",
            "rwkv_runner" => "https://github.com/josStorer/RWKV-Runner",
            _ => ""
        };
    }

    private static string ResolveIconSource(string id)
    {
        return (id ?? "").Trim() switch
        {
            "ollama" => DepIconBase + "ollama.png",
            "llama_cpp_server" => DepIconBase + "llamacpp.png",
            "lm_studio" => DepIconBase + "lmstudio.png",
            "koboldcpp" => DepIconBase + "koboldai.png",
            "mlx_lm" => DepIconBase + "mlx.png",
            "mlc_llm" => DepIconBase + "mlc.png",
            "transformers" => DepIconBase + "huggingface.png",
            "diffusers" => DepIconBase + "huggingface.png",
            "stable_diffusion_cpp" => DepIconBase + "llamacpp.png",
            "vllm" => DepIconBase + "vllm.png",
            "sglang" => DepIconBase + "sglang.png",
            "onnxruntime_genai" => DepIconBase + "onnx.png",
            "openvino_genai" => DepIconBase + "openvino.png",
            "tensorrt_llm" => DepIconBase + "nvidia.png",
            "exllamav2_tabbyapi" => DepIconBase + "nvidia.png",
            "bitnet_cpp" => DepIconBase + "microsoft.png",
            "rwkv_runner" => DepIconBase + "rwkv.png",
            _ => DepIconBase + "ollama.png"
        };
    }

    private static Bitmap? LoadIcon(string iconSource)
    {
        if (string.IsNullOrWhiteSpace(iconSource))
        {
            return null;
        }

        lock (IconCacheGate)
        {
            if (IconCache.TryGetValue(iconSource, out var cached))
            {
                return cached;
            }

            try
            {
                using var stream = AssetLoader.Open(new Uri(iconSource));
                var bitmap = new Bitmap(stream);
                IconCache[iconSource] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
