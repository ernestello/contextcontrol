// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class ContextControlViewModel : ObservableObject
{
    private const double CompactPromptBarHeight = 206;
    private const double MaximumPromptBarHeight = 360;
    private const double PromptLineHeight = 18;
    private const int CompactPromptLines = 4;
    private const int EstimatedPromptWrapColumn = 82;
    private static readonly HashSet<string> AutoAttachmentKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "dir",
        "code",
        "patch"
    };

    private readonly WorkbenchSettings _settings;
    private readonly ContextControlProcessService _processService;
    private readonly ContextPromptBuilder _promptBuilder;
    private readonly IAiConnectionService _apiConnection;
    private readonly IAiConnectionService _browserConnection;
    private Func<string, Task>? _clipboardWriter;
    private bool _isBusy;
    private bool _isPromptOpen;
    private bool _isPromptTypingActive;
    private bool _isPatchPlanReady;
    private bool _isLogPanelOpen;
    private string _phaseTitle = "Ready";
    private string _phaseDetail = "Open the prompt with Space, then run DIR for a fresh request.";
    private string _promptText = "";
    private string _selectedRoute;
    private string _providerStatus = "Browser route selected";
    private string _lastExportPath = "";
    private string _patchSummary = "No patch loaded.";

    public ContextControlViewModel(WorkbenchSettings settings)
    {
        _settings = settings;
        _processService = new ContextControlProcessService(settings.ContextControlRoot);
        _promptBuilder = new ContextPromptBuilder();
        _apiConnection = new ApiAiConnectionService();
        _browserConnection = new BrowserAiConnectionService();

        RouteOptions =
        [
            "Browser: ChatGPT",
            "Browser: DeepSeek",
            "Browser: Claude",
            "API: OpenAI",
            "API: Custom"
        ];

        _selectedRoute = RouteOptions.Contains(settings.SelectedAiRoute)
            ? settings.SelectedAiRoute
            : RouteOptions[0];

        IsPromptOpen = settings.PromptBarOpenByDefault;

        RunDirCommand = new RelayCommand<object>(_ => _ = RunDirAsync(), _ => !IsBusy);
        RunCcCommand = new RelayCommand<object>(_ => _ = RunCcAsync(), _ => !IsBusy);
        RunGoCommand = new RelayCommand<object>(_ => _ = RunGoPreviewAsync(), _ => !IsBusy);
        ApplyPatchCommand = new RelayCommand<object>(_ => _ = ApplyPatchAsync(), _ => !IsBusy && IsPatchPlanReady);
        SendCommand = new RelayCommand<object>(_ => _ = SendAsync(), _ => !IsBusy);
        ToggleLogPanelCommand = new RelayCommand<object>(_ => ToggleLogPanel());
        OpenPromptCommand = new RelayCommand<object>(_ => IsPromptOpen = true);
        ClosePromptCommand = new RelayCommand<object>(_ => IsPromptOpen = false);
        TogglePromptCommand = new RelayCommand<object>(_ => IsPromptOpen = !IsPromptOpen);

        Log("info", $"Context root: {_processService.ContextRoot}");
    }

    public ObservableCollection<string> RouteOptions { get; }
    public ObservableCollection<ContextControlAttachmentViewModel> Attachments { get; } = [];
    public ObservableCollection<ContextControlLogEntryViewModel> LogEntries { get; } = [];
    public bool HasAttachments => Attachments.Count > 0;
    public ICommand RunDirCommand { get; }
    public ICommand RunCcCommand { get; }
    public ICommand RunGoCommand { get; }
    public ICommand ApplyPatchCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand ToggleLogPanelCommand { get; }
    public ICommand OpenPromptCommand { get; }
    public ICommand ClosePromptCommand { get; }
    public ICommand TogglePromptCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsPromptOpen
    {
        get => _isPromptOpen;
        set
        {
            if (SetProperty(ref _isPromptOpen, value))
            {
                if (!value)
                {
                    IsPromptTypingActive = false;
                }

                OnPropertyChanged(nameof(PromptBarHeight));
                OnPropertyChanged(nameof(PromptBarOpacity));
                _settings.PromptBarOpenByDefault = value;
                SaveSettingsQuietly();
            }
        }
    }

    public double PromptBarHeight => IsPromptOpen
        ? IsPromptTypingActive ? CalculatePromptBarHeight() : CompactPromptBarHeight
        : 0;

    public double PromptBarOpacity => IsPromptOpen ? 1 : 0;

    public bool IsPromptTypingActive
    {
        get => _isPromptTypingActive;
        private set
        {
            if (SetProperty(ref _isPromptTypingActive, value))
            {
                OnPropertyChanged(nameof(PromptBarHeight));
            }
        }
    }

    public bool IsLogPanelOpen
    {
        get => _isLogPanelOpen;
        private set => SetProperty(ref _isLogPanelOpen, value);
    }

    public bool IsPatchPlanReady
    {
        get => _isPatchPlanReady;
        private set
        {
            if (SetProperty(ref _isPatchPlanReady, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string PhaseTitle
    {
        get => _phaseTitle;
        private set => SetProperty(ref _phaseTitle, value);
    }

    public string PhaseDetail
    {
        get => _phaseDetail;
        private set => SetProperty(ref _phaseDetail, value);
    }

    public string PromptText
    {
        get => _promptText;
        set
        {
            if (SetProperty(ref _promptText, value ?? ""))
            {
                OnPropertyChanged(nameof(PromptBarHeight));
            }
        }
    }

    public string SelectedRoute
    {
        get => _selectedRoute;
        set
        {
            var clean = string.IsNullOrWhiteSpace(value) ? RouteOptions[0] : value;
            if (SetProperty(ref _selectedRoute, clean))
            {
                _settings.SelectedAiRoute = clean;
                ProviderStatus = clean.StartsWith("API:", StringComparison.OrdinalIgnoreCase)
                    ? "API profile selected"
                    : "Browser profile selected";
                SaveSettingsQuietly();
            }
        }
    }

    public string ProviderStatus
    {
        get => _providerStatus;
        private set => SetProperty(ref _providerStatus, value);
    }

    public string LastExportPath
    {
        get => _lastExportPath;
        private set => SetProperty(ref _lastExportPath, value);
    }

    public string PatchSummary
    {
        get => _patchSummary;
        private set => SetProperty(ref _patchSummary, value);
    }

    public string AttachmentSummary => Attachments.Count == 0
        ? "No attachments"
        : $"{Attachments.Count} attachment(s)";

    public string ContextRootPath => _processService.ContextRoot;

    public string ContextRootLabel => string.IsNullOrWhiteSpace(_processService.ContextRoot)
        ? "context root not resolved"
        : _processService.ContextRoot;

    public void SetClipboardWriter(Func<string, Task> clipboardWriter)
    {
        _clipboardWriter = clipboardWriter;
    }

    public void OpenPrompt()
    {
        IsPromptOpen = true;
    }

    public void ClosePrompt()
    {
        IsPromptOpen = false;
    }

    public void SetPromptTypingActive(bool isActive)
    {
        IsPromptTypingActive = IsPromptOpen && isActive;
    }

    public int AttachFiles(IEnumerable<string> filePaths)
    {
        var attachedCount = 0;

        foreach (var candidatePath in filePaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidatePath);
            }
            catch
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            if (AddAttachment(Path.GetFileName(fullPath), fullPath, "file"))
            {
                attachedCount++;
            }
        }

        if (attachedCount > 0)
        {
            PhaseTitle = attachedCount == 1 ? "1 file attached" : $"{attachedCount} files attached";
            PhaseDetail = "Attachment list updated from dropped files.";
        }

        return attachedCount;
    }

    public async Task CopyCodeContextAsync(IEnumerable<string> requestLines, string sourceLabel)
    {
        await RunBusyAsync("Copy context", async () =>
        {
            var lines = requestLines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (lines.Length == 0)
            {
                PhaseTitle = "Nothing to copy";
                PhaseDetail = "The selected tree item has no shown files.";
                Log("warn", "Copy context cancelled: no request lines.");
                return;
            }

            PhaseTitle = "Copying context";
            PhaseDetail = $"Exporting {lines.Length} {sourceLabel} path(s).";
            var result = await _processService.RunCodeExportAsync(lines);
            LogResult(result);

            if (!result.Succeeded)
            {
                PhaseTitle = "Copy failed";
                PhaseDetail = FirstErrorLine(result);
                return;
            }

            var text = await _processService.ReadOutputFileAsync(_processService.CodeExportPath);
            if (_clipboardWriter is not null)
            {
                await _clipboardWriter(text);
                Log("info", "Context copied to clipboard.");
            }

            AddAttachment(Path.GetFileName(_processService.CodeExportPath), _processService.CodeExportPath, "code");
            LastExportPath = _processService.CodeExportPath;
            PhaseTitle = "Context copied";
            PhaseDetail = $"{lines.Length} path(s) exported and copied.";
        });
    }

    public void RemoveAttachment(ContextControlAttachmentViewModel? attachment)
    {
        if (attachment is null || !Attachments.Remove(attachment))
        {
            return;
        }

        NotifyAttachmentStateChanged();
    }

    private void ToggleLogPanel()
    {
        IsLogPanelOpen = !IsLogPanelOpen;
    }

    private double CalculatePromptBarHeight()
    {
        var visualLines = EstimatePromptVisualLineCount(PromptText);
        var overflowLines = Math.Max(0, visualLines - CompactPromptLines);
        var desiredHeight = CompactPromptBarHeight + overflowLines * PromptLineHeight;
        return Math.Min(MaximumPromptBarHeight, desiredHeight);
    }

    private static int EstimatePromptVisualLineCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var count = 0;
        foreach (var line in normalized.Split('\n'))
        {
            count += Math.Max(1, (int)Math.Ceiling(line.Length / (double)EstimatedPromptWrapColumn));
        }

        return count;
    }

    private async Task RunDirAsync()
    {
        await RunBusyAsync("DIR export", async () =>
        {
            PhaseTitle = "DIR export";
            PhaseDetail = "Exporting the project tree and navigation prompt.";
            var result = await _processService.RunDirectoryExportAsync();
            LogResult(result);

            if (!result.Succeeded)
            {
                PhaseTitle = "DIR failed";
                PhaseDetail = FirstErrorLine(result);
                return;
            }

            AddAttachment(Path.GetFileName(_processService.DirectoryExportPath), _processService.DirectoryExportPath, "dir");
            LastExportPath = _processService.DirectoryExportPath;
            PromptText = StripLegacyDirPayload(PromptText);
            PhaseTitle = "Waiting for scope";
            PhaseDetail = "DIR export attached. Send your prompt; request only the smallest safe file/function/FIND list for CC.";
        });
    }

    private async Task RunCcAsync()
    {
        await RunBusyAsync("CC export", async () =>
        {
            var requestLines = _promptBuilder.BuildCodeExportRequestLines(PromptText);
            if (requestLines.Count == 0)
            {
                PhaseTitle = "CC needs input";
                PhaseDetail = "Paste the AI-requested file/function/FIND list into the prompt bar first.";
                Log("warn", "CC cancelled: no request lines.");
                return;
            }

            PhaseTitle = "CC export";
            PhaseDetail = "Exporting selected source/function context.";
            var result = await _processService.RunCodeExportAsync(requestLines);
            LogResult(result);

            if (!result.Succeeded)
            {
                PhaseTitle = "CC failed";
                PhaseDetail = FirstErrorLine(result);
                return;
            }

            AddAttachment(Path.GetFileName(_processService.CodeExportPath), _processService.CodeExportPath, "code");
            LastExportPath = _processService.CodeExportPath;
            PromptText = StripLegacyDirPayload(PromptText);
            PhaseTitle = "Context ready";
            PhaseDetail = "Send the CC export to the AI. The next complete answer should include CC-REPLACE patch blocks.";
        });
    }

    private async Task RunGoPreviewAsync()
    {
        await RunBusyAsync("GO preview", async () =>
        {
            var patchText = _promptBuilder.ExtractPatchBlocks(PromptText);
            if (string.IsNullOrWhiteSpace(patchText))
            {
                PhaseTitle = "GO needs patch";
                PhaseDetail = "Paste an AI answer containing BEGIN/END CC-REPLACE blocks.";
                Log("warn", "GO preview cancelled: no CC-REPLACE blocks found.");
                return;
            }

            PhaseTitle = "GO preview";
            PhaseDetail = "Writing patch.txt and asking ccReplace for a non-writing plan.";
            await _processService.WritePatchAsync(patchText);
            AddAttachment(Path.GetFileName(_processService.PatchPath), _processService.PatchPath, "patch");

            var result = await _processService.PreviewPatchAsync();
            LogResult(result);
            IsPatchPlanReady = result.Succeeded;
            PatchSummary = result.Succeeded
                ? _promptBuilder.BuildPatchSummary(result.StandardOutput)
                : FirstErrorLine(result);
            PhaseTitle = result.Succeeded ? "Patch planned" : "Patch preview failed";
            PhaseDetail = result.Succeeded
                ? "Review the plan, then apply effective edits from the dock."
                : FirstErrorLine(result);
        });
    }

    private async Task ApplyPatchAsync()
    {
        await RunBusyAsync("GO apply", async () =>
        {
            PhaseTitle = "Applying patch";
            PhaseDetail = "Applying effective edits through ccReplace.";
            var result = await _processService.ApplyPatchAsync("effective");
            LogResult(result);
            IsPatchPlanReady = false;
            PhaseTitle = result.Succeeded ? "Patch applied" : "Patch failed";
            PhaseDetail = result.Succeeded ? "ccReplace applied the effective edits." : FirstErrorLine(result);
        });
    }

    private async Task SendAsync()
    {
        await RunBusyAsync("Send prompt", async () =>
        {
            var message = PromptText.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                PhaseTitle = "Nothing to send";
                PhaseDetail = "Write or generate a prompt first.";
                return;
            }

            var request = new AiSendRequest(SelectedRoute, message, Attachments.Select(item => item.Path).ToArray());
            var service = SelectedRoute.StartsWith("API:", StringComparison.OrdinalIgnoreCase)
                ? _apiConnection
                : _browserConnection;
            var result = await service.SendAsync(request);

            if (result.PreparedMessage is not null && _clipboardWriter is not null)
            {
                await _clipboardWriter(result.PreparedMessage);
                Log("info", "Prepared message copied to clipboard.");
            }

            ProviderStatus = result.Status;
            PhaseTitle = result.Succeeded ? "Prompt prepared" : "Route needs setup";
            PhaseDetail = result.Status;
            Log(result.Succeeded ? "info" : "warn", result.Status);
        });
    }

    private async Task RunBusyAsync(string label, Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Log("run", label);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            PhaseTitle = $"{label} failed";
            PhaseDetail = ex.Message;
            Log("error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool AddAttachment(string label, string path, string kind)
    {
        var safePath = path ?? "";
        var safeKind = kind ?? "";
        var safeLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileName(safePath)
            : label;

        var existing = AutoAttachmentKinds.Contains(safeKind)
            ? Attachments.FirstOrDefault(item => string.Equals(item.Kind, safeKind, StringComparison.OrdinalIgnoreCase))
            : Attachments.FirstOrDefault(item =>
                string.Equals(item.Kind, safeKind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Path, safePath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            Attachments.Add(new ContextControlAttachmentViewModel(safeLabel, safePath, safeKind));
            NotifyAttachmentStateChanged();
            return true;
        }

        var changed = !string.Equals(existing.Path, safePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.Kind, safeKind, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.Label, safeLabel, StringComparison.Ordinal);
        existing.Update(safeLabel, safePath, safeKind);
        return changed;
    }

    private void NotifyAttachmentStateChanged()
    {
        OnPropertyChanged(nameof(AttachmentSummary));
        OnPropertyChanged(nameof(HasAttachments));
    }

    private void LogResult(ContextControlCommandResult result)
    {
        Log(result.Succeeded ? "ok" : "fail", $"{result.Command} exited {result.ExitCode}");
        foreach (var line in InterestingLines(result.StandardOutput).Take(5))
        {
            Log("out", line);
        }

        foreach (var line in InterestingLines(result.StandardError).Take(3))
        {
            Log("err", line);
        }
    }

    private void Log(string level, string message)
    {
        LogEntries.Insert(0, new ContextControlLogEntryViewModel(level, message));
        while (LogEntries.Count > 80)
        {
            LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }

    private static IEnumerable<string> InterestingLines(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length <= 220);
    }

    private static string FirstErrorLine(ContextControlCommandResult result)
    {
        return InterestingLines(result.StandardError).FirstOrDefault()
            ?? InterestingLines(result.StandardOutput).FirstOrDefault()
            ?? $"{result.Command} exited {result.ExitCode}.";
    }

    private static string StripLegacyDirPayload(string promptText)
    {
        var text = promptText ?? "";
        const string header = "Context Control fresh-chat request";
        const string userRequestPrefix = "User request:";
        const string dirPrefix = "DIR export:";

        if (!text.Contains(header, StringComparison.Ordinal))
        {
            return text;
        }

        var userRequestIndex = text.IndexOf(userRequestPrefix, StringComparison.Ordinal);
        if (userRequestIndex < 0)
        {
            return text;
        }

        var userBodyStart = userRequestIndex + userRequestPrefix.Length;
        var dirIndex = text.IndexOf(dirPrefix, userBodyStart, StringComparison.Ordinal);
        var userBodyEnd = dirIndex >= 0 ? dirIndex : text.Length;
        var userRequest = text[userBodyStart..userBodyEnd].Trim();

        return string.Equals(userRequest, "(no request text yet)", StringComparison.Ordinal)
            ? ""
            : userRequest;
    }

    private void RaiseCommandStates()
    {
        (RunDirCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RunCcCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RunGoCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (ApplyPatchCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (SendCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
    }

    private void SaveSettingsQuietly()
    {
        try
        {
            _settings.Save();
        }
        catch
        {
            // UI state should stay usable even if the settings file is locked.
        }
    }
}
