// CC-DESC: Prompt, dock, timeline, install, transfer, and terminal bindable properties.

// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowBusyPromptProgress));
                OnPromptBarLayoutChanged();
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

                OnPromptBarLayoutChanged();
                OnPropertyChanged(nameof(PromptBarOpacity));
                _settings.PromptBarOpenByDefault = value;
                SaveSettingsQuietly();
                SaveChatHistory();
            }
        }
    }

    public double PromptBarHeight => IsPromptOpen
        ? CalculatePromptBarBaseHeight()
            + (ShowBusyPromptProgress ? BusyPromptProgressHeight : 0)
            + (IsCcTimelinePanelVisible ? 70 : 0)
            + (IsTransferProgressActive ? 72 : 0)
            + (ChatRequestProgressItems.Count * 68)
        : 0;

    public double PromptBarOpacity => IsPromptOpen ? 1 : 0;

    private void OnPromptBarLayoutChanged()
    {
        OnPropertyChanged(nameof(PromptBarHeight));
    }

    public bool IsPromptTypingActive
    {
        get => _isPromptTypingActive;
        private set
        {
            if (SetProperty(ref _isPromptTypingActive, value))
            {
                OnPromptBarLayoutChanged();
            }
        }
    }

    public bool IsLogPanelOpen => string.Equals(_dockPanelKey, "log", StringComparison.OrdinalIgnoreCase);

    public bool IsChatPanelOpen => string.Equals(_dockPanelKey, "chat", StringComparison.OrdinalIgnoreCase);

    public bool IsCcTimelineExpanded
    {
        get => _isCcTimelineExpanded;
        set
        {
            if (SetProperty(ref _isCcTimelineExpanded, value))
            {
                OnPropertyChanged(nameof(CcTimelineToggleLabel));
                OnPropertyChanged(nameof(IsCcTimelinePanelVisible));
                OnPromptBarLayoutChanged();
            }
        }
    }

    public bool IsAutopilotEnabled
    {
        get => _isAutopilotEnabled;
        private set
        {
            if (SetProperty(ref _isAutopilotEnabled, value))
            {
                OnPropertyChanged(nameof(AutopilotModeLabel));
                OnPropertyChanged(nameof(AutopilotModeToolTip));
                _settings.IsAutopilotEnabled = value;
                SaveSettingsQuietly();
            }
        }
    }

    public string AutopilotModeLabel => IsAutopilotEnabled ? "CC flow on" : "Raw on";

    public string AutopilotModeToolTip => IsAutopilotEnabled
        ? "CC flow is active. Click to switch to raw chat, which sends only your text."
        : "Raw chat is active. Click to switch to CC flow, which sends ContextControl capsules with DIR/CC attachments and workflow instructions.";

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
                OnPropertyChanged(nameof(LlmCompactInfoLabel));
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
                OnPropertyChanged(nameof(LlmCompactInfoLabel));
                UpdateOllamaBackendDependency();
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
        "Automatic Ollama installation is disabled because it is not ContextControl-local.";

    public bool IsExternalDependencyDeletePromptOpen
    {
        get => _isExternalDependencyDeletePromptOpen;
        private set => SetProperty(ref _isExternalDependencyDeletePromptOpen, value);
    }

    public string ExternalDependencyDeletePromptTitle => _pendingExternalDependencyDelete is { } dependency
        ? $"Force install {dependency.DisplayName}"
        : "Force install dependency";

    public string ExternalDependencyDeletePromptMessage =>
        "Our systems detected that this dependency is most likely outside the Context Control managed dependency control flow."
        + Environment.NewLine
        + Environment.NewLine
        + "Deleting it might result in issues with your existing development environment.";

    public string OllamaInstallButtonLabel => IsOllamaInstalled
        ? "Ollama installed"
        : "Open Ollama";

    public string OllamaInstallerSizeLabel => "External app install; models below download separately";

    public string OllamaInstallCommandLabel => LocalLlmService.OllamaDownloadPageUrl;

    public bool ShowBusyPromptProgress => IsBusy && !IsTransferProgressActive;

    public bool IsTransferProgressActive
    {
        get => _isTransferProgressActive;
        private set
        {
            if (SetProperty(ref _isTransferProgressActive, value))
            {
                OnPropertyChanged(nameof(ShowBusyPromptProgress));
                OnPromptBarLayoutChanged();
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
        private set
        {
            if (SetProperty(ref _transferProgressStatus, value))
            {
                OnPropertyChanged(nameof(TransferProgressHistoryPositionLabel));
            }
        }
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

    public string TransferProgressHistoryPositionLabel =>
        _transferProgressHistory.Count == 0 || _transferProgressHistoryIndex < 0
            ? ""
            : $"{_transferProgressHistoryIndex + 1:N0}/{_transferProgressHistory.Count:N0}";

    public bool CanCloseTransferProgress =>
        IsTransferProgressActive
        && (_isTransferProgressDismissible
            || _transferProgressCancellation is { IsCancellationRequested: false });

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
        private set
        {
            if (SetProperty(ref _phaseTitle, value))
            {
                UpdateCcTimelineFromStatus();
            }
        }
    }

    public string PhaseDetail
    {
        get => _phaseDetail;
        private set
        {
            if (SetProperty(ref _phaseDetail, value))
            {
                MirrorPhaseStatusToTerminal();
                UpdateCcTimelineFromStatus();
            }
        }
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
                SavePromptDraftToSelectedChat();
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

                    OnPromptBarLayoutChanged();
                    return;
                }

                if (!isLargePrompt && IsPromptTypingActive)
                {
                    OnPromptBarLayoutChanged();
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
            var clean = NormalizePromptModeKey(value);

            if (SetProperty(ref _promptModeKey, clean))
            {
                OnPropertyChanged(nameof(IsContextPromptMode));
                OnPropertyChanged(nameof(IsChatPromptMode));
                OnPropertyChanged(nameof(IsCodexPromptMode));
                OnPropertyChanged(nameof(IsTerminalPromptMode));
                OnPropertyChanged(nameof(IsMessagePromptMode));
                OnPropertyChanged(nameof(PromptWatermark));
                OnPropertyChanged(nameof(PromptSendButtonLabel));
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
                OnPropertyChanged(nameof(CodexStatus));
                OnPropertyChanged(nameof(IsCcPromptActionRowVisible));
                OnPropertyChanged(nameof(ChatWorkspaceTitle));
                OnPropertyChanged(nameof(PromptContextPressureLabel));
                OnPropertyChanged(nameof(PromptTokenomicsLabel));
                OnPropertyChanged(nameof(PromptFooterSummary));
                _settings.PromptModeKey = clean;

                SaveSettingsQuietly();
                SaveChatHistory();
            }
        }
    }

    public bool IsContextPromptMode => string.Equals(PromptModeKey, "context", StringComparison.OrdinalIgnoreCase);

    public bool IsChatPromptMode => IsContextPromptMode;

    public bool IsCodexPromptMode => string.Equals(PromptModeKey, "codex", StringComparison.OrdinalIgnoreCase);

    public bool IsTerminalPromptMode => string.Equals(PromptModeKey, "terminal", StringComparison.OrdinalIgnoreCase);

    public bool IsMessagePromptMode => !IsTerminalPromptMode;

    public bool IsImageGenWorkspaceActive
    {
        get => _isImageGenWorkspaceActive;
        set
        {
            if (SetProperty(ref _isImageGenWorkspaceActive, value))
            {
                OnPropertyChanged(nameof(PromptWatermark));
                OnPropertyChanged(nameof(AttachmentSummary));
                OnPropertyChanged(nameof(PromptFooterSummary));
                OnPropertyChanged(nameof(PromptTokenomicsLabel));
                OnPropertyChanged(nameof(PromptContextPressureLabel));
                OnPropertyChanged(nameof(PromptSendButtonLabel));
                OnPropertyChanged(nameof(ActiveInstalledLocalModels));
                OnPropertyChanged(nameof(SelectedActiveLocalModel));
                OnPropertyChanged(nameof(SelectedLocalModelLabel));
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
                OnPropertyChanged(nameof(IsCcPromptChromeVisible));
                OnPropertyChanged(nameof(IsPromptModeSwitcherVisible));
                OnPropertyChanged(nameof(PromptPrimaryModeButtonLabel));
                OnPropertyChanged(nameof(PromptPrimaryModeButtonToolTip));
                OnPropertyChanged(nameof(IsCcTimelinePanelVisible));
                OnPropertyChanged(nameof(IsCcPromptActionRowVisible));
                OnPromptBarLayoutChanged();
                SwitchConversationKindForWorkspace();
            }
        }
    }

    public string PromptWatermark => IsChatPromptMode
        ? IsImageGenWorkspaceActive
            ? "Describe the image you want to generate..."
            : "Ask the selected local model, or paste CC request/patch text..."
        : IsCodexPromptMode ? "Ask Codex through the ContextControl DIR -> CC -> GO flow..."
        : IsTerminalPromptMode ? "Terminal output"
        : "Message Context Control...";

    public string PromptSendButtonLabel => IsImageGenWorkspaceActive
        ? "Generate"
        : IsCodexPromptMode ? "Send to Codex" : "Send";

    public string PromptModelCapabilityHint
    {
        get
        {
            if (IsCodexPromptMode)
            {
                return CodexStatus;
            }

            if (IsImageGenWorkspaceActive)
            {
                return SelectedImageGenerationModel is null
                    ? "Select an image generation model"
                    : "Prompt-only image generation";
            }

            return SelectedLocalModel?.IsImageModel == true
                ? "Accepts image input"
                : "";
        }
    }

    public bool HasPromptModelCapabilityHint => !string.IsNullOrWhiteSpace(PromptModelCapabilityHint);

    public bool IsCcPromptChromeVisible => !IsImageGenWorkspaceActive;

    public bool IsPromptModeSwitcherVisible => IsCcPromptChromeVisible || IsImageGenWorkspaceActive;

    public string PromptPrimaryModeButtonLabel => IsImageGenWorkspaceActive ? "Prompt" : "CC";

    public string PromptPrimaryModeButtonToolTip => IsImageGenWorkspaceActive
        ? "Image generation prompt"
        : "Context Control prompt";

    public bool IsCcTimelinePanelVisible => IsCcPromptChromeVisible && IsCcTimelineExpanded;

    public bool IsCcPromptActionRowVisible => IsCcPromptChromeVisible && (IsContextPromptMode || IsCodexPromptMode);

    public string ChatWorkspaceTitle => IsCodexPromptMode ? "Codex Chat" : "CC Chat";

    public bool IsCodexRequestRunning
    {
        get => _isCodexRequestRunning;
        private set
        {
            if (SetProperty(ref _isCodexRequestRunning, value))
            {
                OnPropertyChanged(nameof(CodexStatus));
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
                (OpenCodexLoginCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (RefreshCodexStatusCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (RunCodexDoctorCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (CancelCodexRequestCommand as RelayCommand<ChatRequestProgressViewModel>)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCodexAuthenticated
    {
        get => _isCodexAuthenticated;
        private set
        {
            if (SetProperty(ref _isCodexAuthenticated, value))
            {
                OnPropertyChanged(nameof(CodexLoginButtonLabel));
                OnPropertyChanged(nameof(CodexSetupSummary));
                OnPropertyChanged(nameof(CodexSetupStatusKind));
            }
        }
    }

    public bool IsCodexLoginRequired
    {
        get => _isCodexLoginRequired;
        private set
        {
            if (SetProperty(ref _isCodexLoginRequired, value))
            {
                OnPropertyChanged(nameof(CodexLoginButtonLabel));
                OnPropertyChanged(nameof(CodexSetupSummary));
                OnPropertyChanged(nameof(CodexSetupStatusKind));
            }
        }
    }

    public bool IsRefreshingCodexStatus
    {
        get => _isRefreshingCodexStatus;
        private set
        {
            if (SetProperty(ref _isRefreshingCodexStatus, value))
            {
                OnPropertyChanged(nameof(CodexSetupSummary));
                (RefreshCodexStatusCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (RunCodexDoctorCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string CodexLoginButtonLabel => IsCodexAuthenticated ? "Relogin" : "Login";

    public string CodexSetupSummary => IsRefreshingCodexStatus
        ? "Checking Codex CLI login..."
        : IsCodexAuthenticated
            ? "Codex login ready"
            : IsCodexLoginRequired
                ? "Codex login required"
                : "Codex setup pending";

    public string CodexSetupStatusKind => IsCodexAuthenticated ? "ready" : IsCodexLoginRequired ? "login" : "pending";

    public string CodexStatus
    {
        get => _codexStatus;
        private set
        {
            if (SetProperty(ref _codexStatus, string.IsNullOrWhiteSpace(value) ? "Codex CLI read-only CC capsule" : value))
            {
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
            }
        }
    }

    public string TerminalOutputText
    {
        get => _terminalOutputText;
        private set => SetProperty(ref _terminalOutputText, value ?? "");
    }

    private static string NormalizePromptModeKey(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "codex" => "codex",
            "terminal" => "terminal",
            _ => "context"
        };
    }

}
