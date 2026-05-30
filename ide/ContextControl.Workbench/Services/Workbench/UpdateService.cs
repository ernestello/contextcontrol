// CC-DESC: Checks GitHub releases and downloads the Windows installer update.

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

public sealed record AppUpdateInfo(
    bool IsAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string InstallerUrl,
    string TargetCommit,
    DateTimeOffset? PublishedAt);

public sealed record AppUpdateDownloadResult(bool Succeeded, string Status, string? InstallerPath = null);

public sealed class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/VulkanVX/contextcontrol/releases/latest";
    private const string InstallerAssetName = "ContextControl-win-x64-Setup.exe";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(20);

    public string CurrentVersion => NormalizeVersionLabel(
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0");

    public async Task<AppUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var http = CreateHttpClient();
        using var response = await http.GetAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var latestVersion = NormalizeVersionLabel(GetString(root, "tag_name") ?? "0.0.0");
        var releaseUrl = GetString(root, "html_url") ?? "https://github.com/VulkanVX/contextcontrol/releases/latest";
        var targetCommit = GetString(root, "target_commitish") ?? "";
        var publishedAt = TryReadDate(root, "published_at");
        var installerUrl = ResolveInstallerUrl(root);
        var currentVersion = CurrentVersion;

        return new AppUpdateInfo(
            IsVersionNewer(latestVersion, currentVersion),
            currentVersion,
            latestVersion,
            releaseUrl,
            installerUrl,
            targetCommit,
            publishedAt);
    }

    public async Task<AppUpdateDownloadResult> DownloadInstallerAsync(
        AppUpdateInfo update,
        IProgress<LocalLlmTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerUrl))
        {
            return new AppUpdateDownloadResult(false, $"Release {update.LatestVersion} has no {InstallerAssetName} asset.");
        }

        if (!Uri.TryCreate(update.InstallerUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return new AppUpdateDownloadResult(false, "Update download URL was not HTTPS.");
        }

        var updateDirectory = Path.Combine(Path.GetTempPath(), "ContextControl", "updates", update.LatestVersion);
        Directory.CreateDirectory(updateDirectory);
        var installerPath = Path.Combine(updateDirectory, InstallerAssetName);
        var partialPath = installerPath + ".download";
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        using var http = CreateHttpClient(timeout: TimeSpan.FromMinutes(20));
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new AppUpdateDownloadResult(false, $"Update download failed: HTTP {(int)response.StatusCode}.");
        }

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 128, useAsync: true);
        var buffer = new byte[1024 * 128];
        var stopwatch = Stopwatch.StartNew();
        long readBytes = 0;
        progress?.Report(new LocalLlmTransferProgress(
            "Downloading update",
            $"Downloading ContextControl {update.LatestVersion}...",
            0,
            totalBytes,
            null,
            totalBytes is > 0 ? 0 : null));
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readBytes += read;
            var elapsedSeconds = Math.Max(0.001d, stopwatch.Elapsed.TotalSeconds);
            var speed = readBytes / elapsedSeconds;
            var percent = totalBytes is > 0
                ? Math.Clamp(readBytes / (double)totalBytes.Value * 100d, 0d, 100d)
                : (double?)null;
            var status = totalBytes is > 0
                ? $"Downloading update {FormatBytes(readBytes)} / {FormatBytes(totalBytes.Value)}"
                : $"Downloading update {FormatBytes(readBytes)}";
            progress?.Report(new LocalLlmTransferProgress(
                "Downloading update",
                status,
                readBytes,
                totalBytes,
                speed,
                percent));
        }

        if (File.Exists(installerPath))
        {
            File.Delete(installerPath);
        }

        File.Move(partialPath, installerPath);
        progress?.Report(new LocalLlmTransferProgress(
            "Downloading update",
            $"Downloaded ContextControl {update.LatestVersion}.",
            readBytes,
            totalBytes ?? readBytes,
            null,
            100));
        return new AppUpdateDownloadResult(true, $"Downloaded ContextControl {update.LatestVersion}.", installerPath);
    }

    public AppUpdateDownloadResult LaunchInstaller(string installerPath, string installDirectory, int waitForProcessId = 0)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            return new AppUpdateDownloadResult(false, "Downloaded installer was not found.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = false
        };
        if (!string.IsNullOrWhiteSpace(installDirectory) && Directory.Exists(installDirectory))
        {
            startInfo.ArgumentList.Add($"/installDir={installDirectory}");
        }

        if (waitForProcessId > 0)
        {
            startInfo.ArgumentList.Add($"/waitForProcess={waitForProcessId}");
        }

        Process.Start(startInfo);
        return new AppUpdateDownloadResult(true, "Update installer started.", installerPath);
    }

    private static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var http = new HttpClient
        {
            Timeout = timeout ?? HttpTimeout
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ContextControl", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    private static string ResolveInstallerUrl(JsonElement releaseRoot)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            if (!string.Equals(name, InstallerAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return GetString(asset, "browser_download_url") ?? "";
        }

        return "";
    }

    private static bool IsVersionNewer(string latest, string current)
    {
        if (TryParseVersion(latest, out var latestVersion) && TryParseVersion(current, out var currentVersion))
        {
            return latestVersion.CompareTo(currentVersion) > 0;
        }

        return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        var clean = NormalizeVersionLabel(value);
        var plusIndex = clean.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            clean = clean[..plusIndex];
        }

        var dashIndex = clean.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            clean = clean[..dashIndex];
        }

        return Version.TryParse(clean, out version!);
    }

    private static string NormalizeVersionLabel(string value)
    {
        var clean = (value ?? "").Trim();
        return clean.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? clean[1..]
            : clean;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static DateTimeOffset? TryReadDate(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
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
}
