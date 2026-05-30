// CC-DESC: Extracted ContextControlViewModel system slice.
// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private void LoadChatHistory()
    {
        var document = _chatHistoryService.Load(
            ResolveConversationScopeKey(_chatHistoryScopeKey, _activeConversationKind),
            _activeConversationKind,
            includeFallbacks: IsChatConversationKind(_activeConversationKind));
        ChatSessions.Clear();
        ChatMessages.Clear();
        SelectedChatSession = null;
        OnPropertyChanged(nameof(HasChatSessions));
        OnPropertyChanged(nameof(ChatHistoryPanelTitle));
        OnPropertyChanged(nameof(ChatHistorySummary));
        RestoreProjectPromptState(document);
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

    private void RestoreProjectPromptState(ChatHistoryDocument document)
    {
        Attachments.Clear();
        _legacyPendingAttachments = (document.Attachments ?? [])
            .Where(attachment => !AutoAttachmentKinds.Contains(attachment.Kind)
                && !string.IsNullOrWhiteSpace(attachment.Path)
                && File.Exists(attachment.Path))
            .ToArray();

        _lastAssistantPatchBlocks = "";
        _lastUserRequest = document.LastUserRequest ?? "";
        _semanticIndex = null;
        LastExportPath = "";
        IsPatchPlanReady = false;
        PatchSummary = "No patch loaded.";
        UpdatePatchPlanActions(null);

        var route = string.IsNullOrWhiteSpace(document.SelectedRoute) ? _settings.SelectedAiRoute : document.SelectedRoute;
        if (!string.IsNullOrWhiteSpace(route) && RouteOptions.Contains(route))
        {
            SelectedRoute = route;
        }

        if (!string.IsNullOrWhiteSpace(document.SelectedLocalModelId))
        {
            _settings.SelectedLocalModel = document.SelectedLocalModelId;
            var installed = InstalledLocalModels.FirstOrDefault(model => string.Equals(model.Id, document.SelectedLocalModelId, StringComparison.OrdinalIgnoreCase));
            if (installed is not null)
            {
                SelectedLocalModel = installed;
            }
        }

        var selectedImageModelId = document.SelectedImageModelId;
        if (string.IsNullOrWhiteSpace(selectedImageModelId) && IsImageGenConversationKind(_activeConversationKind))
        {
            selectedImageModelId = string.IsNullOrWhiteSpace(_settings.SelectedImageModel)
                ? document.SelectedLocalModelId
                : _settings.SelectedImageModel;
        }

        if (!string.IsNullOrWhiteSpace(selectedImageModelId))
        {
            var installedImage = InstalledImageGenerationModels.FirstOrDefault(model => string.Equals(model.Id, selectedImageModelId, StringComparison.OrdinalIgnoreCase));
            installedImage ??= InstalledImageGenerationModels.FirstOrDefault(model =>
                string.Equals(model.Id, _settings.SelectedImageModel, StringComparison.OrdinalIgnoreCase));
            if (installedImage is not null)
            {
                _settings.SelectedImageModel = installedImage.Id;
                SelectedImageGenerationModel = installedImage;
            }
        }

        _legacyPromptText = document.PromptText ?? "";
        PromptText = "";
        if (!string.IsNullOrWhiteSpace(document.PromptModeKey))
        {
            PromptModeKey = document.PromptModeKey;
        }

        IsAutopilotEnabled = document.IsAutopilotEnabled ?? _settings.IsAutopilotEnabled;
        IsPromptOpen = document.IsPromptOpen || _settings.PromptBarOpenByDefault;
        TerminalOutputText = document.TerminalOutputText ?? "";
        NotifyAttachmentStateChanged();
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
        SelectChatSession(session, save: false);

        if (resetWorkflow)
        {
            ResetChatWorkflowState();
        }

        if (save)
        {
            SaveChatHistory();
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

        if (SelectedChatSession is { } previous && !ReferenceEquals(previous, session))
        {
            previous.SetPendingAttachments(Attachments);
            previous.SetDraftPromptText(PromptText);
        }

        _isSwitchingChatSession = true;
        try
        {
            foreach (var item in ChatSessions)
            {
                item.IsActive = ReferenceEquals(item, session);
            }

            SelectedChatSession = session;
            ChatMessages.Load(session);

            LoadPromptDraftForSession(session);
            LoadPendingAttachmentsForSession(session);

            _lastAssistantPatchBlocks = session.FindLastPatchText();
            PhaseTitle = "Chat selected";
            PhaseDetail = session.Title;
        }
        finally
        {
            _isSwitchingChatSession = false;
        }

        if (save)
        {
            SaveChatHistory();
        }
    }

    private void LoadPendingAttachmentsForSession(ChatSessionViewModel session)
    {
        _isSyncingChatAttachments = true;
        try
        {
            Attachments.Clear();
            var pending = session.CreatePendingAttachments()
                .Where(attachment => !string.IsNullOrWhiteSpace(attachment.Path) && File.Exists(attachment.Path))
                .ToArray();
            if (pending.Length == 0 && _legacyPendingAttachments.Count > 0)
            {
                pending = _legacyPendingAttachments
                    .Select(attachment =>
                    {
                        var viewModel = new ContextControlAttachmentViewModel(
                            string.IsNullOrWhiteSpace(attachment.Label) ? Path.GetFileName(attachment.Path) : attachment.Label,
                            attachment.Path,
                            attachment.Kind);
                        viewModel.IncludeInPrompt = attachment.IncludeInPrompt;
                        return viewModel;
                    })
                    .ToArray();
                session.SetPendingAttachments(pending);
                _legacyPendingAttachments = [];
            }

            foreach (var attachment in pending)
            {
                Attachments.Add(attachment);
            }
        }
        finally
        {
            _isSyncingChatAttachments = false;
        }

        NotifyAttachmentStateChanged();
    }

    private void LoadPromptDraftForSession(ChatSessionViewModel session)
    {
        _isSyncingChatDraft = true;
        try
        {
            var draft = session.DraftPromptText;
            if (string.IsNullOrWhiteSpace(draft) && !string.IsNullOrWhiteSpace(_legacyPromptText))
            {
                draft = _legacyPromptText;
                session.SetDraftPromptText(draft);
                _legacyPromptText = "";
            }

            PromptText = draft;
        }
        finally
        {
            _isSyncingChatDraft = false;
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
        _semanticIndex = null;
        PromptText = "";
        LastExportPath = "";
        IsPatchPlanReady = false;
        PatchSummary = "No patch loaded.";
        UpdatePatchPlanActions(null);
        NotifyAttachmentStateChanged();
        PhaseTitle = "New chat";
        PhaseDetail = "Fresh local chat with no DIR, CC, patch, or previous request context.";
    }

    private void SavePendingAttachmentsToSelectedChat()
    {
        if (_isSyncingChatAttachments || _isSwitchingChatSession)
        {
            return;
        }

        SelectedChatSession?.SetPendingAttachments(Attachments);
    }

    private void SavePromptDraftToSelectedChat()
    {
        if (_isSyncingChatDraft || _isSwitchingChatSession)
        {
            return;
        }

        SelectedChatSession?.SetDraftPromptText(PromptText);
    }

    private void AppendChatMessage(LocalLlmChatMessageViewModel message)
    {
        AppendChatMessageToSession(EnsureSelectedChatSession(), message);
    }

    private ChatSessionViewModel EnsureSelectedChatSession()
    {
        if (SelectedChatSession is { } selected)
        {
            return selected;
        }

        CreateNewChatSession(save: false, resetWorkflow: false);
        return SelectedChatSession ?? ChatSessions[0];
    }

    private void AppendChatMessageToSession(ChatSessionViewModel session, LocalLlmChatMessageViewModel message)
    {
        if (!ChatSessions.Contains(session))
        {
            return;
        }

        session.Append(message);
        if (ReferenceEquals(SelectedChatSession, session))
        {
            ChatMessages.Add(message);
        }

        var index = ChatSessions.IndexOf(session);
        if (index > 0)
        {
            ChatSessions.Move(index, 0);
        }

        OnPropertyChanged(nameof(ChatHistorySummary));
        SaveChatHistory();
    }

    private async Task AppendChatMessageToCapturedConversationAsync(
        string scopeKey,
        string conversationKind,
        ChatSessionViewModel session,
        LocalLlmChatMessageViewModel message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AppendChatMessageToCapturedConversation(scopeKey, conversationKind, session, message);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
            AppendChatMessageToCapturedConversation(scopeKey, conversationKind, session, message));
    }

    private void AppendChatMessageToCapturedConversation(
        string scopeKey,
        string conversationKind,
        ChatSessionViewModel session,
        LocalLlmChatMessageViewModel message)
    {
        var capturedScope = ResolveConversationScopeKey(scopeKey, conversationKind);
        var currentScope = ResolveConversationScopeKey(_chatHistoryScopeKey, _activeConversationKind);
        if (string.Equals(capturedScope, currentScope, StringComparison.OrdinalIgnoreCase)
            && string.Equals(conversationKind, _activeConversationKind, StringComparison.OrdinalIgnoreCase))
        {
            var liveSession = ChatSessions.FirstOrDefault(item => ReferenceEquals(item, session))
                ?? ChatSessions.FirstOrDefault(item => string.Equals(item.Id, session.Id, StringComparison.OrdinalIgnoreCase));
            if (liveSession is null)
            {
                liveSession = session;
                ChatSessions.Insert(0, liveSession);
                OnPropertyChanged(nameof(HasChatSessions));
                OnPropertyChanged(nameof(ChatHistorySummary));
                if (SelectedChatSession is null)
                {
                    SelectChatSession(liveSession, save: false);
                }
            }

            AppendChatMessageToSession(liveSession, message);
            return;
        }

        AppendChatMessageToStoredConversation(capturedScope, conversationKind, session, message);
    }

    private void AppendChatMessageToStoredConversation(
        string resolvedScopeKey,
        string conversationKind,
        ChatSessionViewModel session,
        LocalLlmChatMessageViewModel message)
    {
        try
        {
            var document = _chatHistoryService.Load(
                resolvedScopeKey,
                conversationKind,
                includeFallbacks: IsChatConversationKind(conversationKind));
            var sessionData = document.Sessions.FirstOrDefault(item =>
                    string.Equals(item.Id, session.Id, StringComparison.OrdinalIgnoreCase))
                ?? session.ToData();
            var storedSession = new ChatSessionViewModel(sessionData);
            storedSession.Append(message);

            document.ConversationKind = conversationKind;
            document.SelectedSessionId = string.IsNullOrWhiteSpace(document.SelectedSessionId)
                ? storedSession.Id
                : document.SelectedSessionId;
            document.Sessions.RemoveAll(item => string.Equals(item.Id, storedSession.Id, StringComparison.OrdinalIgnoreCase));
            document.Sessions.Insert(0, storedSession.ToData());

            _chatHistoryService.Save(
                document,
                resolvedScopeKey,
                conversationKind,
                mirrorDefaultScope: IsChatConversationKind(conversationKind));
        }
        catch (Exception ex)
        {
            Log("warn", $"Image Gen response history update skipped: {ex.Message}");
        }
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
        var detail = BuildPatchFailureDetail(result, summary);
        var builder = new StringBuilder();
        builder.AppendLine($"{title}.");
        builder.AppendLine(detail);
        builder.AppendLine();
        builder.AppendLine("GO needs raw BEGIN/END CC-REPLACE blocks with FILE and MODE headers.");
        builder.AppendLine("Minimum shape:");
        builder.AppendLine("BEGIN CC-REPLACE");
        builder.AppendLine("FILE: path/relative/to/project");
        builder.AppendLine("MODE: replace_region");
        builder.AppendLine("NAME: marker_name");
        builder.AppendLine("---");
        builder.AppendLine("replacement text");
        builder.AppendLine("END CC-REPLACE");
        return builder.ToString().TrimEnd();
    }

    private static string BuildPatchShapeFailureChatText(string detail)
    {
        var builder = new StringBuilder();
        builder.AppendLine("GO preview cancelled before ccReplace.");
        builder.AppendLine(detail);
        builder.AppendLine();
        builder.AppendLine("Valid minimum shape:");
        builder.AppendLine("BEGIN CC-REPLACE");
        builder.AppendLine("FILE: path/relative/to/project");
        builder.AppendLine("MODE: replace_region");
        builder.AppendLine("NAME: marker_name");
        builder.AppendLine("---");
        builder.AppendLine("replacement text");
        builder.AppendLine("END CC-REPLACE");
        builder.AppendLine();
        builder.AppendLine("For includes:");
        builder.AppendLine("BEGIN CC-REPLACE");
        builder.AppendLine("FILE: path/relative/to/project");
        builder.AppendLine("MODE: insert_include");
        builder.AppendLine("HEADER: <memory>");
        builder.AppendLine("END CC-REPLACE");
        return builder.ToString().TrimEnd();
    }

    private static string BuildPatchFailureDetail(ContextControlCommandResult result, PatchPlanSummary summary)
    {
        var commandLines = InterestingLines(result.StandardError)
            .Concat(InterestingLines(result.StandardOutput))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(10)
            .ToArray();
        var commandDetail = commandLines.Length > 0 ? string.Join(Environment.NewLine, commandLines) : "";
        var summaryError = summary.Error ?? "";

        if (!string.IsNullOrWhiteSpace(commandDetail)
            && (summaryError.Equals("No patch plan returned.", StringComparison.OrdinalIgnoreCase)
                || summaryError.Equals("Plan JSON could not be parsed cleanly.", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(summaryError)
                || !result.Succeeded))
        {
            return commandDetail;
        }

        return string.IsNullOrWhiteSpace(summaryError)
            ? FirstErrorLine(result)
            : summaryError;
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
        if (_isSwitchingProjectState || _isSwitchingChatSession || _isSwitchingConversationKind)
        {
            return;
        }

        try
        {
            SavePendingAttachmentsToSelectedChat();
            SavePromptDraftToSelectedChat();
            if (ChatSessions.Count == 0)
            {
                return;
            }

            _chatHistoryService.Save(new ChatHistoryDocument
            {
                ConversationKind = _activeConversationKind,
                SelectedSessionId = SelectedChatSession?.Id,
                PromptText = "",
                PromptModeKey = PromptModeKey,
                IsAutopilotEnabled = IsAutopilotEnabled,
                IsPromptOpen = IsPromptOpen,
                LastUserRequest = _lastUserRequest,
                SelectedRoute = SelectedRoute,
                SelectedLocalModelId = SelectedLocalModel?.Id ?? _settings.SelectedLocalModel,
                SelectedImageModelId = SelectedImageGenerationModel?.Id ?? _settings.SelectedImageModel,
                TerminalOutputText = TerminalOutputText,
                Sessions = ChatSessions.Select(session => session.ToData()).ToList()
            },
            ResolveConversationScopeKey(_chatHistoryScopeKey, _activeConversationKind),
            _activeConversationKind,
            mirrorDefaultScope: IsChatConversationKind(_activeConversationKind));
        }
        catch (Exception ex)
        {
            Log("warn", $"Chat history save skipped: {ex.Message}");
        }
    }

    private void SwitchConversationKindForWorkspace()
    {
        var nextKind = IsImageGenWorkspaceActive ? ImageGenConversationKind : ChatConversationKind;
        if (string.Equals(_activeConversationKind, nextKind, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SaveChatHistory();
        _isSwitchingConversationKind = true;
        try
        {
            _activeConversationKind = nextKind;
            LoadChatHistory();
        }
        finally
        {
            _isSwitchingConversationKind = false;
        }

        PhaseTitle = IsImageGenConversationKind(nextKind) ? "Image Gen chat loaded" : "Chat loaded";
        PhaseDetail = IsImageGenConversationKind(nextKind)
            ? "Image generation has its own chat history."
            : "Regular chat history restored.";
        OnPropertyChanged(nameof(ChatHistoryPanelTitle));
        OnPropertyChanged(nameof(ChatHistorySummary));
    }

    private static bool IsChatConversationKind(string? conversationKind)
    {
        return !IsImageGenConversationKind(conversationKind);
    }

    private static bool IsImageGenConversationKind(string? conversationKind)
    {
        return string.Equals(conversationKind, ImageGenConversationKind, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveConversationScopeKey(string projectScopeKey, string conversationKind)
    {
        var cleanScope = string.IsNullOrWhiteSpace(projectScopeKey) ? "default" : projectScopeKey.Trim();
        return IsImageGenConversationKind(conversationKind)
            ? $"{cleanScope}::imagegen"
            : cleanScope;
    }

}
