// CC-DESC: Local LLM service slice extracted from LocalLlmService.cs.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private static async Task<InstalledModelsResult> DetectInstalledModelsAsync(CancellationToken cancellationToken)
    {
        var httpResult = await DetectInstalledModelsFromHttpAsync(cancellationToken).ConfigureAwait(false);
        if (httpResult.Installed || httpResult.Reachable || httpResult.ModelIds.Count > 0)
        {
            return httpResult;
        }

        return await DetectInstalledModelsFromCommandAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<InstalledModelsResult> DetectInstalledModelsFromHttpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = CreateHttpClient(TimeSpan.FromMilliseconds(900));
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
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command],
            TimeSpan.FromMilliseconds(1200),
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

}
