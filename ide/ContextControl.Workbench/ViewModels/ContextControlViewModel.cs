// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class ContextControlViewModel : ObservableObject
{
    private const double CompactPromptBarHeight = 206;
    private const double MaximumPromptBarHeight = 360;
    private const double PromptLineHeight = 18;
    private const int CompactPromptLines = 4;
    private const int EstimatedPromptWrapColumn = 82;
    private const int LargePromptCharacterThreshold = 24_000;
    private const int LargePromptLineThreshold = 600;
    private const int PromptHeightEstimationCharacterLimit = 12_000;
    private const int PromptHeightEstimationLineLimit = 240;
    private static readonly int PromptVisualLinesForMaximumHeight =
        CompactPromptLines + (int)Math.Ceiling((MaximumPromptBarHeight - CompactPromptBarHeight) / PromptLineHeight);
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
    private readonly LocalLlmService _localLlmService;
    private readonly SkillbookService _skillbookService;
    private readonly ContextCapsuleBuilder _capsuleBuilder;
    private readonly ChatHistoryService _chatHistoryService;
    private Func<string, Task>? _clipboardWriter;
    private bool _isBusy;
    private bool _isPromptOpen;
    private bool _isPromptTypingActive;
    private bool _isLargePrompt;
    private bool _isPatchPlanReady;
    private bool _isRefreshingLocalModels;
    private bool _isInstallingOllama;
    private bool _isOllamaInstalled;
    private bool _isOllamaInstallerPromptOpen;
    private bool _ollamaInstallerPromptShown;
    private bool _isTransferProgressActive;
    private bool _isTransferProgressIndeterminate = true;
    private double _transferProgressValue;
    private string _dockPanelKey = "log";
    private string _promptModeKey;
    private string _phaseTitle = "Ready";
    private string _phaseDetail = "Open the prompt with Space, then run DIR for a fresh request.";
    private string _transferProgressTitle = "";
    private string _transferProgressStatus = "";
    private string _transferProgressSizeLabel = "";
    private string _transferProgressSpeedLabel = "";
    private string _transferProgressPercentLabel = "";
    private string _terminalOutputText = "";
    private DateTime? _generationStartedAt;
    private string _promptText = "";
    private string _selectedRoute;
    private LocalLlmModelViewModel? _selectedLocalModel;
    private string _providerStatus = "Browser route selected";
    private string _hardwareSummary = "Detecting GPU...";
    private string _localLlmStatus = "Local model scan pending.";
    private string _lastAssistantPatchBlocks = "";
    private string _lastUserRequest = "";
    private string _fileRequestModelId;
    private string _patchWriteModelId;
    private string _patchReviewModelId;
    private string _chatModelId;
    private string _lastExportPath = "";
    private string _patchSummary = "No patch loaded.";
    private string _activeProjectRoot = "";
    private string _activeProjectRulesPath = "";
    private ChatSessionViewModel? _selectedChatSession;

    public ContextControlViewModel(WorkbenchSettings settings)
    {
        _settings = settings;
        _processService = new ContextControlProcessService(settings.ContextControlRoot);
        _promptBuilder = new ContextPromptBuilder();
        _apiConnection = new ApiAiConnectionService();
        _browserConnection = new BrowserAiConnectionService();
        _localLlmService = new LocalLlmService();
        _skillbookService = new SkillbookService(settings.ContextControlRoot);
        _capsuleBuilder = new ContextCapsuleBuilder();
        _chatHistoryService = new ChatHistoryService(settings.ContextControlRoot);
        _fileRequestModelId = settings.FileRequestModel;
        _patchWriteModelId = settings.PatchWriteModel;
        _patchReviewModelId = settings.PatchReviewModel;
        _chatModelId = settings.ChatModel;

        RouteOptions =
        [
            "Browser: ChatGPT",
            "Browser: DeepSeek",
            "Browser: Claude",
            "Local: Ollama",
            "API: OpenAI",
            "API: Custom"
        ];

        _selectedRoute = RouteOptions.Contains(settings.SelectedAiRoute)
            ? settings.SelectedAiRoute
            : RouteOptions[0];
        _promptModeKey = string.Equals(settings.PromptModeKey, "terminal", StringComparison.OrdinalIgnoreCase)
            ? "terminal"
            : "context";

        IsPromptOpen = settings.PromptBarOpenByDefault;
        LocalLlmModels = new ObservableCollection<LocalLlmModelViewModel>(
            LocalLlmService.Catalog.Select(model => new LocalLlmModelViewModel(model)));
        LocalModelIdOptions = new ObservableCollection<string>(LocalLlmService.Catalog.Select(model => model.Id));
        SkillbookEntries = new ObservableCollection<SkillbookEntryViewModel>(
            _skillbookService.LoadEntries().Select(entry => new SkillbookEntryViewModel(entry)));
        ChatSessions = [];

        RunDirCommand = new RelayCommand<object>(_ => _ = RunDirAsync(), _ => !IsBusy);
        RunCcCommand = new RelayCommand<object>(_ => _ = RunCcAsync(), _ => !IsBusy);
        RunGoCommand = new RelayCommand<object>(_ => _ = RunGoPreviewAsync(), _ => !IsBusy);
        ApplyPatchCommand = new RelayCommand<object>(_ => _ = ApplyPatchAsync(), _ => !IsBusy && IsPatchPlanReady);
        ApplyAllPatchCommand = new RelayCommand<object>(_ => _ = ApplyPatchAsync("all"), _ => !IsBusy && IsPatchPlanReady);
        SendCommand = new RelayCommand<object>(_ => _ = SendAsync(), _ => !IsBusy);
        ToggleLogPanelCommand = new RelayCommand<object>(_ => SelectDockPanel("log"));
        SelectLogPanelCommand = new RelayCommand<object>(_ => SelectDockPanel("log"));
        SelectChatPanelCommand = new RelayCommand<object>(_ => SelectDockPanel("chat"));
        RefreshLocalModelsCommand = new RelayCommand<object>(_ => _ = RefreshLocalModelsAsync(), _ => !IsRefreshingLocalModels);
        InstallOllamaCommand = new RelayCommand<object>(_ => _ = InstallOllamaAsync(), _ => !IsOllamaInstalled && !IsBusy && !IsRefreshingLocalModels && !IsInstallingOllama);
        OpenOllamaDownloadPageCommand = new RelayCommand<object>(_ => OpenOllamaDownloadPage());
        PullLocalModelCommand = new RelayCommand<LocalLlmModelViewModel>(
            model => _ = PullLocalModelAsync(model),
            model => model is { CanPull: true } && !IsBusy && !IsRefreshingLocalModels && !IsInstallingOllama);
        SwitchPromptToContextCommand = new RelayCommand<object>(_ => PromptModeKey = "context");
        SwitchPromptToChatCommand = new RelayCommand<object>(_ => PromptModeKey = "context");
        SwitchPromptToTerminalCommand = new RelayCommand<object>(_ => PromptModeKey = "terminal");
        ClearTerminalCommand = new RelayCommand<object>(_ => TerminalOutputText = "");
        CopySnippetCommand = new RelayCommand<ChatSnippetViewModel>(snippet => _ = CopySnippetAsync(snippet));
        CopyChatTextCommand = new RelayCommand<LocalLlmChatPartViewModel>(part => _ = CopyChatTextAsync(part));
        UseSnippetForCcCommand = new RelayCommand<ChatSnippetViewModel>(UseSnippetForCc);
        PreviewSnippetCommand = new RelayCommand<ChatSnippetViewModel>(snippet => _ = PreviewSnippetAsync(snippet), snippet => snippet?.IsPatch == true);
        ToggleAttachmentIncludeCommand = new RelayCommand<ContextControlAttachmentViewModel>(ToggleAttachmentInclude);
        ToggleThinkingCommand = new RelayCommand<LocalLlmChatMessageViewModel>(message => message?.ToggleThinking());
        ToggleSnippetCommand = new RelayCommand<ChatSnippetViewModel>(snippet => snippet?.ToggleExpanded());
        NewChatSessionCommand = new RelayCommand<object>(_ => CreateNewChatSession());
        SelectChatSessionCommand = new RelayCommand<ChatSessionViewModel>(SelectChatSession);
        RemoveChatSessionCommand = new RelayCommand<ChatSessionViewModel>(RemoveChatSession);
        CloseOllamaInstallerPromptCommand = new RelayCommand<object>(_ => IsOllamaInstallerPromptOpen = false);
        OpenPromptCommand = new RelayCommand<object>(_ => IsPromptOpen = true);
        ClosePromptCommand = new RelayCommand<object>(_ => IsPromptOpen = false);
        TogglePromptCommand = new RelayCommand<object>(_ => IsPromptOpen = !IsPromptOpen);

        Log("info", $"Context root: {_processService.ContextRoot}");
        LoadChatHistory();
        _ = RefreshLocalModelsAsync();
    }

    public ObservableCollection<string> RouteOptions { get; }
    public ObservableCollection<ContextControlAttachmentViewModel> Attachments { get; } = [];
    public ObservableCollection<ContextControlLogEntryViewModel> LogEntries { get; } = [];
    public ObservableCollection<LocalLlmModelViewModel> LocalLlmModels { get; }
    public ObservableCollection<LocalLlmModelViewModel> InstalledLocalModels { get; } = [];
    public ObservableCollection<string> LocalModelIdOptions { get; }
    public ObservableCollection<SkillbookEntryViewModel> SkillbookEntries { get; }
    public ObservableCollection<ChatSessionViewModel> ChatSessions { get; }
    public ObservableCollection<LocalLlmChatMessageViewModel> ChatMessages { get; } = [];
    public ObservableCollection<PatchPlanActionViewModel> PatchPlanActions { get; } = [];
    public bool HasAttachments => Attachments.Count > 0;
    public bool HasInstalledLocalModels => InstalledLocalModels.Count > 0;
    public bool HasChatSessions => ChatSessions.Count > 0;
    public bool HasPatchPlanActions => PatchPlanActions.Count > 0;
    public string ChatHistorySummary => $"{ChatSessions.Count:N0} chat(s)";

    public ChatSessionViewModel? SelectedChatSession
    {
        get => _selectedChatSession;
        private set => SetProperty(ref _selectedChatSession, value);
    }

    public ICommand RunDirCommand { get; }
    public ICommand RunCcCommand { get; }
    public ICommand RunGoCommand { get; }
    public ICommand ApplyPatchCommand { get; }
    public ICommand ApplyAllPatchCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand ToggleLogPanelCommand { get; }
    public ICommand SelectLogPanelCommand { get; }
    public ICommand SelectChatPanelCommand { get; }
    public ICommand RefreshLocalModelsCommand { get; }
    public ICommand InstallOllamaCommand { get; }
    public ICommand OpenOllamaDownloadPageCommand { get; }
    public ICommand PullLocalModelCommand { get; }
    public ICommand SwitchPromptToContextCommand { get; }
    public ICommand SwitchPromptToChatCommand { get; }
    public ICommand SwitchPromptToTerminalCommand { get; }
    public ICommand ClearTerminalCommand { get; }
    public ICommand CopySnippetCommand { get; }
    public ICommand CopyChatTextCommand { get; }
    public ICommand UseSnippetForCcCommand { get; }
    public ICommand PreviewSnippetCommand { get; }
    public ICommand ToggleAttachmentIncludeCommand { get; }
    public ICommand ToggleThinkingCommand { get; }
    public ICommand ToggleSnippetCommand { get; }
    public ICommand NewChatSessionCommand { get; }
    public ICommand SelectChatSessionCommand { get; }
    public ICommand RemoveChatSessionCommand { get; }
    public ICommand CloseOllamaInstallerPromptCommand { get; }
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
                OnPropertyChanged(nameof(ShowBusyPromptProgress));
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
        ? CalculatePromptBarBaseHeight() + (IsTransferProgressActive ? 54 : 0)
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

    public bool IsLogPanelOpen => string.Equals(_dockPanelKey, "log", StringComparison.OrdinalIgnoreCase);

    public bool IsChatPanelOpen => string.Equals(_dockPanelKey, "chat", StringComparison.OrdinalIgnoreCase);

    public bool IsRefreshingLocalModels
    {
        get => _isRefreshingLocalModels;
        private set
        {
            if (SetProperty(ref _isRefreshingLocalModels, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsInstallingOllama
    {
        get => _isInstallingOllama;
        private set
        {
            if (SetProperty(ref _isInstallingOllama, value))
            {
                OnPropertyChanged(nameof(OllamaInstallButtonLabel));
                RaiseCommandStates();
            }
        }
    }

    public bool IsOllamaInstalled
    {
        get => _isOllamaInstalled;
        private set
        {
            if (SetProperty(ref _isOllamaInstalled, value))
            {
                OnPropertyChanged(nameof(OllamaInstallButtonLabel));
                RaiseCommandStates();
            }
        }
    }

    public bool IsOllamaInstallerPromptOpen
    {
        get => _isOllamaInstallerPromptOpen;
        private set => SetProperty(ref _isOllamaInstallerPromptOpen, value);
    }

    public string OllamaInstallerPromptMessage =>
        "Ollama has finished downloading, please find the setup window and complete the installation to proceed.";

    public string OllamaInstallButtonLabel => IsOllamaInstalled
        ? "Ollama installed"
        : IsInstallingOllama ? "Installing" : "Install Ollama";

    public string OllamaInstallerSizeLabel => $"{LocalLlmService.OllamaInstallerSize}; models below download separately";

    public string OllamaInstallCommandLabel => LocalLlmService.OllamaWindowsInstallCommand;

    public bool ShowBusyPromptProgress => IsBusy && !IsTransferProgressActive;

    public bool IsTransferProgressActive
    {
        get => _isTransferProgressActive;
        private set
        {
            if (SetProperty(ref _isTransferProgressActive, value))
            {
                OnPropertyChanged(nameof(ShowBusyPromptProgress));
                OnPropertyChanged(nameof(PromptBarHeight));
            }
        }
    }

    public bool IsTransferProgressIndeterminate
    {
        get => _isTransferProgressIndeterminate;
        private set => SetProperty(ref _isTransferProgressIndeterminate, value);
    }

    public double TransferProgressValue
    {
        get => _transferProgressValue;
        private set => SetProperty(ref _transferProgressValue, Math.Clamp(value, 0, 100));
    }

    public string TransferProgressTitle
    {
        get => _transferProgressTitle;
        private set => SetProperty(ref _transferProgressTitle, value);
    }

    public string TransferProgressStatus
    {
        get => _transferProgressStatus;
        private set => SetProperty(ref _transferProgressStatus, value);
    }

    public string TransferProgressSizeLabel
    {
        get => _transferProgressSizeLabel;
        private set => SetProperty(ref _transferProgressSizeLabel, value);
    }

    public string TransferProgressSpeedLabel
    {
        get => _transferProgressSpeedLabel;
        private set => SetProperty(ref _transferProgressSpeedLabel, value);
    }

    public string TransferProgressPercentLabel
    {
        get => _transferProgressPercentLabel;
        private set => SetProperty(ref _transferProgressPercentLabel, value);
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
            var nextText = value ?? "";
            var wasLargePrompt = _isLargePrompt;
            var isLargePrompt = IsLargePromptText(nextText);
            if (SetProperty(ref _promptText, nextText))
            {
                _isLargePrompt = isLargePrompt;
                OnPropertyChanged(nameof(PromptTokenomicsLabel));
                OnPropertyChanged(nameof(PromptContextPressureLabel));
                OnPropertyChanged(nameof(PromptFooterSummary));
                if (wasLargePrompt != isLargePrompt)
                {
                    OnPropertyChanged(nameof(IsLargePrompt));
                    OnPropertyChanged(nameof(PromptTextWrapping));
                    OnPropertyChanged(nameof(PromptHorizontalScrollBarVisibility));
                    OnPropertyChanged(nameof(PromptFooterSummary));
                    if (isLargePrompt)
                    {
                        IsPromptTypingActive = false;
                    }

                    OnPropertyChanged(nameof(PromptBarHeight));
                    return;
                }

                if (!isLargePrompt && IsPromptTypingActive)
                {
                    OnPropertyChanged(nameof(PromptBarHeight));
                }
            }
        }
    }

    public bool IsLargePrompt => _isLargePrompt;

    public TextWrapping PromptTextWrapping => IsLargePrompt ? TextWrapping.NoWrap : TextWrapping.Wrap;

    public ScrollBarVisibility PromptHorizontalScrollBarVisibility =>
        IsLargePrompt ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

    public string PromptModeKey
    {
        get => _promptModeKey;
        private set
        {
            var clean = value?.Trim().ToLowerInvariant() switch
            {
                "terminal" => "terminal",
                _ => "context"
            };

            if (SetProperty(ref _promptModeKey, clean))
            {
                OnPropertyChanged(nameof(IsContextPromptMode));
                OnPropertyChanged(nameof(IsChatPromptMode));
                OnPropertyChanged(nameof(IsTerminalPromptMode));
                OnPropertyChanged(nameof(IsMessagePromptMode));
                OnPropertyChanged(nameof(PromptWatermark));
                OnPropertyChanged(nameof(PromptSendButtonLabel));
                _settings.PromptModeKey = clean;

                SaveSettingsQuietly();
            }
        }
    }

    public bool IsContextPromptMode => string.Equals(PromptModeKey, "context", StringComparison.OrdinalIgnoreCase);

    public bool IsChatPromptMode => IsContextPromptMode;

    public bool IsTerminalPromptMode => string.Equals(PromptModeKey, "terminal", StringComparison.OrdinalIgnoreCase);

    public bool IsMessagePromptMode => !IsTerminalPromptMode;

    public string PromptWatermark => IsChatPromptMode
        ? "Ask the selected local model, or paste CC request/patch text..."
        : IsTerminalPromptMode ? "Terminal output"
        : "Message Context Control...";

    public string PromptSendButtonLabel => "Send";

    public string TerminalOutputText
    {
        get => _terminalOutputText;
        private set => SetProperty(ref _terminalOutputText, value ?? "");
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
                ProviderStatus = clean.StartsWith("Local:", StringComparison.OrdinalIgnoreCase)
                    ? "Local Ollama route selected"
                    : clean.StartsWith("API:", StringComparison.OrdinalIgnoreCase)
                    ? "API profile selected"
                    : "Browser profile selected";
                SaveSettingsQuietly();
            }
        }
    }

    public LocalLlmModelViewModel? SelectedLocalModel
    {
        get => _selectedLocalModel;
        set
        {
            if (SetProperty(ref _selectedLocalModel, value))
            {
                if (value is not null)
                {
                    _settings.SelectedLocalModel = value.Id;
                    SaveSettingsQuietly();
                }

                OnPropertyChanged(nameof(SelectedLocalModelLabel));
                OnPropertyChanged(nameof(PromptContextPressureLabel));
                OnPropertyChanged(nameof(PromptFooterSummary));
            }
        }
    }

    public string SelectedLocalModelLabel => SelectedLocalModel?.DisplayName ?? "No installed local model";

    public string FileRequestModelId
    {
        get => _fileRequestModelId;
        set
        {
            var clean = CleanModelId(value, "qwen2.5-coder:1.5b");
            if (SetProperty(ref _fileRequestModelId, clean))
            {
                _settings.FileRequestModel = clean;
                SaveSettingsQuietly();
            }
        }
    }

    public string PatchWriteModelId
    {
        get => _patchWriteModelId;
        set
        {
            var clean = CleanModelId(value, "qwen2.5-coder:3b");
            if (SetProperty(ref _patchWriteModelId, clean))
            {
                _settings.PatchWriteModel = clean;
                SaveSettingsQuietly();
            }
        }
    }

    public string PatchReviewModelId
    {
        get => _patchReviewModelId;
        set
        {
            var clean = CleanModelId(value, "phi4-mini");
            if (SetProperty(ref _patchReviewModelId, clean))
            {
                _settings.PatchReviewModel = clean;
                SaveSettingsQuietly();
            }
        }
    }

    public string ChatModelId
    {
        get => _chatModelId;
        set
        {
            var clean = CleanModelId(value, "qwen2.5-coder:3b");
            if (SetProperty(ref _chatModelId, clean))
            {
                _settings.ChatModel = clean;
                SaveSettingsQuietly();
            }
        }
    }

    public string SkillbookSummary => $"{SkillbookEntries.Count} instruction(s); project overrides global entries";

    public string SkillbookProjectPath => _skillbookService.ProjectRoot;

    public string SkillbookGlobalPath => _skillbookService.GlobalRoot;

    public string PromptTokenomicsLabel
    {
        get
        {
            var promptTokens = ContextCapsuleBuilder.EstimateTokens(PromptText);
            var attachments = EstimateAttachmentBudget();
            return attachments.IsClipped
                ? $"{promptTokens:N0} prompt tok; {attachments.SentTokens:N0} sent attachment tok ({attachments.FullTokens:N0} full)"
                : $"{promptTokens:N0} prompt tok; {attachments.SentTokens:N0} attachment tok";
        }
    }

    public string PromptContextPressureLabel
    {
        get
        {
            var model = SelectedLocalModel;
            var budget = EstimateComfortableBudget(model?.ComfortableContext);
            var attachmentBudget = EstimateAttachmentBudget();
            var total = ContextCapsuleBuilder.EstimateTokens(PromptText)
                + attachmentBudget.SentTokens
                + ContextCapsuleBuilder.DefaultOutputReserveTokens;
            var pressure = budget <= 0 ? 0 : total * 100d / budget;
            var modelLabel = model?.DisplayName ?? SelectedLocalModelLabel;
            var suffix = attachmentBudget.IsClipped ? "; clipped" : "";
            return $"{modelLabel}: {total:N0}/{budget:N0} tok est ({pressure:0.#}%{suffix})";
        }
    }

    public string ProviderStatus
    {
        get => _providerStatus;
        private set => SetProperty(ref _providerStatus, value);
    }

    public string HardwareSummary
    {
        get => _hardwareSummary;
        private set => SetProperty(ref _hardwareSummary, value);
    }

    public string LocalLlmStatus
    {
        get => _localLlmStatus;
        private set => SetProperty(ref _localLlmStatus, value);
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

    public string PromptFooterSummary => IsLargePrompt
        ? $"{AttachmentSummary} - large prompt mode - {PromptTokenomicsLabel}"
        : $"{AttachmentSummary} - {PromptTokenomicsLabel}";

    public string ContextRootPath => _processService.ContextRoot;

    public string ContextRootLabel => string.IsNullOrWhiteSpace(_processService.ContextRoot)
        ? "context root not resolved"
        : _processService.ContextRoot;

    public string ActiveProjectRoot
    {
        get => _activeProjectRoot;
        set
        {
            if (SetProperty(ref _activeProjectRoot, value ?? ""))
            {
                OnPropertyChanged(nameof(ActiveProjectRootLabel));
                OnPropertyChanged(nameof(EffectiveProjectRootLabel));
            }
        }
    }

    public string ActiveProjectRootLabel => string.IsNullOrWhiteSpace(ActiveProjectRoot)
        ? "active project not resolved"
        : ActiveProjectRoot;

    public string EffectiveProjectRootLabel => string.IsNullOrWhiteSpace(ActiveProjectRoot)
        ? ContextRootLabel
        : ActiveProjectRoot;

    public string ActiveProjectRulesPath
    {
        get => _activeProjectRulesPath;
        set
        {
            if (SetProperty(ref _activeProjectRulesPath, value ?? ""))
            {
                OnPropertyChanged(nameof(ActiveProjectRulesPathLabel));
            }
        }
    }

    public string ActiveProjectRulesPathLabel => string.IsNullOrWhiteSpace(ActiveProjectRulesPath)
        ? "project rules not resolved"
        : ActiveProjectRulesPath;

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
        IsPromptTypingActive = IsPromptOpen && isActive && !IsLargePrompt;
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
            var result = await _processService.RunCodeExportAsync(lines, ActiveProjectRoot, ActiveProjectRulesPath);
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

    private void ToggleAttachmentInclude(ContextControlAttachmentViewModel? attachment)
    {
        if (attachment is null)
        {
            return;
        }

        attachment.IncludeInPrompt = !attachment.IncludeInPrompt;
        NotifyAttachmentStateChanged();
    }

    private async Task CopySnippetAsync(ChatSnippetViewModel? snippet)
    {
        if (snippet is null || _clipboardWriter is null)
        {
            return;
        }

        await _clipboardWriter(snippet.Text);
        PhaseTitle = "Snippet copied";
        PhaseDetail = snippet.Title;
        Log("info", $"Copied snippet: {snippet.Title}");
    }

    private async Task CopyChatTextAsync(LocalLlmChatPartViewModel? part)
    {
        if (part is null || !part.IsText || string.IsNullOrWhiteSpace(part.Text) || _clipboardWriter is null)
        {
            return;
        }

        await _clipboardWriter(part.Text);
        PhaseTitle = "Chat text copied";
        PhaseDetail = "Copied message body text only.";
        Log("info", "Copied chat message text.");
    }

    private void UseSnippetForCc(ChatSnippetViewModel? snippet)
    {
        if (snippet is null)
        {
            return;
        }

        PromptText = EnsureEndsWithEnd(snippet.Text);
        IsPromptOpen = true;
        PromptModeKey = "context";
        PhaseTitle = snippet.IsRequestList ? "CC request loaded" : "Snippet loaded";
        PhaseDetail = snippet.IsRequestList
            ? "Review the request lines, then press CC."
            : "Snippet copied into the prompt.";
    }

    private async Task PreviewSnippetAsync(ChatSnippetViewModel? snippet)
    {
        if (snippet is null || !snippet.IsPatch)
        {
            return;
        }

        _lastAssistantPatchBlocks = snippet.Text;
        PromptText = snippet.Text;
        await RunGoPreviewAsync();
    }

    private void LoadChatHistory()
    {
        var document = _chatHistoryService.Load();
        foreach (var session in document.Sessions
                     .OrderByDescending(session => session.UpdatedUtc)
                     .Select(session => new ChatSessionViewModel(session)))
        {
            ChatSessions.Add(session);
        }

        if (ChatSessions.Count == 0)
        {
            CreateNewChatSession(save: false, resetWorkflow: false);
            return;
        }

        var selected = !string.IsNullOrWhiteSpace(document.SelectedSessionId)
            ? ChatSessions.FirstOrDefault(session => string.Equals(session.Id, document.SelectedSessionId, StringComparison.OrdinalIgnoreCase))
            : null;
        SelectChatSession(selected ?? ChatSessions[0], save: false);
    }

    private void CreateNewChatSession()
    {
        CreateNewChatSession(save: true, resetWorkflow: true);
    }

    private void CreateNewChatSession(bool save)
    {
        CreateNewChatSession(save, resetWorkflow: true);
    }

    private void CreateNewChatSession(bool save, bool resetWorkflow)
    {
        var session = ChatSessionViewModel.CreateNew();
        ChatSessions.Insert(0, session);
        OnPropertyChanged(nameof(HasChatSessions));
        OnPropertyChanged(nameof(ChatHistorySummary));
        SelectChatSession(session, save);

        if (resetWorkflow)
        {
            ResetChatWorkflowState();
        }
    }

    private void SelectChatSession(ChatSessionViewModel? session)
    {
        SelectChatSession(session, save: true);
    }

    private void SelectChatSession(ChatSessionViewModel? session, bool save)
    {
        if (session is null)
        {
            return;
        }

        foreach (var item in ChatSessions)
        {
            item.IsActive = ReferenceEquals(item, session);
        }

        SelectedChatSession = session;
        ChatMessages.Clear();
        foreach (var message in session.CreateMessages())
        {
            ChatMessages.Add(message);
        }

        _lastAssistantPatchBlocks = ChatMessages
            .SelectMany(message => message.Snippets)
            .LastOrDefault(snippet => snippet.IsPatch)
            ?.Text ?? "";
        PhaseTitle = "Chat selected";
        PhaseDetail = session.Title;

        if (save)
        {
            SaveChatHistory();
        }
    }

    private void RemoveChatSession(ChatSessionViewModel? session)
    {
        if (session is null || !ChatSessions.Contains(session))
        {
            return;
        }

        var wasSelected = ReferenceEquals(SelectedChatSession, session);
        var removedIndex = ChatSessions.IndexOf(session);
        ChatSessions.Remove(session);
        OnPropertyChanged(nameof(HasChatSessions));
        OnPropertyChanged(nameof(ChatHistorySummary));

        if (ChatSessions.Count == 0)
        {
            CreateNewChatSession(save: false, resetWorkflow: true);
            SaveChatHistory();
            PhaseTitle = "Chat removed";
            PhaseDetail = "Started a fresh chat.";
            return;
        }

        if (wasSelected)
        {
            var nextIndex = Math.Clamp(removedIndex, 0, ChatSessions.Count - 1);
            SelectChatSession(ChatSessions[nextIndex], save: false);
        }

        SaveChatHistory();
        PhaseTitle = "Chat removed";
        PhaseDetail = session.Title;
    }

    private void ResetChatWorkflowState()
    {
        Attachments.Clear();
        _lastAssistantPatchBlocks = "";
        _lastUserRequest = "";
        PromptText = "";
        LastExportPath = "";
        IsPatchPlanReady = false;
        PatchSummary = "No patch loaded.";
        UpdatePatchPlanActions(null);
        NotifyAttachmentStateChanged();
        PhaseTitle = "New chat";
        PhaseDetail = "Fresh local chat with no DIR, CC, patch, or previous request context.";
    }

    private void AppendChatMessage(LocalLlmChatMessageViewModel message)
    {
        if (SelectedChatSession is null)
        {
            CreateNewChatSession(save: false, resetWorkflow: false);
        }

        ChatMessages.Add(message);
        SelectedChatSession?.Append(message);
        if (SelectedChatSession is { } active)
        {
            var index = ChatSessions.IndexOf(active);
            if (index > 0)
            {
                ChatSessions.Move(index, 0);
            }
        }

        OnPropertyChanged(nameof(ChatHistorySummary));
        SaveChatHistory();
    }

    private void UpdatePatchPlanActions(PatchPlanSummary? summary)
    {
        PatchPlanActions.Clear();
        if (summary is not null)
        {
            foreach (var action in summary.Actions.Take(24))
            {
                PatchPlanActions.Add(new PatchPlanActionViewModel(action));
            }
        }

        OnPropertyChanged(nameof(HasPatchPlanActions));
    }

    private static string BuildPatchPlanChatText(PatchPlanSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("GO preview ready.");
        builder.AppendLine(summary.CompactLabel);
        if (summary.Actions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Files:");
            foreach (var action in summary.Actions.Take(18))
            {
                builder.AppendLine($"{action.AddedLabel} {action.RemovedLabel} {action.FileLabel} :: {action.PartLabel} [{action.StatusLabel}]");
            }

            if (summary.Actions.Count > 18)
            {
                builder.AppendLine($"... {summary.Actions.Count - 18:N0} more action(s)");
            }
        }

        builder.AppendLine();
        builder.AppendLine("GO preview did not write source files. Apply effective writes non-duplicate edits through ccReplace.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildPatchFailureChatText(string title, ContextControlCommandResult result, PatchPlanSummary summary)
    {
        var detail = !string.IsNullOrWhiteSpace(summary.Error)
            ? summary.Error
            : FirstErrorLine(result);
        return $"{title}.{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}GO needs raw BEGIN/END CC-REPLACE blocks. Send is for talking to the model; GO is only for patch preview.";
    }

    private static string BuildPatchApplyChatText(ContextControlCommandResult result, string decision)
    {
        if (result.Succeeded)
        {
            return $"GO apply complete.{Environment.NewLine}ccReplace applied {decision} edits. Version snapshots are kept by the existing ccReplace cache when enabled.";
        }

        return $"GO apply failed.{Environment.NewLine}{FirstErrorLine(result)}";
    }

    private void SaveChatHistory()
    {
        try
        {
            _chatHistoryService.Save(new ChatHistoryDocument
            {
                SelectedSessionId = SelectedChatSession?.Id,
                Sessions = ChatSessions.Select(session => session.ToData()).ToList()
            });
        }
        catch (Exception ex)
        {
            Log("warn", $"Chat history save skipped: {ex.Message}");
        }
    }

    private void SelectDockPanel(string panelKey)
    {
        var clean = string.Equals(panelKey, "chat", StringComparison.OrdinalIgnoreCase)
            ? "chat"
            : "log";
        if (string.Equals(_dockPanelKey, clean, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _dockPanelKey = clean;
        OnPropertyChanged(nameof(IsLogPanelOpen));
        OnPropertyChanged(nameof(IsChatPanelOpen));
    }

    private async Task RefreshLocalModelsAsync()
    {
        if (IsRefreshingLocalModels)
        {
            return;
        }

        IsRefreshingLocalModels = true;
        LocalLlmStatus = "Detecting GPU and local Ollama models...";
        try
        {
            var result = await _localLlmService.RefreshAsync();
            HardwareSummary = result.Hardware.Summary;
            LocalLlmStatus = result.Status;
            IsOllamaInstalled = result.OllamaInstalled;
            ApplyLocalModelRefresh(result);
            Log(result.OllamaReachable ? "info" : "warn", result.Status);
        }
        catch (Exception ex)
        {
            LocalLlmStatus = ex.Message;
            Log("warn", $"Local model scan failed: {ex.Message}");
        }
        finally
        {
            IsRefreshingLocalModels = false;
        }
    }

    private IProgress<LocalLlmTransferProgress> CreateTransferProgress(string initialTitle)
    {
        BeginTransferProgress(initialTitle);
        return new Progress<LocalLlmTransferProgress>(UpdateTransferProgress);
    }

    private IProgress<string> CreateTerminalProgress()
    {
        return new Progress<string>(AppendTerminalOutput);
    }

    private IProgress<LocalLlmGenerationProgress> CreateGenerationProgress(string modelName)
    {
        BeginGenerationProgress(modelName);
        return new Progress<LocalLlmGenerationProgress>(UpdateGenerationProgress);
    }

    private void BeginGenerationProgress(string modelName)
    {
        IsPromptOpen = true;
        _generationStartedAt = DateTime.UtcNow;
        TransferProgressTitle = $"Chatting with {modelName}";
        TransferProgressStatus = "Loading model...";
        TransferProgressSizeLabel = "0 output tok";
        TransferProgressSpeedLabel = "speed pending";
        TransferProgressPercentLabel = "";
        TransferProgressValue = 0;
        IsTransferProgressIndeterminate = true;
        IsTransferProgressActive = true;
    }

    private void UpdateGenerationProgress(LocalLlmGenerationProgress progress)
    {
        var elapsed = _generationStartedAt is { } startedAt
            ? Math.Max(0, (DateTime.UtcNow - startedAt).TotalSeconds)
            : 0;
        TransferProgressStatus = progress.Status;
        TransferProgressSizeLabel = progress.EvalCount is { } evalCount
            ? $"{evalCount} output tok"
            : "loading";
        TransferProgressSpeedLabel = BuildGenerationSpeedLabel(progress, elapsed);
        TransferProgressPercentLabel = elapsed > 0 ? $"{elapsed:0.#}s" : "";
        IsTransferProgressIndeterminate = !progress.Done;
        if (progress.Done)
        {
            TransferProgressValue = 100;
        }
    }

    private static string BuildGenerationSpeedLabel(LocalLlmGenerationProgress progress, double elapsedSeconds)
    {
        var evalSeconds = progress.EvalDurationNanoseconds is > 0
            ? progress.EvalDurationNanoseconds.Value / 1_000_000_000d
            : elapsedSeconds;
        if (progress.EvalCount is > 0 && evalSeconds > 0)
        {
            return $"{progress.EvalCount.Value / evalSeconds:0.#} tok/s";
        }

        return "speed pending";
    }

    private void AppendTerminalOutput(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var normalized = line
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (normalized.Length == 0)
        {
            return;
        }

        var builder = new System.Text.StringBuilder(TerminalOutputText);
        foreach (var item in normalized)
        {
            builder.Append('[');
            builder.Append(DateTime.Now.ToString("HH:mm:ss"));
            builder.Append("] ");
            builder.AppendLine(item);
        }

        const int maxTerminalCharacters = 20000;
        var text = builder.ToString();
        if (text.Length > maxTerminalCharacters)
        {
            text = text[^maxTerminalCharacters..];
        }

        TerminalOutputText = text;
    }

    private void BeginTransferProgress(string title)
    {
        IsPromptOpen = true;
        PromptModeKey = "terminal";
        TransferProgressTitle = title;
        TransferProgressStatus = "Starting...";
        TransferProgressSizeLabel = "0 B / ?";
        TransferProgressSpeedLabel = "0 B/s";
        TransferProgressPercentLabel = "";
        TransferProgressValue = 0;
        IsTransferProgressIndeterminate = true;
        IsTransferProgressActive = true;
    }

    private void UpdateTransferProgress(LocalLlmTransferProgress progress)
    {
        TransferProgressTitle = progress.Operation;
        TransferProgressStatus = string.IsNullOrWhiteSpace(progress.Status) ? progress.Operation : progress.Status;
        if (!_ollamaInstallerPromptShown
            && progress.Operation.Equals("Installing Ollama", StringComparison.OrdinalIgnoreCase)
            && TransferProgressStatus.Contains("Running OllamaSetup.exe", StringComparison.OrdinalIgnoreCase))
        {
            _ollamaInstallerPromptShown = true;
            IsOllamaInstallerPromptOpen = true;
        }

        IsTransferProgressIndeterminate = progress.Percent is null;
        if (progress.Percent is { } percent)
        {
            TransferProgressValue = percent;
            TransferProgressPercentLabel = $"{percent:0.#}%";
        }
        else
        {
            TransferProgressPercentLabel = "";
        }

        TransferProgressSizeLabel = FormatTransferSize(progress.CurrentBytes, progress.TotalBytes);
        TransferProgressSpeedLabel = progress.BytesPerSecond is { } speed && speed > 0
            ? $"{FormatBytes((long)speed)}/s"
            : "speed pending";
    }

    private void CompleteTransferProgress(string status, bool succeeded)
    {
        TransferProgressStatus = status;
        TransferProgressSpeedLabel = succeeded ? "complete" : "stopped";
        if (succeeded)
        {
            TransferProgressValue = 100;
            TransferProgressPercentLabel = "100%";
            IsTransferProgressIndeterminate = false;
        }

        IsTransferProgressActive = false;
    }

    private static string FormatTransferSize(long? currentBytes, long? totalBytes)
    {
        var current = currentBytes is { } currentValue ? FormatBytes(currentValue) : "0 B";
        var total = totalBytes is { } totalValue && totalValue > 0 ? FormatBytes(totalValue) : "?";
        return $"{current} / {total}";
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

    private async Task InstallOllamaAsync()
    {
        if (IsInstallingOllama)
        {
            return;
        }

        IsInstallingOllama = true;
        _ollamaInstallerPromptShown = false;
        IsOllamaInstallerPromptOpen = false;
        LocalLlmStatus = $"Installing Ollama with: {LocalLlmService.OllamaWindowsInstallCommand}";
        PhaseTitle = "Installing Ollama";
        PhaseDetail = $"{LocalLlmService.OllamaInstallerSize}; models are downloaded separately.";
        Log("run", "Install Ollama");

        try
        {
            var progress = CreateTransferProgress("Installing Ollama");
            var terminal = CreateTerminalProgress();
            var result = await _localLlmService.InstallOllamaAsync(progress, terminal);
            LocalLlmStatus = result.Status;
            PhaseTitle = result.Succeeded ? "Ollama installed" : "Ollama install failed";
            PhaseDetail = result.Status;
            if (result.Succeeded)
            {
                IsOllamaInstalled = true;
                AppendTerminalOutput("Ollama installation detected. Install button disabled.");
            }

            CompleteTransferProgress(result.Status, result.Succeeded);
            Log(result.Succeeded ? "ok" : "warn", result.Status);

            if (result.Succeeded)
            {
                await RefreshLocalModelsAsync();
            }
        }
        catch (Exception ex)
        {
            LocalLlmStatus = ex.Message;
            PhaseTitle = "Ollama install failed";
            PhaseDetail = ex.Message;
            CompleteTransferProgress(ex.Message, succeeded: false);
            Log("error", ex.Message);
        }
        finally
        {
            IsInstallingOllama = false;
        }
    }

    private void OpenOllamaDownloadPage()
    {
        var result = _localLlmService.OpenOllamaDownloadPage();
        LocalLlmStatus = result.Status;
        Log(result.Succeeded ? "info" : "warn", result.Status);
    }

    private async Task PullLocalModelAsync(LocalLlmModelViewModel? model)
    {
        if (model is null || IsBusy)
        {
            return;
        }

        model.IsPulling = true;
        RaiseCommandStates();
        try
        {
            await RunBusyAsync($"Pull {model.Id}", async () =>
            {
                LocalLlmStatus = $"Downloading {model.Id}...";
                PhaseTitle = "Downloading model";
                PhaseDetail = model.PullCommand;
                var progress = CreateTransferProgress($"Downloading {model.Id}");
                var terminal = CreateTerminalProgress();
                var result = await _localLlmService.PullModelAsync(model.Id, progress, terminal);
                LocalLlmStatus = result.Status;
                PhaseTitle = result.Succeeded ? "Model ready" : "Pull failed";
                PhaseDetail = result.Status;
                CompleteTransferProgress(result.Status, result.Succeeded);
                Log(result.Succeeded ? "ok" : "warn", result.Status);

                if (result.Succeeded)
                {
                    await RefreshLocalModelsAsync();
                }
            });
        }
        finally
        {
            model.IsPulling = false;
            RaiseCommandStates();
        }
    }

    private void ApplyLocalModelRefresh(LocalLlmRefreshResult result)
    {
        foreach (var unknownModelId in result.UnknownInstalledModelIds)
        {
            if (LocalLlmModels.Any(model => string.Equals(model.Id, unknownModelId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            LocalLlmModels.Add(new LocalLlmModelViewModel(CreateUnknownInstalledModel(unknownModelId)));
            if (!LocalModelIdOptions.Contains(unknownModelId, StringComparer.OrdinalIgnoreCase))
            {
                LocalModelIdOptions.Add(unknownModelId);
            }
        }

        foreach (var model in LocalLlmModels)
        {
            model.ApplyState(result.InstalledModelIds.Contains(model.Id), result.Hardware);
        }

        RefreshInstalledLocalModels();
    }

    private void RefreshInstalledLocalModels()
    {
        InstalledLocalModels.Clear();
        foreach (var model in LocalLlmModels
            .Where(model => model.IsInstalled)
            .OrderByDescending(model => model.IsRecommended)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            InstalledLocalModels.Add(model);
        }

        OnPropertyChanged(nameof(HasInstalledLocalModels));

        var preferred = InstalledLocalModels.FirstOrDefault(model =>
                string.Equals(model.Id, _settings.SelectedLocalModel, StringComparison.OrdinalIgnoreCase))
            ?? InstalledLocalModels.FirstOrDefault(model => model.IsRecommended)
            ?? InstalledLocalModels.FirstOrDefault();

        if (preferred is null)
        {
            SelectedLocalModel = null;
            return;
        }

        if (SelectedLocalModel is null
            || !InstalledLocalModels.Contains(SelectedLocalModel))
        {
            SelectedLocalModel = preferred;
        }
    }

    private static LocalLlmCatalogModel CreateUnknownInstalledModel(string modelId)
    {
        return new LocalLlmCatalogModel(
            modelId,
            modelId,
            "Installed",
            "See model card",
            "Detected from local Ollama; requirements unknown",
            "Unknown",
            "Start with 4K",
            "Keep snippets small until tested.",
            "Unknown",
            "Existing local Ollama model.",
            0,
            4,
            true,
            $"ollama pull {modelId}");
    }

    private double CalculatePromptBarBaseHeight()
    {
        if (IsLargePrompt)
        {
            return MaximumPromptBarHeight;
        }

        return IsPromptTypingActive ? CalculatePromptBarHeight() : CompactPromptBarHeight;
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

        var count = 0;
        var lineLength = 0;
        var processedLines = 0;
        var index = 0;

        for (; index < text.Length
            && index < PromptHeightEstimationCharacterLimit
            && processedLines < PromptHeightEstimationLineLimit;
            index++)
        {
            var character = text[index];
            if (character is '\r' or '\n')
            {
                count += EstimateWrappedLines(lineLength);
                processedLines++;
                lineLength = 0;

                if (character == '\r'
                    && index + 1 < text.Length
                    && text[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            lineLength++;
        }

        if (index < text.Length)
        {
            return Math.Max(count, PromptVisualLinesForMaximumHeight);
        }

        count += EstimateWrappedLines(lineLength);
        return count;
    }

    private static int EstimateWrappedLines(int lineLength)
    {
        return Math.Max(1, (int)Math.Ceiling(lineLength / (double)EstimatedPromptWrapColumn));
    }

    private static bool IsLargePromptText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.Length >= LargePromptCharacterThreshold)
        {
            return true;
        }

        var lineBreaks = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is not ('\r' or '\n'))
            {
                continue;
            }

            if (++lineBreaks >= LargePromptLineThreshold)
            {
                return true;
            }

            if (character == '\r'
                && index + 1 < text.Length
                && text[index + 1] == '\n')
            {
                index++;
            }
        }

        return false;
    }

    private async Task RunDirAsync()
    {
        await RunBusyAsync("DIR export", async () =>
        {
            PhaseTitle = "DIR export";
            PhaseDetail = "Exporting the project tree and navigation prompt.";
            var result = await _processService.RunDirectoryExportAsync(ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);

            if (!result.Succeeded)
            {
                PhaseTitle = "DIR failed";
                PhaseDetail = FirstErrorLine(result);
                return;
            }

            RemoveAttachmentsByKind("code", "patch");
            _lastAssistantPatchBlocks = "";
            IsPatchPlanReady = false;
            PatchSummary = "No patch loaded.";
            UpdatePatchPlanActions(null);
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
                PhaseDetail = "Paste clean file/function/FIND lines, or a quoted model list, into the prompt bar first.";
                Log("warn", "CC cancelled: no request lines.");
                AppendTerminalOutput("CC cancelled: no usable file/function/FIND request lines found.");
                return;
            }

            var missingPaths = FindMissingRequestPaths(requestLines);
            if (missingPaths.Count > 0)
            {
                PhaseTitle = "CC path not found";
                PhaseDetail = missingPaths.Count == 1
                    ? $"Not in project tree: {missingPaths[0]}"
                    : $"{missingPaths.Count} request paths are not in the active project tree.";
                Log("warn", $"CC cancelled: missing request path {missingPaths[0]}");
                AppendTerminalOutput("CC cancelled: these request paths are not in the active project tree:");
                foreach (var missing in missingPaths.Take(8))
                {
                    AppendTerminalOutput($"  {missing}");
                }

                AppendTerminalOutput("Use real paths from DIR, or use FIND: text for discovery.");
                return;
            }

            PromptText = EnsureEndsWithEnd(string.Join(Environment.NewLine, requestLines));
            PhaseTitle = "CC export";
            PhaseDetail = $"Exporting {requestLines.Count} selected source/function request line(s).";
            AppendTerminalOutput($"CC export started with {requestLines.Count} request line(s).");
            var result = await _processService.RunCodeExportAsync(requestLines, ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);

            if (!result.Succeeded)
            {
                PhaseTitle = "CC failed";
                PhaseDetail = FirstErrorLine(result);
                AppendTerminalOutput($"CC export failed: {PhaseDetail}");
                return;
            }

            RemoveAttachmentsByKind("patch");
            IsPatchPlanReady = false;
            UpdatePatchPlanActions(null);
            AddAttachment(Path.GetFileName(_processService.CodeExportPath), _processService.CodeExportPath, "code");
            LastExportPath = _processService.CodeExportPath;
            PromptText = string.IsNullOrWhiteSpace(_lastUserRequest)
                ? StripLegacyDirPayload(PromptText)
                : _lastUserRequest;
            PhaseTitle = "Context ready";
            PhaseDetail = "Send the CC export to the AI. The next complete answer should include CC-REPLACE patch blocks.";
            AppendTerminalOutput($"CC export complete: {requestLines.Count} request line(s) exported.");
        });
    }

    private async Task RunGoPreviewAsync()
    {
        await RunBusyAsync("GO preview", async () =>
        {
            var promptPatchBlocks = _promptBuilder.ExtractPatchBlocks(PromptText);
            var patchText = !string.IsNullOrWhiteSpace(promptPatchBlocks)
                ? promptPatchBlocks
                : _lastAssistantPatchBlocks;
            if (string.IsNullOrWhiteSpace(patchText))
            {
                PhaseTitle = "GO needs patch";
                PhaseDetail = "Paste BEGIN/END CC-REPLACE blocks, or press GO on a patch snippet. Send talks to the model; GO only previews patches.";
                Log("warn", "GO preview cancelled: no CC-REPLACE blocks found.");
                AppendTerminalOutput("GO cancelled: no CC-REPLACE blocks found in the prompt or latest assistant patch.");
                UpdatePatchPlanActions(null);
                return;
            }

            PhaseTitle = "GO preview";
            PhaseDetail = "Writing patch.txt and asking ccReplace for a non-writing plan.";
            AppendTerminalOutput("GO preview started: writing patch.txt and running ccReplace -PlanOnly -Json.");
            await _processService.WritePatchAsync(patchText);
            AddAttachment(Path.GetFileName(_processService.PatchPath), _processService.PatchPath, "patch");

            var result = await _processService.PreviewPatchAsync(ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);
            var summary = _promptBuilder.ParsePatchPlanSummary(result.StandardOutput);
            var planReady = result.Succeeded && string.IsNullOrWhiteSpace(summary.Error);
            IsPatchPlanReady = planReady;
            PatchSummary = result.Succeeded ? summary.CompactLabel : FirstErrorLine(result);
            UpdatePatchPlanActions(planReady ? summary : null);
            PhaseTitle = planReady ? "Patch planned" : "Patch preview failed";
            PhaseDetail = planReady
                ? "Review the plan, then apply effective edits from the dock."
                : (string.IsNullOrWhiteSpace(summary.Error) ? FirstErrorLine(result) : summary.Error);
            AppendTerminalOutput(planReady ? PatchSummary : $"GO preview failed: {PhaseDetail}");
            AppendChatMessage(new LocalLlmChatMessageViewModel(
                "assistant",
                planReady ? BuildPatchPlanChatText(summary) : BuildPatchFailureChatText("GO preview failed", result, summary),
                "ccReplace",
                "GO preview"));
        });
    }

    private async Task ApplyPatchAsync()
    {
        await ApplyPatchAsync("effective");
    }

    private async Task ApplyPatchAsync(string decision)
    {
        await RunBusyAsync("GO apply", async () =>
        {
            PhaseTitle = "Applying patch";
            PhaseDetail = string.Equals(decision, "all", StringComparison.OrdinalIgnoreCase)
                ? "Applying all ccReplace actions."
                : "Applying effective edits through ccReplace.";
            var result = await _processService.ApplyPatchAsync(decision, ActiveProjectRoot, ActiveProjectRulesPath);
            LogResult(result);
            IsPatchPlanReady = false;
            PhaseTitle = result.Succeeded ? "Patch applied" : "Patch failed";
            PhaseDetail = result.Succeeded ? "ccReplace applied the selected edits." : FirstErrorLine(result);
            AppendTerminalOutput(result.Succeeded ? $"GO apply complete: {decision} edits applied." : $"GO apply failed: {PhaseDetail}");
            AppendChatMessage(new LocalLlmChatMessageViewModel(
                "assistant",
                BuildPatchApplyChatText(result, decision),
                "ccReplace",
                "GO apply"));
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

            if (IsChatPromptMode || SelectedRoute.StartsWith("Local:", StringComparison.OrdinalIgnoreCase))
            {
                await SendLocalChatAsync(message);
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

    private async Task SendLocalChatAsync(string message)
    {
        var phase = ResolveCapsulePhase(message);
        var capsuleMessage = phase is ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview
            ? ResolvePatchTaskMessage(message)
            : message;
        if (phase == ContextCapsulePhase.FileRequest && !IsLikelyCcRequestList(message))
        {
            _lastUserRequest = message;
        }

        var model = ResolveModelForPhase(phase);
        if (model is not { IsInstalled: true })
        {
            PhaseTitle = "No local model";
            PhaseDetail = "Pull a model from the LLMs tab, then select it for chat.";
            Log("warn", "Local chat cancelled: no installed model selected.");
            return;
        }

        var capsuleAttachments = await BuildCapsuleAttachmentsAsync(phase);
        var capsule = _capsuleBuilder.Build(new ContextCapsuleBuildRequest(
            capsuleMessage,
            phase,
            model.Id,
            model.ComfortableContext,
            _skillbookService.BuildEnabledInstructionText(),
            capsuleAttachments));

        var attachmentSnapshot = Attachments.ToArray();
        AppendChatMessage(new LocalLlmChatMessageViewModel(
            "user",
            capsuleMessage,
            model.Id,
            FormatCapsulePhase(phase),
            capsule.Summary,
            attachments: attachmentSnapshot));
        PromptText = "";
        PhaseTitle = "Local CC chat";
        PhaseDetail = $"{FormatCapsulePhase(phase)} with {model.DisplayName}; {capsule.Summary}.";
        ProviderStatus = $"Local Ollama: {model.Id}";
        var generationProgress = CreateGenerationProgress(model.DisplayName);
        var terminal = CreateTerminalProgress();
        terminal.Report($"Sending {FormatCapsulePhase(phase)} capsule to {model.DisplayName} ({model.Id})...");

        var result = await _localLlmService.SendChatAsync(
            new LocalLlmRequest(
                model.Id,
                capsule.Text,
                FormatCapsulePhase(phase),
                attachmentSnapshot.Select(attachment => attachment.DisplayTitle).ToArray()),
            generationProgress,
            terminal);

        if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
        {
            var assistant = new LocalLlmChatMessageViewModel(
                "assistant",
                result.Message,
                model.Id,
                FormatCapsulePhase(phase),
                capsule.Summary,
                result.Stats,
                attachmentSnapshot);
            AppendChatMessage(assistant);
            var latestPatch = assistant.Snippets.LastOrDefault(snippet => snippet.IsPatch);
            if (latestPatch is not null)
            {
                _lastAssistantPatchBlocks = latestPatch.Text;
            }

            if (assistant.HasThinking)
            {
                model.MarkThinkingDetected();
            }
        }

        ProviderStatus = result.Status;
        PhaseTitle = result.Succeeded ? "Local answer ready" : "Local chat failed";
        PhaseDetail = result.Status;
        CompleteTransferProgress(result.Status, result.Succeeded);
        Log(result.Succeeded ? "ok" : "warn", result.Status);
    }

    private ContextCapsulePhase ResolveCapsulePhase(string message)
    {
        var hasPatch = Attachments.Any(attachment => attachment.Kind.Equals("patch", StringComparison.OrdinalIgnoreCase));
        var hasCode = Attachments.Any(attachment => attachment.Kind.Equals("code", StringComparison.OrdinalIgnoreCase));
        var hasDir = Attachments.Any(attachment => attachment.Kind.Equals("dir", StringComparison.OrdinalIgnoreCase));
        var wantsPatchReview = message.Contains("review", StringComparison.OrdinalIgnoreCase)
            || message.Contains("repair", StringComparison.OrdinalIgnoreCase)
            || message.Contains("fix patch", StringComparison.OrdinalIgnoreCase);

        if (hasPatch && wantsPatchReview)
        {
            return ContextCapsulePhase.PatchReview;
        }

        if (hasCode)
        {
            return ContextCapsulePhase.PatchWrite;
        }

        if (hasDir)
        {
            return ContextCapsulePhase.FileRequest;
        }

        return ContextCapsulePhase.Chat;
    }

    private string ResolvePatchTaskMessage(string message)
    {
        if (!IsLikelyCcRequestList(message))
        {
            _lastUserRequest = message;
            return message;
        }

        return string.IsNullOrWhiteSpace(_lastUserRequest)
            ? message
            : _lastUserRequest;
    }

    private static bool IsLikelyCcRequestList(string text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return false;
        }

        var requestLike = lines.Count(line =>
        {
            var clean = ContextPromptBuilder.NormalizeCodeExportRequestLine(line);
            return clean.Equals("END", StringComparison.OrdinalIgnoreCase)
                || ContextPromptBuilder.IsCodeExportRequestLine(clean);
        });
        return requestLike >= Math.Max(1, lines.Length - 1);
    }

    private IReadOnlyList<string> FindMissingRequestPaths(IReadOnlyList<string> requestLines)
    {
        var root = ResolveEffectiveProjectRootPath();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        var missing = new List<string>();
        foreach (var line in requestLines)
        {
            var requestPath = ExtractRequestPathForValidation(line);
            if (string.IsNullOrWhiteSpace(requestPath))
            {
                continue;
            }

            if (!RequestPathExists(root, requestPath))
            {
                missing.Add(requestPath);
            }
        }

        return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private string ResolveEffectiveProjectRootPath()
    {
        return string.IsNullOrWhiteSpace(ActiveProjectRoot)
            ? _processService.ContextRoot
            : ActiveProjectRoot;
    }

    private static string ExtractRequestPathForValidation(string requestLine)
    {
        var clean = ContextPromptBuilder.NormalizeCodeExportRequestLine(requestLine);
        if (string.IsNullOrWhiteSpace(clean)
            || clean.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("FUNC:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("SYMBOL:", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        if (clean.StartsWith("FUNCTION ", StringComparison.OrdinalIgnoreCase))
        {
            var body = clean["FUNCTION ".Length..].Trim();
            var separator = body.IndexOf(" :: ", StringComparison.Ordinal);
            return separator > 0 ? body[..separator].Trim() : "";
        }

        return clean;
    }

    private static bool RequestPathExists(string projectRoot, string requestPath)
    {
        try
        {
            if (requestPath.Contains('*', StringComparison.Ordinal) || requestPath.Contains('?', StringComparison.Ordinal))
            {
                return RequestWildcardPathExists(projectRoot, requestPath);
            }

            var fullPath = ResolveProjectRelativePath(projectRoot, requestPath);
            return !string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool RequestWildcardPathExists(string projectRoot, string requestPath)
    {
        var normalized = requestPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var directoryPart = Path.GetDirectoryName(normalized) ?? "";
        var pattern = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var searchRoot = string.IsNullOrWhiteSpace(directoryPart)
            ? Path.GetFullPath(projectRoot)
            : ResolveProjectRelativePath(projectRoot, directoryPart);
        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
        {
            return false;
        }

        return Directory.EnumerateFiles(searchRoot, pattern, SearchOption.TopDirectoryOnly).Any();
    }

    private static string ResolveProjectRelativePath(string projectRoot, string relativePath)
    {
        var root = Path.GetFullPath(projectRoot);
        var normalizedRelative = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalizedRelative));
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : "";
    }

    private LocalLlmModelViewModel? ResolveModelForPhase(ContextCapsulePhase phase)
    {
        if (SelectedLocalModel is { IsInstalled: true } selected)
        {
            return selected;
        }

        var preferredId = phase switch
        {
            ContextCapsulePhase.FileRequest => FileRequestModelId,
            ContextCapsulePhase.PatchWrite => PatchWriteModelId,
            ContextCapsulePhase.PatchReview => PatchReviewModelId,
            _ => ChatModelId
        };

        return InstalledLocalModels.FirstOrDefault(model => string.Equals(model.Id, preferredId, StringComparison.OrdinalIgnoreCase))
            ?? InstalledLocalModels.FirstOrDefault();
    }

    private async Task<IReadOnlyList<ContextCapsuleAttachment>> BuildCapsuleAttachmentsAsync(ContextCapsulePhase phase)
    {
        var results = new List<ContextCapsuleAttachment>();
        foreach (var attachment in Attachments)
        {
            var included = ShouldIncludeAttachmentForPhase(attachment, phase) && File.Exists(attachment.Path);
            var text = "";
            if (included)
            {
                try
                {
                    text = await File.ReadAllTextAsync(attachment.Path);
                }
                catch (Exception ex)
                {
                    included = false;
                    text = $"[could not read attachment: {ex.Message}]";
                }
            }

            results.Add(new ContextCapsuleAttachment(
                attachment.DisplayTitle,
                attachment.Kind,
                attachment.Path,
                text,
                included));
        }

        return results;
    }

    private static bool ShouldIncludeAttachmentForPhase(ContextControlAttachmentViewModel attachment, ContextCapsulePhase phase)
    {
        if (!attachment.IncludeInPrompt)
        {
            return false;
        }

        if (attachment.Kind.Equals("dir", StringComparison.OrdinalIgnoreCase))
        {
            return phase == ContextCapsulePhase.FileRequest;
        }

        if (attachment.Kind.Equals("code", StringComparison.OrdinalIgnoreCase))
        {
            return phase is ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview;
        }

        if (attachment.Kind.Equals("patch", StringComparison.OrdinalIgnoreCase))
        {
            return phase == ContextCapsulePhase.PatchReview;
        }

        return true;
    }

    private static string FormatCapsulePhase(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => "file request",
            ContextCapsulePhase.PatchWrite => "patch write",
            ContextCapsulePhase.PatchReview => "patch review",
            _ => "chat"
        };
    }

    private async Task RunBusyAsync(string label, Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        PhaseTitle = label;
        PhaseDetail = "Starting...";
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
        if (changed)
        {
            NotifyAttachmentStateChanged();
        }

        return changed;
    }

    private void RemoveAttachmentsByKind(params string[] kinds)
    {
        if (kinds.Length == 0 || Attachments.Count == 0)
        {
            return;
        }

        var kindSet = new HashSet<string>(kinds, StringComparer.OrdinalIgnoreCase);
        var removed = false;
        for (var index = Attachments.Count - 1; index >= 0; index--)
        {
            if (!kindSet.Contains(Attachments[index].Kind))
            {
                continue;
            }

            Attachments.RemoveAt(index);
            removed = true;
        }

        if (removed)
        {
            NotifyAttachmentStateChanged();
        }
    }

    private void NotifyAttachmentStateChanged()
    {
        OnPropertyChanged(nameof(AttachmentSummary));
        OnPropertyChanged(nameof(PromptFooterSummary));
        OnPropertyChanged(nameof(PromptTokenomicsLabel));
        OnPropertyChanged(nameof(PromptContextPressureLabel));
        OnPropertyChanged(nameof(HasAttachments));
    }

    private static string CleanModelId(string? modelId, string fallback)
    {
        return string.IsNullOrWhiteSpace(modelId) ? fallback : modelId.Trim();
    }

    private static string EnsureEndsWithEnd(string text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.Equals("END", StringComparison.OrdinalIgnoreCase))
            .ToList();
        lines.Add("END");
        return string.Join(Environment.NewLine, lines);
    }

    private static int EstimateComfortableBudget(string? comfortableContext)
    {
        var label = comfortableContext ?? "";
        if (label.Contains("8K", StringComparison.OrdinalIgnoreCase))
        {
            return 8192;
        }

        return 4096;
    }

    private AttachmentTokenBudget EstimateAttachmentBudget()
    {
        var fullTokens = 0;
        var sentTokens = 0;
        var remainingCharacters = ContextCapsuleBuilder.MaxAttachmentCharacters;
        var clipped = false;
        var phase = ResolveCapsulePhase(PromptText);

        foreach (var attachment in Attachments.Where(attachment => ShouldIncludeAttachmentForPhase(attachment, phase)))
        {
            var characters = EstimateAttachmentCharacters(attachment);
            if (characters <= 0)
            {
                continue;
            }

            fullTokens += EstimateTokensForCharacterCount(characters);
            if (remainingCharacters <= 0)
            {
                clipped = true;
                continue;
            }

            var sentCharacters = Math.Min(characters, remainingCharacters);
            var clippedThisAttachment = characters > remainingCharacters;
            var billedCharacters = sentCharacters
                + (clippedThisAttachment ? Environment.NewLine.Length + ContextCapsuleBuilder.AttachmentClipMarker.Length : 0);
            sentTokens += EstimateTokensForCharacterCount(billedCharacters);
            remainingCharacters -= Math.Max(0, billedCharacters);
            clipped |= clippedThisAttachment;
        }

        return new AttachmentTokenBudget(sentTokens, fullTokens, clipped);
    }

    private static int EstimateAttachmentCharacters(ContextControlAttachmentViewModel attachment)
    {
        if (attachment is null || string.IsNullOrWhiteSpace(attachment.Path) || !File.Exists(attachment.Path))
        {
            return 0;
        }

        try
        {
            var length = new FileInfo(attachment.Path).Length;
            return length > int.MaxValue ? int.MaxValue : (int)length;
        }
        catch
        {
            return 0;
        }
    }

    private static int EstimateTokensForCharacterCount(long characterCount)
    {
        if (characterCount <= 0)
        {
            return 0;
        }

        var tokens = (long)Math.Ceiling(characterCount / 4d);
        return tokens > int.MaxValue ? int.MaxValue : (int)tokens;
    }

    private sealed record AttachmentTokenBudget(int SentTokens, int FullTokens, bool IsClipped);

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
        (ApplyAllPatchCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (SendCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (RefreshLocalModelsCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (InstallOllamaCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
        (PullLocalModelCommand as RelayCommand<LocalLlmModelViewModel>)?.RaiseCanExecuteChanged();
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
