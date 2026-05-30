// CC-DESC: Detects local LLM hardware/Ollama state and talks to local chat models.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed record LocalLlmCatalogModel(
    string Id,
    string DisplayName,
    string ReleaseDate,
    string IconSource,
    string DownloadSize,
    string License,
    string MinimumRequirement,
    string AdvertisedContext,
    string ComfortableContext,
    string SourceBudget,
    string ExpectedSpeed,
    string PracticalUse,
    double MinimumVramGiB,
    double RecommendedVramGiB,
    bool WorksOnCpu,
    string PullCommand);

public sealed record LocalLlmGpuInfo(string Name, long? AdapterRamBytes)
{
    public double? MemoryGiB => AdapterRamBytes is > 0
        ? AdapterRamBytes.Value / 1024d / 1024d / 1024d
        : null;

    public string MemoryLabel => MemoryGiB is { } memory
        ? $"{memory:0.#} GB"
        : "VRAM unknown";
}

public sealed record LocalLlmHardwareProfile(IReadOnlyList<LocalLlmGpuInfo> Gpus)
{
    public double? MaxGpuMemoryGiB => Gpus
        .Select(gpu => gpu.MemoryGiB)
        .Where(memory => memory is > 0)
        .DefaultIfEmpty()
        .Max();

    public string Summary
    {
        get
        {
            if (Gpus.Count == 0)
            {
                return "GPU not detected yet. CPU-capable small models are safest.";
            }

            var primary = Gpus
                .OrderByDescending(gpu => gpu.MemoryGiB ?? 0)
                .First();
            return $"{primary.Name} - {primary.MemoryLabel}";
        }
    }
}

public sealed record LocalLlmRefreshResult(
    IReadOnlyList<LocalLlmCatalogModel> Catalog,
    IReadOnlySet<string> InstalledModelIds,
    IReadOnlyList<string> UnknownInstalledModelIds,
    LocalLlmHardwareProfile Hardware,
    bool OllamaInstalled,
    bool OllamaReachable,
    string? OllamaExecutablePath,
    string Status);

public sealed record LocalLlmStorageConfigurationResult(
    bool Succeeded,
    string Directory,
    string Status);

public sealed record LocalLlmUsageStats(
    long? PromptTokens,
    long? OutputTokens,
    long? TotalDurationNanoseconds,
    long? LoadDurationNanoseconds,
    long? PromptEvalDurationNanoseconds,
    long? EvalDurationNanoseconds)
{
    public double TokensPerSecond
    {
        get
        {
            var seconds = EvalDurationNanoseconds is > 0
                ? EvalDurationNanoseconds.Value / 1_000_000_000d
                : 0;
            return OutputTokens is > 0 && seconds > 0 ? OutputTokens.Value / seconds : 0;
        }
    }

    public string Summary
    {
        get
        {
            var prompt = PromptTokens is { } promptTokens ? $"{promptTokens} in" : "? in";
            var output = OutputTokens is { } outputTokens ? $"{outputTokens} out" : "? out";
            var speed = TokensPerSecond > 0 ? $"{TokensPerSecond:0.#} tok/s" : "speed ?";
            var total = FormatStatsDuration(TotalDurationNanoseconds);
            return $"{prompt}, {output}, {speed}, {total}";
        }
    }

    private static string FormatStatsDuration(long? nanoseconds)
    {
        var seconds = nanoseconds is > 0 ? nanoseconds.Value / 1_000_000_000d : 0;
        return seconds <= 0 ? "?" : $"{seconds:0.##}s";
    }
}

public sealed record LocalLlmRequest(
    string ModelId,
    string Prompt,
    string Phase,
    IReadOnlyList<string> AttachmentLabels,
    int? ContextWindowTokens = null,
    IReadOnlyList<string>? ImagePaths = null);

public sealed record LocalLlmChatResult(
    bool Succeeded,
    string Status,
    string? Message = null,
    LocalLlmUsageStats? Stats = null);

public sealed record LocalLlmImageGenerationResult(
    bool Succeeded,
    string Status,
    IReadOnlyList<string> ImagePaths,
    string OutputDirectory,
    string? StandardOutput = null);

public sealed record LocalLlmTransferProgress(
    string Operation,
    string Status,
    long? CurrentBytes,
    long? TotalBytes,
    double? BytesPerSecond,
    double? Percent);

public sealed record LocalLlmGenerationProgress(
    string Status,
    string? Delta,
    long? PromptEvalCount,
    long? EvalCount,
    long? TotalDurationNanoseconds,
    long? LoadDurationNanoseconds,
    long? PromptEvalDurationNanoseconds,
    long? EvalDurationNanoseconds,
    bool Done);
