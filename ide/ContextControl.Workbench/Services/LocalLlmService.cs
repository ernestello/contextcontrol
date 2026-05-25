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
    int? ContextWindowTokens = null);

public sealed record LocalLlmChatResult(
    bool Succeeded,
    string Status,
    string? Message = null,
    LocalLlmUsageStats? Stats = null);

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

public sealed class LocalLlmService
{
    public const string OllamaDownloadPageUrl = "https://ollama.com/download/windows";
    public const string OllamaWindowsInstallerUrl = "https://ollama.com/download/OllamaSetup.exe";
    public const string OllamaWindowsInstallCommand = "irm https://ollama.com/install.ps1 | iex";
    public const string OllamaInstallerSize = "~2.0 GB installer";
    private static readonly Regex SizeProgressRegex = new(
        @"(?<current>\d+(?:\.\d+)?)\s*(?<currentUnit>[KMGT]?i?B)\s*/\s*(?<total>\d+(?:\.\d+)?)\s*(?<totalUnit>[KMGT]?i?B)(?:\s+(?<speed>\d+(?:\.\d+)?)\s*(?<speedUnit>[KMGT]?i?B/s))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PercentRegex = new(
        @"(?<percent>\d+(?:\.\d+)?)%",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AnsiRegex = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Uri OllamaBaseUri = new("http://127.0.0.1:11434");

    public static IReadOnlyList<LocalLlmCatalogModel> Catalog { get; } =
    [
        new(
            "qwen2.5-coder:1.5b",
            "Qwen2.5 Coder 1.5B",
            "~986 MB Q4",
            "Apache 2.0",
            "4 GB RAM; CPU works; 2 GB+ VRAM is comfortable",
            "32K",
            "4K safe, 8K usually okay",
            "4K: 150-300 LOC / 8k-12k chars; 8K: 350-700 LOC / 18k-28k chars",
            "Fast on 4 GB VRAM; good CPU fallback",
            "Fast classifier, file summary, folder responsibility detection, tiny edits.",
            0,
            2,
            true,
            "ollama pull qwen2.5-coder:1.5b"),
        new(
            "qwen2.5-coder:3b",
            "Qwen2.5 Coder 3B",
            "~1.9 GB Q4",
            "Apache 2.0",
            "6-8 GB RAM; 4 GB VRAM recommended",
            "32K",
            "4K safe, 8K maybe okay",
            "4K: 150-300 LOC / 8k-12k chars; 8K: 350-700 LOC / 18k-28k chars",
            "Good practical speed on 4 GB VRAM",
            "Best local CC fit for style detection, small patches, and patch review.",
            3.5,
            4,
            true,
            "ollama pull qwen2.5-coder:3b"),
        new(
            "phi4-mini",
            "Phi-4 Mini",
            "~2.5 GB Q4",
            "MIT",
            "8 GB RAM; 4 GB VRAM recommended",
            "128K",
            "4K safe, 8K maybe okay",
            "4K: 150-300 LOC / 8k-12k chars; 8K: 350-700 LOC / 18k-28k chars",
            "Medium on 4 GB VRAM",
            "Refactor planning, reasoning, consistency review, project-style explanation.",
            3.5,
            4,
            true,
            "ollama pull phi4-mini"),
        new(
            "granite-code:3b",
            "Granite Code 3B",
            "~2.0 GB Q4",
            "Apache 2.0",
            "8 GB RAM; 4 GB VRAM recommended",
            "128K",
            "4K safe, 8K maybe okay",
            "4K: 150-300 LOC / 8k-12k chars; 8K: 350-700 LOC / 18k-28k chars",
            "Medium on 4 GB VRAM",
            "Code explanation and fixing alternative; useful against Qwen Coder.",
            3.5,
            4,
            true,
            "ollama pull granite-code:3b"),
        new(
            "qwen2.5-coder:7b",
            "Qwen2.5 Coder 7B",
            "~4.7 GB Q4",
            "Apache 2.0",
            "12 GB RAM; 8 GB+ VRAM recommended",
            "32K",
            "8K on 8GB+; avoid on 4GB",
            "8K: ~350-700 LOC / ~18k-28k chars. 4GB VRAM likely offloads; keep snippets small if experimenting.",
            "Slow/offloaded on 4 GB; good on 8 GB+",
            "Stronger coding model for 8 GB+ GPUs, not a good fit for fast 4 GB use.",
            6.5,
            8,
            false,
            "ollama pull qwen2.5-coder:7b")
    ];

    public async Task<LocalLlmRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var hardwareTask = DetectHardwareAsync(cancellationToken);
        var installedTask = DetectInstalledModelsAsync(cancellationToken);

        await Task.WhenAll(hardwareTask, installedTask).ConfigureAwait(false);

        var hardware = await hardwareTask.ConfigureAwait(false);
        var installed = await installedTask.ConfigureAwait(false);
        var catalogIds = Catalog.Select(model => model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = installed.ModelIds
            .Where(modelId => !catalogIds.Contains(modelId))
            .OrderBy(modelId => modelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var status = installed.Reachable
            ? $"Ollama ready. {installed.ModelIds.Count} local model(s) installed."
            : installed.Status;

        return new LocalLlmRefreshResult(
            Catalog,
            installed.ModelIds,
            unknown,
            hardware,
            installed.Installed,
            installed.Reachable,
            installed.ExecutablePath,
            status);
    }

    public async Task<LocalLlmChatResult> SendChatAsync(
        string modelId,
        string message,
        IReadOnlyList<string> attachmentPaths,
        CancellationToken cancellationToken = default)
    {
        return await SendChatAsync(modelId, message, attachmentPaths, null, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalLlmChatResult> SendChatAsync(
        string modelId,
        string message,
        IReadOnlyList<string> attachmentPaths,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        var preparedMessage = BuildChatMessage(message, attachmentPaths);
        return await SendChatAsync(
            new LocalLlmRequest(modelId, preparedMessage, "chat", attachmentPaths.Select(path => Path.GetFileName(path) ?? path).ToArray()),
            progress,
            terminal,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalLlmChatResult> SendChatAsync(
        LocalLlmRequest request,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            return new LocalLlmChatResult(false, "Choose an installed local model first.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return new LocalLlmChatResult(false, "Write a chat message first.");
        }

        var chatRequest = new OllamaChatRequest(
            request.ModelId,
            [new OllamaChatMessage("user", request.Prompt.Trim())],
            Stream: true,
            Options: request.ContextWindowTokens is > 0
                ? new OllamaChatOptions(request.ContextWindowTokens.Value)
                : null);

        try
        {
            terminal?.Report(request.ContextWindowTokens is > 0
                ? $"> ollama chat {chatRequest.Model} --num-ctx {request.ContextWindowTokens.Value}"
                : $"> ollama chat {chatRequest.Model}");
            progress?.Report(new LocalLlmGenerationProgress("Loading model and preparing prompt...", null, null, null, null, null, null, null, false));
            using var http = CreateHttpClient(request.ContextWindowTokens is > 8192 ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(10));
            using var content = new StringContent(JsonSerializer.Serialize(chatRequest, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(new Uri(OllamaBaseUri, "/api/chat"), content, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new LocalLlmChatResult(false, $"Ollama returned {(int)response.StatusCode}: {FirstLine(responseText)}");
            }

            var answerBuilder = new StringBuilder();
            OllamaChatResponse? finalResponse = null;
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(responseStream);
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions);
                var delta = chatResponse?.Message?.Content;
                if (!string.IsNullOrEmpty(delta))
                {
                    answerBuilder.Append(delta);
                }

                progress?.Report(new LocalLlmGenerationProgress(
                    chatResponse?.Done == true ? "Generation complete." : "Generating local response...",
                    delta,
                    chatResponse?.PromptEvalCount,
                    chatResponse?.EvalCount,
                    chatResponse?.TotalDuration,
                    chatResponse?.LoadDuration,
                    chatResponse?.PromptEvalDuration,
                    chatResponse?.EvalDuration,
                    chatResponse?.Done == true));

                if (chatResponse?.Done == true)
                {
                    finalResponse = chatResponse;
                    terminal?.Report(BuildGenerationSummary(chatResponse));
                }
            }

            var answer = answerBuilder.ToString().Trim();
            var stats = finalResponse is null ? null : BuildUsageStats(finalResponse);
            return string.IsNullOrWhiteSpace(answer)
                ? new LocalLlmChatResult(false, "Ollama returned an empty response.")
                : new LocalLlmChatResult(true, $"Local chat completed with {chatRequest.Model}.", answer, stats);
        }
        catch (HttpRequestException ex)
        {
            return new LocalLlmChatResult(false, $"Ollama is not reachable at {OllamaBaseUri}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new LocalLlmChatResult(false, "Local chat timed out.");
        }
    }

    public Task<LocalLlmChatResult> PullModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        return PullModelAsync(modelId, null, null, cancellationToken);
    }

    public async Task<LocalLlmChatResult> PullModelAsync(
        string modelId,
        IProgress<LocalLlmTransferProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new LocalLlmChatResult(false, "No model selected.");
        }

        var ollamaPath = ResolveOllamaExecutable();
        if (ollamaPath is null)
        {
            return new LocalLlmChatResult(false, "Ollama command was not found. Install Ollama first.");
        }

        var parser = new OllamaPullProgressParser($"Downloading {modelId}", progress);
        terminal?.Report($"> {ollamaPath} pull {modelId}");
        var result = await RunProcessStreamingAsync(
            ollamaPath,
            ["pull", modelId],
            TimeSpan.FromMinutes(30),
            chunk =>
            {
                parser.Append(chunk);
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                }
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0
            ? new LocalLlmChatResult(true, $"Downloaded {modelId}.")
            : new LocalLlmChatResult(false, FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput) ?? $"ollama pull exited {result.ExitCode}.");
    }

    public Task<LocalLlmChatResult> InstallOllamaAsync(CancellationToken cancellationToken = default)
    {
        return InstallOllamaAsync(null, null, cancellationToken);
    }

    public async Task<LocalLlmChatResult> InstallOllamaAsync(
        IProgress<LocalLlmTransferProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new LocalLlmChatResult(false, "Automatic Ollama install is wired for Windows. Open the download page for this OS.");
        }

        terminal?.Report($"> download {OllamaWindowsInstallerUrl}");
        var installerPath = await DownloadOllamaInstallerAsync(progress, terminal, cancellationToken).ConfigureAwait(false);
        progress?.Report(new LocalLlmTransferProgress(
            "Installing Ollama",
            "Running OllamaSetup.exe; finish the setup window to continue",
            new FileInfo(installerPath).Length,
            new FileInfo(installerPath).Length,
            null,
            100));
        terminal?.Report($"> start {installerPath}");
        terminal?.Report("Installer window launched. Waiting for it to finish...");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        };

        try
        {
            if (!process.Start())
            {
                return new LocalLlmChatResult(false, "Ollama installer could not be started.");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            terminal?.Report($"Installer exited {process.ExitCode}.");
            return await WaitForOllamaInstallAsync(terminal, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new LocalLlmChatResult(false, $"Ollama installer could not run: {ex.Message}");
        }
    }

    public LocalLlmChatResult OpenOllamaDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OllamaDownloadPageUrl,
                UseShellExecute = true
            });
            return new LocalLlmChatResult(true, "Opened the official Ollama download page.");
        }
        catch (Exception ex)
        {
            return new LocalLlmChatResult(false, $"Could not open Ollama download page: {ex.Message}");
        }
    }

    private static async Task<LocalLlmChatResult> WaitForOllamaInstallAsync(
        IProgress<string>? terminal,
        CancellationToken cancellationToken)
    {
        terminal?.Report("Checking for installed Ollama executable...");
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            var executablePath = ResolveOllamaExecutable();
            if (executablePath is not null)
            {
                terminal?.Report($"Ollama installed: {executablePath}");
                return new LocalLlmChatResult(true, "Ollama installed. Refreshing local model state.");
            }

            terminal?.Report($"Ollama not visible yet; checking again ({attempt}/30)...");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }

        return new LocalLlmChatResult(false, "Ollama installer closed, but the Ollama executable was not found yet. Try Refresh or reopen the app.");
    }

    private static string? ResolveOllamaExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Ollama", "ollama.exe")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindExecutableOnPath("ollama.exe") ?? FindExecutableOnPath("ollama");
    }

    private static string? FindExecutableOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore broken PATH entries.
            }
        }

        return null;
    }

    private static async Task<InstalledModelsResult> DetectInstalledModelsAsync(CancellationToken cancellationToken)
    {
        var httpResult = await DetectInstalledModelsFromHttpAsync(cancellationToken).ConfigureAwait(false);
        if (httpResult.Reachable || httpResult.ModelIds.Count > 0)
        {
            return httpResult;
        }

        return await DetectInstalledModelsFromCommandAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<InstalledModelsResult> DetectInstalledModelsFromHttpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = CreateHttpClient(TimeSpan.FromMilliseconds(1800));
            var text = await http.GetStringAsync(new Uri(OllamaBaseUri, "/api/tags"), cancellationToken).ConfigureAwait(false);
            var tags = JsonSerializer.Deserialize<OllamaTagsResponse>(text, JsonOptions);
            var ids = tags?.Models?
                .Select(model => string.IsNullOrWhiteSpace(model.Name) ? model.Model : model.Name)
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Select(model => model!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return new InstalledModelsResult(true, true, ids, ResolveOllamaExecutable(), $"Ollama ready. {ids.Count} local model(s) installed.");
        }
        catch
        {
            var executablePath = ResolveOllamaExecutable();
            return executablePath is not null
                ? new InstalledModelsResult(true, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), executablePath, $"Ollama installed at {executablePath}. Start Ollama to detect installed models.")
                : new InstalledModelsResult(false, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), null, "Ollama is not running. Start Ollama to detect installed models.");
        }
    }

    private static async Task<InstalledModelsResult> DetectInstalledModelsFromCommandAsync(CancellationToken cancellationToken)
    {
        var ollamaPath = ResolveOllamaExecutable();
        if (ollamaPath is null)
        {
            return new InstalledModelsResult(false, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), null, "Ollama command was not found. Install Ollama to download and chat locally.");
        }

        var result = await RunProcessAsync(
            ollamaPath,
            ["list"],
            TimeSpan.FromSeconds(3),
            cancellationToken).ConfigureAwait(false);

        if (!result.Started)
        {
            return new InstalledModelsResult(true, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ollamaPath, $"Ollama found at {ollamaPath}, but it could not be started.");
        }

        if (result.ExitCode != 0)
        {
            return new InstalledModelsResult(true, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ollamaPath, FirstLine(result.StandardError) ?? "Ollama is installed but not responding.");
        }

        var ids = result.StandardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new InstalledModelsResult(true, true, ids, ollamaPath, $"Ollama ready. {ids.Count} local model(s) installed.");
    }

    private static async Task<LocalLlmHardwareProfile> DetectHardwareAsync(CancellationToken cancellationToken)
    {
        var nvidia = await DetectNvidiaGpusAsync(cancellationToken).ConfigureAwait(false);
        if (nvidia.Count > 0)
        {
            return new LocalLlmHardwareProfile(nvidia);
        }

        var windows = await DetectWindowsGpusAsync(cancellationToken).ConfigureAwait(false);
        return new LocalLlmHardwareProfile(windows);
    }

    private static async Task<IReadOnlyList<LocalLlmGpuInfo>> DetectNvidiaGpusAsync(CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            "nvidia-smi",
            ["--query-gpu=name,memory.total", "--format=csv,noheader,nounits"],
            TimeSpan.FromSeconds(2),
            cancellationToken).ConfigureAwait(false);

        if (!result.Started || result.ExitCode != 0)
        {
            return [];
        }

        return result.StandardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseNvidiaGpuLine)
            .Where(gpu => gpu is not null)
            .Select(gpu => gpu!)
            .ToArray();
    }

    private static LocalLlmGpuInfo? ParseNvidiaGpuLine(string line)
    {
        var parts = line.Split(',', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return null;
        }

        long? bytes = null;
        if (parts.Length > 1 && long.TryParse(parts[1], out var mib))
        {
            bytes = mib * 1024L * 1024L;
        }

        return new LocalLlmGpuInfo(parts[0], bytes);
    }

    private static async Task<IReadOnlyList<LocalLlmGpuInfo>> DetectWindowsGpusAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        const string command = "Get-CimInstance Win32_VideoController | ForEach-Object { (($_.Name -replace '\\|',' ') + '|' + $_.AdapterRAM) }";
        var result = await RunProcessAsync(
            "powershell",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
            TimeSpan.FromSeconds(3),
            cancellationToken).ConfigureAwait(false);

        if (!result.Started || result.ExitCode != 0)
        {
            return [];
        }

        return result.StandardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseWindowsGpuLine)
            .Where(gpu => gpu is not null)
            .Select(gpu => gpu!)
            .ToArray();
    }

    private static LocalLlmGpuInfo? ParseWindowsGpuLine(string line)
    {
        var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return null;
        }

        long? bytes = null;
        if (parts.Length > 1 && long.TryParse(parts[1], out var parsed) && parsed > 0)
        {
            bytes = parsed;
        }

        return new LocalLlmGpuInfo(parts[0], bytes);
    }

    private static string BuildChatMessage(string message, IReadOnlyList<string> attachmentPaths)
    {
        var clean = message.Trim();
        var attachments = attachmentPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (attachments.Length == 0)
        {
            return clean;
        }

        var builder = new StringBuilder(clean);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Attached local file paths:");
        foreach (var path in attachments)
        {
            builder.AppendLine(path);
        }

        return builder.ToString();
    }

    private static string BuildGenerationSummary(OllamaChatResponse response)
    {
        var evalTokens = response.EvalCount ?? 0;
        var evalSeconds = NanosecondsToSeconds(response.EvalDuration);
        var speed = evalTokens > 0 && evalSeconds > 0
            ? $"{evalTokens / evalSeconds:0.#} tok/s"
            : "speed unknown";
        var total = FormatDuration(response.TotalDuration);
        var load = FormatDuration(response.LoadDuration);
        var prompt = response.PromptEvalCount is { } promptTokens
            ? $"{promptTokens} prompt tok"
            : "prompt tok ?";
        return $"done: {response.EvalCount ?? 0} output tok, {prompt}, {speed}, total {total}, load {load}";
    }

    private static LocalLlmUsageStats BuildUsageStats(OllamaChatResponse response)
    {
        return new LocalLlmUsageStats(
            response.PromptEvalCount,
            response.EvalCount,
            response.TotalDuration,
            response.LoadDuration,
            response.PromptEvalDuration,
            response.EvalDuration);
    }

    private static double NanosecondsToSeconds(long? nanoseconds)
    {
        return nanoseconds is > 0 ? nanoseconds.Value / 1_000_000_000d : 0;
    }

    private static string FormatDuration(long? nanoseconds)
    {
        var seconds = NanosecondsToSeconds(nanoseconds);
        return seconds <= 0 ? "?" : $"{seconds:0.##}s";
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        return new HttpClient
        {
            BaseAddress = OllamaBaseUri,
            Timeout = timeout
        };
    }

    private static async Task<string> DownloadOllamaInstallerAsync(
        IProgress<LocalLlmTransferProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken)
    {
        var downloadDirectory = Path.Combine(Path.GetTempPath(), "ContextControl", "Ollama");
        Directory.CreateDirectory(downloadDirectory);
        var installerPath = Path.Combine(downloadDirectory, "OllamaSetup.exe");

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        using var response = await http.GetAsync(
            OllamaWindowsInstallerUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 128, useAsync: true);

        var buffer = new byte[1024 * 128];
        long readBytes = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        progress?.Report(new LocalLlmTransferProgress(
            "Downloading Ollama",
            "Downloading OllamaSetup.exe",
            0,
            totalBytes,
            0,
            totalBytes is > 0 ? 0 : null));
        terminal?.Report($"Downloading OllamaSetup.exe ({FormatBytes(totalBytes ?? 0)} total)");

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readBytes += read;

            var elapsed = stopwatch.Elapsed;
            if (elapsed - lastReport >= TimeSpan.FromMilliseconds(160) || totalBytes == readBytes)
            {
                lastReport = elapsed;
                var speed = elapsed.TotalSeconds <= 0 ? 0 : readBytes / elapsed.TotalSeconds;
                progress?.Report(new LocalLlmTransferProgress(
                    "Downloading Ollama",
                    "Downloading OllamaSetup.exe",
                    readBytes,
                    totalBytes,
                    speed,
                    totalBytes is > 0 ? Math.Clamp(readBytes * 100d / totalBytes.Value, 0, 100) : null));
                terminal?.Report($"{FormatBytes(readBytes)} / {(totalBytes is > 0 ? FormatBytes(totalBytes.Value) : "?")} at {FormatBytes((long)speed)}/s");
            }
        }

        progress?.Report(new LocalLlmTransferProgress(
            "Downloading Ollama",
            "OllamaSetup.exe downloaded",
            readBytes,
            totalBytes ?? readBytes,
            0,
            100));
        terminal?.Report($"Downloaded OllamaSetup.exe to {installerPath}");

        return installerPath;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return ProcessResult.NotStarted();
            }
        }
        catch
        {
            return ProcessResult.NotStarted();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new ProcessResult(true, -1, await SafeReadAsync(stdoutTask).ConfigureAwait(false), await SafeReadAsync(stderrTask).ConfigureAwait(false));
        }

        return new ProcessResult(
            true,
            process.ExitCode,
            await SafeReadAsync(stdoutTask).ConfigureAwait(false),
            await SafeReadAsync(stderrTask).ConfigureAwait(false));
    }

    private static async Task<ProcessResult> RunProcessStreamingAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return ProcessResult.NotStarted();
            }
        }
        catch
        {
            return ProcessResult.NotStarted();
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = ReadProcessStreamAsync(process.StandardOutput, stdout, onChunk, cancellationToken);
        var stderrTask = ReadProcessStreamAsync(process.StandardError, stderr, onChunk, cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await Task.WhenAll(SafeWaitAsync(stdoutTask), SafeWaitAsync(stderrTask)).ConfigureAwait(false);
            return new ProcessResult(true, -1, stdout.ToString(), stderr.ToString());
        }

        await Task.WhenAll(SafeWaitAsync(stdoutTask), SafeWaitAsync(stderrTask)).ConfigureAwait(false);
        return new ProcessResult(true, process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task ReadProcessStreamAsync(
        StreamReader reader,
        StringBuilder sink,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        var buffer = new char[512];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            var chunk = new string(buffer, 0, read);
            sink.Append(chunk);
            onChunk(chunk);
        }
    }

    private static async Task SafeWaitAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Keep process cleanup best-effort.
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return "";
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Detection should never crash the workbench.
        }
    }

    private static string? FirstLine(string? text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

    private static string CleanProgressText(string text)
    {
        return AnsiRegex.Replace(text ?? "", "")
            .Replace('\b', ' ')
            .Trim();
    }

    private static string FormatBytes(long bytes)
    {
        var value = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var number = (double)value;
        var unitIndex = 0;
        while (number >= 1024 && unitIndex < units.Length - 1)
        {
            number /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{number:0} {units[unitIndex]}"
            : $"{number:0.#} {units[unitIndex]}";
    }

    private static long? ParseByteCount(string value, string unit)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var normalized = (unit ?? "").Trim().ToUpperInvariant();
        var multiplier = normalized switch
        {
            "KB" or "KIB" => 1024d,
            "MB" or "MIB" => 1024d * 1024d,
            "GB" or "GIB" => 1024d * 1024d * 1024d,
            "TB" or "TIB" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };

        return (long)Math.Max(0, number * multiplier);
    }

    private static double? ParseByteRate(string value, string unit)
    {
        var cleanUnit = (unit ?? "").Replace("/s", "", StringComparison.OrdinalIgnoreCase);
        return ParseByteCount(value, cleanUnit);
    }

    private sealed class OllamaPullProgressParser(string operation, IProgress<LocalLlmTransferProgress>? progress)
    {
        private readonly StringBuilder _line = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long? _lastCurrentBytes;

        public void Append(string chunk)
        {
            if (progress is null || string.IsNullOrEmpty(chunk))
            {
                return;
            }

            foreach (var ch in chunk)
            {
                if (ch is '\r' or '\n')
                {
                    Flush();
                    continue;
                }

                _line.Append(ch);
            }

            Flush();
        }

        private void Flush()
        {
            if (_line.Length == 0)
            {
                return;
            }

            var text = CleanProgressText(_line.ToString());
            if (string.IsNullOrWhiteSpace(text))
            {
                _line.Clear();
                return;
            }

            var sizeMatch = SizeProgressRegex.Match(text);
            if (sizeMatch.Success)
            {
                var current = ParseByteCount(sizeMatch.Groups["current"].Value, sizeMatch.Groups["currentUnit"].Value);
                var total = ParseByteCount(sizeMatch.Groups["total"].Value, sizeMatch.Groups["totalUnit"].Value);
                var speed = sizeMatch.Groups["speed"].Success
                    ? ParseByteRate(sizeMatch.Groups["speed"].Value, sizeMatch.Groups["speedUnit"].Value)
                    : EstimateSpeed(current);
                progress!.Report(new LocalLlmTransferProgress(
                    operation,
                    text,
                    current,
                    total,
                    speed,
                    current is not null && total is > 0 ? Math.Clamp(current.Value * 100d / total.Value, 0, 100) : null));
                _line.Clear();
                return;
            }

            var percentMatch = PercentRegex.Match(text);
            if (percentMatch.Success
                && double.TryParse(percentMatch.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                progress!.Report(new LocalLlmTransferProgress(
                    operation,
                    text,
                    null,
                    null,
                    EstimateSpeed(null),
                    Math.Clamp(percent, 0, 100)));
                _line.Clear();
                return;
            }

            progress!.Report(new LocalLlmTransferProgress(
                operation,
                text,
                _lastCurrentBytes,
                null,
                EstimateSpeed(_lastCurrentBytes),
                null));
            _line.Clear();
        }

        private double? EstimateSpeed(long? currentBytes)
        {
            if (currentBytes is null)
            {
                return null;
            }

            _lastCurrentBytes = currentBytes;
            return _stopwatch.Elapsed.TotalSeconds <= 0
                ? null
                : currentBytes.Value / _stopwatch.Elapsed.TotalSeconds;
        }
    }

    private sealed record InstalledModelsResult(
        bool Installed,
        bool Reachable,
        IReadOnlySet<string> ModelIds,
        string? ExecutablePath,
        string Status);

    private sealed record ProcessResult(bool Started, int ExitCode, string StandardOutput, string StandardError)
    {
        public static ProcessResult NotStarted() => new(false, -1, "", "");
    }

    private sealed record OllamaTagsResponse(IReadOnlyList<OllamaModelTag>? Models);

    private sealed record OllamaModelTag(string? Name, string? Model);

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaChatMessage> Messages,
        bool Stream,
        OllamaChatOptions? Options = null);

    private sealed record OllamaChatOptions([property: JsonPropertyName("num_ctx")] int NumContext);

    private sealed record OllamaChatMessage(string Role, string Content);

    private sealed record OllamaChatResponse(
        OllamaChatMessage? Message,
        bool? Done,
        long? TotalDuration,
        long? LoadDuration,
        long? PromptEvalCount,
        long? PromptEvalDuration,
        long? EvalCount,
        long? EvalDuration);
}
