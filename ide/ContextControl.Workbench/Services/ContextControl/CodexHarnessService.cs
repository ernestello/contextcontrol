// CC-DESC: Runs Codex CLI inside a ContextControl-owned read-only capsule harness.

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

public sealed record CodexHarnessRequest(
    string UserMessage,
    ContextCapsulePhase Phase,
    string ContextControlRoot,
    IReadOnlyList<ContextCapsuleAttachment> Attachments,
    string CodexInstructions,
    string EnabledSkillbookInstructions);

public sealed record CodexHarnessResult(
    bool Succeeded,
    string Status,
    string Message,
    string Thinking,
    string EventTrace,
    int ExitCode);

public sealed record CodexHarnessExecutionPlan(
    string HarnessRoot,
    string PromptPath,
    string LastMessagePath,
    IReadOnlyList<string> Arguments);

public sealed record CodexAvailabilityResult(
    bool Available,
    string Status,
    string Version,
    bool IsAuthenticated = false,
    bool RequiresLogin = false);

public sealed record CodexLoginLaunchResult(
    bool Succeeded,
    string Status);

public sealed class CodexHarnessService
{
    private const int MaxAttachmentCharacters = 512_000;
    private const int MaxEventTraceLines = 80;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public async Task<CodexAvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var versionResult = await RunCodexCommandAsync(["--version"], TimeSpan.FromSeconds(5), cancellationToken);
            var version = versionResult.StandardOutput.Trim();
            if (versionResult.ExitCode != 0 || string.IsNullOrWhiteSpace(version))
            {
                var detail = FirstInterestingLine(versionResult.StandardError)
                    ?? FirstInterestingLine(versionResult.StandardOutput)
                    ?? $"codex --version exited {versionResult.ExitCode}";
                return new CodexAvailabilityResult(false, $"Codex CLI unavailable: {detail}", "");
            }

            var loginResult = await RunCodexCommandAsync(["login", "status"], TimeSpan.FromSeconds(5), cancellationToken);
            var loginText = $"{loginResult.StandardOutput}{Environment.NewLine}{loginResult.StandardError}".Trim();
            if (loginResult.ExitCode == 0 && IsLoggedInStatus(loginText))
            {
                var loginLine = FirstInterestingLine(loginText) ?? "logged in";
                return new CodexAvailabilityResult(
                    true,
                    $"Codex CLI ready: {version}; {loginLine}",
                    version,
                    IsAuthenticated: true,
                    RequiresLogin: false);
            }

            var loginDetail = FirstInterestingLine(loginText) ?? "not logged in";
            if (IsLoginRequiredText(loginText) || loginResult.ExitCode != 0)
            {
                return new CodexAvailabilityResult(
                    true,
                    $"Codex CLI installed ({version}), but login is required. Click Login Codex, complete auth, then Refresh Codex. Detail: {loginDetail}",
                    version,
                    IsAuthenticated: false,
                    RequiresLogin: true);
            }

            return new CodexAvailabilityResult(
                true,
                $"Codex CLI installed ({version}), but authentication status is unclear. Click Refresh Codex or run Codex Doctor.",
                version,
                IsAuthenticated: false,
                RequiresLogin: true);
        }
        catch (OperationCanceledException)
        {
            return new CodexAvailabilityResult(false, "Codex CLI check timed out.", "");
        }
        catch (Exception ex)
        {
            return new CodexAvailabilityResult(false, $"Codex CLI unavailable: {ex.Message}", "");
        }
    }

    public CodexLoginLaunchResult LaunchInteractiveLogin()
    {
        if (OperatingSystem.IsWindows())
        {
            var powershellCommand = "$Host.UI.RawUI.WindowTitle='ContextControl Codex Login'; "
                + "codex login; "
                + "Write-Host ''; "
                + "Write-Host 'Return to ContextControl and click Refresh Codex.'; "
                + "Read-Host 'Press Enter to close'";

            if (TryStartProcess(
                    "wt.exe",
                    ["new-tab", "powershell", "-NoExit", "-ExecutionPolicy", "Bypass", "-Command", powershellCommand],
                    out _))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex login in Windows Terminal. Complete the browser/device auth, then click Refresh Codex.");
            }

            if (TryStartProcess(
                    "powershell.exe",
                    ["-NoExit", "-ExecutionPolicy", "Bypass", "-Command", powershellCommand],
                    out _))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex login in PowerShell. Complete the browser/device auth, then click Refresh Codex.");
            }

            if (TryStartProcess(
                    "cmd.exe",
                    ["/k", "codex login && echo. && echo Return to ContextControl and click Refresh Codex."],
                    out var cmdError))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex login in Command Prompt. Complete auth, then click Refresh Codex.");
            }

            return new CodexLoginLaunchResult(false, $"Could not open a terminal for Codex login: {cmdError}. Open a terminal and run: codex login");
        }

        if (OperatingSystem.IsMacOS())
        {
            var script = "tell application \"Terminal\" to do script \"codex login; echo ''; echo Return to ContextControl and click Refresh Codex.; read -r -p 'Press Enter to close' _\"";
            if (TryStartProcess("osascript", ["-e", script], out var macError))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex login in Terminal. Complete auth, then click Refresh Codex.");
            }

            return new CodexLoginLaunchResult(false, $"Could not open Terminal for Codex login: {macError}. Open a terminal and run: codex login");
        }

        var shellCommand = "codex login; echo; echo 'Return to ContextControl and click Refresh Codex.'; read -r -p 'Press Enter to close' _";
        string[][] linuxLaunchers =
        [
            ["x-terminal-emulator", "-e", "bash", "-lc", shellCommand],
            ["gnome-terminal", "--", "bash", "-lc", shellCommand],
            ["konsole", "-e", "bash", "-lc", shellCommand],
            ["xterm", "-e", "bash", "-lc", shellCommand]
        ];

        var lastError = "";
        foreach (var launcher in linuxLaunchers)
        {
            if (TryStartProcess(launcher[0], launcher.Skip(1).ToArray(), out lastError))
            {
                return new CodexLoginLaunchResult(true, "Opened Codex login in a terminal. Complete auth, then click Refresh Codex.");
            }
        }

        return new CodexLoginLaunchResult(false, $"Could not open a terminal for Codex login: {lastError}. Open a terminal and run: codex login");
    }

    public async Task<CodexLoginLaunchResult> RunDoctorAsync(
        IProgress<string>? terminal = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunCodexCommandAsync(["doctor"], TimeSpan.FromSeconds(30), cancellationToken);
            foreach (var line in InterestingLines(result.StandardOutput).Concat(InterestingLines(result.StandardError)).Take(80))
            {
                terminal?.Report(line);
            }

            var detail = FirstInterestingLine(result.StandardError)
                ?? FirstInterestingLine(result.StandardOutput)
                ?? $"codex doctor exited {result.ExitCode}";
            return result.ExitCode == 0
                ? new CodexLoginLaunchResult(true, $"Codex doctor passed: {detail}")
                : new CodexLoginLaunchResult(false, $"Codex doctor found an issue: {detail}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CodexLoginLaunchResult(false, $"Codex doctor failed: {ex.Message}");
        }
    }

    public async Task<CodexHarnessResult> SendAsync(
        CodexHarnessRequest request,
        IProgress<LocalLlmGenerationProgress>? progress = null,
        IProgress<string>? terminal = null,
        CancellationToken cancellationToken = default)
    {
        var root = string.IsNullOrWhiteSpace(request.ContextControlRoot)
            ? AppContext.BaseDirectory
            : request.ContextControlRoot;
        var plan = BuildExecutionPlan(root);
        Directory.CreateDirectory(plan.HarnessRoot);

        var prompt = BuildPrompt(request);
        await File.WriteAllTextAsync(plan.PromptPath, prompt, Utf8NoBom, cancellationToken);
        TryDelete(plan.LastMessagePath);

        progress?.Report(new LocalLlmGenerationProgress("Starting Codex CLI...", null, null, null, null, null, null, null, false));
        terminal?.Report("Codex harness uses an empty working directory and read-only sandbox.");
        terminal?.Report($"Codex capsule written: {plan.PromptPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "codex",
            WorkingDirectory = plan.HarnessRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            CreateNoWindow = true
        };

        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        var eventTrace = new List<string>();
        var thinking = new StringBuilder();
        var messageCandidates = new List<string>();

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Start();

            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();

            var stdoutTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    outputBuilder.AppendLine(line);
                    var parsed = ParseJsonEvent(line);
                    if (!string.IsNullOrWhiteSpace(parsed.TraceLine))
                    {
                        if (eventTrace.Count < MaxEventTraceLines)
                        {
                            eventTrace.Add(parsed.TraceLine);
                        }

                        terminal?.Report(parsed.TraceLine);
                    }

                    if (!string.IsNullOrWhiteSpace(parsed.Thinking))
                    {
                        if (thinking.Length > 0)
                        {
                            thinking.AppendLine();
                        }

                        thinking.AppendLine(parsed.Thinking.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(parsed.Message))
                    {
                        messageCandidates.Add(parsed.Message.Trim());
                    }

                    progress?.Report(new LocalLlmGenerationProgress(
                        string.IsNullOrWhiteSpace(parsed.TraceLine) ? "Codex event received..." : parsed.TraceLine,
                        parsed.Message,
                        null,
                        messageCandidates.Sum(candidate => Math.Max(1, candidate.Length / 4)),
                        null,
                        null,
                        null,
                        null,
                        false));
                }
            }, cancellationToken);

            var stderrTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    errorBuilder.AppendLine(line);
                    terminal?.Report(line);
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask);

            var finalMessage = await ReadLastMessageAsync(plan.LastMessagePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(finalMessage))
            {
                finalMessage = messageCandidates.LastOrDefault() ?? "";
            }

            var succeeded = process.ExitCode == 0 && !string.IsNullOrWhiteSpace(finalMessage);
            var status = succeeded
                ? "Codex response ready."
                : BuildFailureStatus(process.ExitCode, errorBuilder.ToString(), outputBuilder.ToString());

            progress?.Report(new LocalLlmGenerationProgress(status, null, null, null, null, null, null, null, true));
            return new CodexHarnessResult(
                succeeded,
                status,
                finalMessage.Trim(),
                thinking.ToString().Trim(),
                string.Join(Environment.NewLine, eventTrace),
                process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            var status = "Codex stopped by user.";
            progress?.Report(new LocalLlmGenerationProgress(status, null, null, null, null, null, null, null, true));
            return new CodexHarnessResult(false, status, "", "", string.Join(Environment.NewLine, eventTrace), -1);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var status = IsLoginRequiredText(ex.Message)
                ? "Codex login required. Click Login Codex, complete auth, then Refresh Codex."
                : $"Codex CLI failed: {ex.Message}";
            progress?.Report(new LocalLlmGenerationProgress(status, null, null, null, null, null, null, null, true));
            return new CodexHarnessResult(false, status, "", "", string.Join(Environment.NewLine, eventTrace), -1);
        }
        finally
        {
            process?.Dispose();
        }
    }

    public static string BuildPrompt(CodexHarnessRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ContextControl Codex harness capsule");
        builder.AppendLine();
        builder.AppendLine("You are running under ContextControl.");
        builder.AppendLine("Your working directory is an empty harness folder by design.");
        builder.AppendLine("The repository context you may use is included below as attachment text.");
        builder.AppendLine("Do not run repository navigation commands or read files outside the capsule for normal CC phases.");
        builder.AppendLine();
        builder.AppendLine($"Phase: {FormatPhase(request.Phase)}");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.CodexInstructions))
        {
            builder.AppendLine(request.CodexInstructions.Trim());
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.EnabledSkillbookInstructions))
        {
            builder.AppendLine("Enabled project/global Skillbook instructions:");
            builder.AppendLine(request.EnabledSkillbookInstructions.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("User request:");
        builder.AppendLine(string.IsNullOrWhiteSpace(request.UserMessage) ? "(empty)" : request.UserMessage.Trim());
        builder.AppendLine();

        var included = request.Attachments.Where(attachment => attachment.Included).ToArray();
        builder.AppendLine("Attachment inventory:");
        if (included.Length == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var attachment in included)
            {
                var text = attachment.Text ?? "";
                builder.AppendLine($"- {attachment.Label} ({attachment.Kind}) BODY_CHARS: {text.Length}; EST_TOKENS: {ContextCapsuleBuilder.EstimateTokens(text)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Included ContextControl attachments:");
        var remaining = MaxAttachmentCharacters;
        foreach (var attachment in included)
        {
            var text = attachment.Text ?? "";
            var clipped = text;
            if (remaining <= 0)
            {
                clipped = "";
            }
            else if (clipped.Length > remaining)
            {
                clipped = clipped[..remaining] + Environment.NewLine + ContextCapsuleBuilder.AttachmentClipMarker;
            }

            remaining -= Math.Max(0, clipped.Length);
            builder.AppendLine($"--- ATTACHMENT {attachment.Kind}: {attachment.Label}");
            builder.AppendLine(clipped.TrimEnd());
            builder.AppendLine($"--- END ATTACHMENT {attachment.Label}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public static CodexHarnessExecutionPlan BuildExecutionPlan(string contextControlRoot)
    {
        var root = string.IsNullOrWhiteSpace(contextControlRoot)
            ? AppContext.BaseDirectory
            : contextControlRoot;
        var harnessRoot = Path.Combine(root, ".tmp", "codex-harness");
        var lastMessagePath = Path.Combine(harnessRoot, "last-codex-message.md");
        return new CodexHarnessExecutionPlan(
            harnessRoot,
            Path.Combine(harnessRoot, "last-codex-capsule.md"),
            lastMessagePath,
            [
                "exec",
                "--json",
                "--ephemeral",
                "--sandbox",
                "read-only",
                "--ask-for-approval",
                "never",
                "--skip-git-repo-check",
                "--ignore-rules",
                "-C",
                harnessRoot,
                "-o",
                lastMessagePath,
                "-"
            ]);
    }

    private static async Task<string> ReadLastMessageAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken)
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string BuildFailureStatus(int exitCode, string error, string output)
    {
        var combined = $"{error}{Environment.NewLine}{output}";
        if (IsLoginRequiredText(combined))
        {
            return "Codex login required. Click Login Codex, complete auth, then Refresh Codex.";
        }

        var detail = FirstInterestingLine(error) ?? FirstInterestingLine(output);
        return string.IsNullOrWhiteSpace(detail)
            ? $"Codex exited with code {exitCode} and no final message."
            : $"Codex exited with code {exitCode}: {detail}";
    }

    private static string? FirstInterestingLine(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !line.StartsWith("{", StringComparison.Ordinal));
    }

    public static bool IsLoginRequiredText(string text)
    {
        var clean = text ?? "";
        return clean.Contains("not logged in", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("login required", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("please login", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("please log in", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("authenticate", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("access token", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("refresh token", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("auth token", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("api key", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("credential", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoggedInStatus(string text)
    {
        var clean = text ?? "";
        return clean.Contains("logged in", StringComparison.OrdinalIgnoreCase)
            && !clean.Contains("not logged in", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CodexCommandResult> RunCodexCommandAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "codex",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedToken.CancelAfter(timeout);
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(linkedToken.Token);
        var errorTask = process.StandardError.ReadToEndAsync(linkedToken.Token);
        await process.WaitForExitAsync(linkedToken.Token);
        return new CodexCommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static bool TryStartProcess(string fileName, IReadOnlyList<string> arguments, out string error)
    {
        error = "";
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static IEnumerable<string> InterestingLines(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length <= 240);
    }

    private static ParsedCodexEvent ParseJsonEvent(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return new ParsedCodexEvent("", "", CleanTrace(line));
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = ReadString(root, "type");
            var text = ExtractText(root);
            var trace = BuildTraceLine(type, root, text);
            var isReasoning = type.Contains("reason", StringComparison.OrdinalIgnoreCase)
                || text.Contains("thinking", StringComparison.OrdinalIgnoreCase);
            var isMessage = type.Contains("message", StringComparison.OrdinalIgnoreCase)
                || type.Contains("final", StringComparison.OrdinalIgnoreCase)
                || type.Contains("completed", StringComparison.OrdinalIgnoreCase);

            return new ParsedCodexEvent(
                isMessage ? text : "",
                isReasoning ? text : "",
                trace);
        }
        catch
        {
            return new ParsedCodexEvent("", "", CleanTrace(line));
        }
    }

    private static string BuildTraceLine(string type, JsonElement root, string text)
    {
        var cleanType = string.IsNullOrWhiteSpace(type) ? "codex.event" : type;
        var status = ReadString(root, "status");
        var title = string.IsNullOrWhiteSpace(status) ? cleanType : $"{cleanType} {status}";
        if (string.IsNullOrWhiteSpace(text))
        {
            return title;
        }

        return $"{title}: {CleanTrace(text)}";
    }

    private static string ExtractText(JsonElement element)
    {
        var builder = new StringBuilder();
        ExtractText(element, builder, depth: 0);
        return builder.ToString().Trim();
    }

    private static void ExtractText(JsonElement element, StringBuilder builder, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("text")
                        || property.NameEquals("delta")
                        || property.NameEquals("message")
                        || property.NameEquals("summary"))
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            AppendExtractedText(builder, property.Value.GetString());
                            continue;
                        }
                    }

                    if (property.NameEquals("content")
                        || property.NameEquals("item")
                        || property.NameEquals("output")
                        || property.NameEquals("reasoning"))
                    {
                        ExtractText(property.Value, builder, depth + 1);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ExtractText(item, builder, depth + 1);
                }

                break;
        }
    }

    private static void AppendExtractedText(StringBuilder builder, string? text)
    {
        var clean = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(clean);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }

    private static string CleanTrace(string? text)
    {
        var clean = (text ?? "")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return clean.Length <= 220 ? clean : clean[..220] + " ...";
    }

    private static string FormatPhase(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => "file-request",
            ContextCapsulePhase.SourceAudit => "source-audit",
            ContextCapsulePhase.PatchWrite => "patch-write",
            ContextCapsulePhase.PatchReview => "patch-review",
            _ => "chat"
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Stale diagnostic output is non-fatal; the next run will still use stdout candidates.
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Cancellation cleanup is best-effort.
        }
    }

    private sealed record ParsedCodexEvent(string Message, string Thinking, string TraceLine);

    private sealed record CodexCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
