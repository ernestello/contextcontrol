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
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private async Task SendAsync()
    {
        var currentMessage = PromptText.Trim();
        if (IsImageGenWorkspaceActive && IsMessagePromptMode)
        {
            await SendImageGenerationAsync(currentMessage);
            return;
        }

        if (IsAutopilotEnabled
            && !string.IsNullOrWhiteSpace(currentMessage)
            && IsLikelyCcRequestList(currentMessage)
            && _promptBuilder.BuildCodeExportRequestLines(currentMessage).Count > 0
            && !HasIncludedAttachmentKind("code")
            && !HasIncludedAttachmentKind("patch"))
        {
            PhaseTitle = "CC request detected";
            PhaseDetail = "Send detected file/function/FIND lines and is running CC export instead of asking the model again.";
            AppendTerminalOutput("Send detected a CC request list; running CC export.");
            await RunCcAsync();
            return;
        }

        if (IsCodexPromptMode)
        {
            var codexMessage = currentMessage;
            if (string.IsNullOrWhiteSpace(codexMessage))
            {
                codexMessage = IsAutopilotEnabled ? BuildEmptyLocalSendMessage() : "";
                if (string.IsNullOrWhiteSpace(codexMessage))
                {
                    PhaseTitle = "Nothing to send";
                    PhaseDetail = IsAutopilotEnabled
                        ? "Write a prompt, run DIR, or attach CC context before sending to Codex."
                        : "Write a prompt first.";
                    return;
                }
            }

            await SendCodexChatAsync(codexMessage);
            return;
        }

        if (IsChatPromptMode || SelectedRoute.StartsWith("Local:", StringComparison.OrdinalIgnoreCase))
        {
            var localMessage = currentMessage;
            if (string.IsNullOrWhiteSpace(localMessage))
            {
                localMessage = IsAutopilotEnabled ? BuildEmptyLocalSendMessage() : "";
                if (string.IsNullOrWhiteSpace(localMessage))
                {
                    PhaseTitle = "Nothing to send";
                    PhaseDetail = IsAutopilotEnabled
                        ? "Write a prompt, run DIR, or attach CC context first."
                        : "Write a prompt first.";
                    return;
                }
            }

            _ = SendLocalChatAsync(localMessage);
            return;
        }

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

    private bool TryResolveDirRequestLocally(string currentMessage)
    {
        var hasDir = Attachments.Any(attachment => attachment.Kind.Equals("dir", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt);
        var hasCode = Attachments.Any(attachment => attachment.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt);
        var hasPatch = Attachments.Any(attachment => attachment.Kind.Equals("patch", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt);
        if (!hasDir || hasCode || hasPatch)
        {
            return false;
        }

        var resolverSource = SelectResolverSourceText(currentMessage);
        if (!IsMeaningfulTaskPrompt(resolverSource))
        {
            return false;
        }

        var result = ResolveFileRequestFromIndex(resolverSource);
        if (!result.HasRequestLines)
        {
            return false;
        }

        _lastUserRequest = resolverSource;
        PromptText = EnsureEndsWithEnd(result.RequestText);
        IsPromptOpen = true;
        PromptModeKey = "context";
        SelectDockPanel("chat");

        var exactCount = result.RequestLines.Count(line => !line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase));
        var findCount = result.RequestLines.Count - exactCount;
        PhaseTitle = result.UsesFindTerms ? "Resolver needs discovery" : "Resolver suggested files";
        PhaseDetail = result.UsesFindTerms
            ? $"Loaded {findCount:N0} FIND request line(s). Press Send or CC to run discovery."
            : $"Loaded {exactCount:N0} exact file request line(s). Press Send or CC to export source.";
        AppendTerminalOutput(result.UsesFindTerms
            ? $"Resolver loaded FIND fallback from DIR context: {findCount:N0} line(s)."
            : $"Resolver loaded exact CC request from DIR context: {exactCount:N0} file(s).");

        var targetSession = EnsureSelectedChatSession();
        var attachmentSnapshot = BuildPendingAttachmentSnapshotForKinds("dir");
        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "user",
            resolverSource,
            "ContextControl",
            "file resolver",
            attachments: attachmentSnapshot));
        ConsumeSentAttachments(attachmentSnapshot);
        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "assistant",
            BuildResolverChatText(result),
            "ContextControl",
            "file resolver"));
        return true;
    }

    private string SelectResolverSourceText(string currentMessage)
    {
        if ((string.IsNullOrWhiteSpace(currentMessage)
                || LooksLikeAttachmentDiagnostic(currentMessage)
                || LooksLikeContextOnlyPrompt(currentMessage))
            && !string.IsNullOrWhiteSpace(_lastUserRequest))
        {
            return _lastUserRequest;
        }

        return currentMessage;
    }

    private ContextFileResolveResult ResolveFileRequestFromIndex(string userMessage)
    {
        return _fileResolver.Resolve(userMessage, _semanticIndex);
    }

    private static string BuildResolverChatText(ContextFileResolveResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.UsesFindTerms
            ? "ContextControl generated focused discovery lines from the DIR semantic index."
            : "ContextControl resolved exact CC request lines from the DIR semantic index.");
        builder.AppendLine();
        builder.AppendLine("```cc-request");
        builder.AppendLine(result.RequestText);
        builder.AppendLine("```");

        if (result.Reasons.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Reasons:");
            foreach (var reason in result.Reasons.Take(6))
            {
                builder.AppendLine($"- {reason}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildEmptyLocalSendMessage()
    {
        if (Attachments.Any(attachment => attachment.Kind.Equals("code", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt))
        {
            return string.IsNullOrWhiteSpace(_lastUserRequest)
                ? "Audit the attached CC source context. If the task is not clear, ask for the missing user request."
                : _lastUserRequest;
        }

        if (Attachments.Any(attachment => attachment.Kind.Equals("patch", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt))
        {
            return "Review the attached patch context using the ContextControl patch review flow.";
        }

        if (Attachments.Any(attachment => attachment.Kind.Equals("dir", StringComparison.OrdinalIgnoreCase) && attachment.IncludeInPrompt))
        {
            return string.IsNullOrWhiteSpace(_lastUserRequest)
                ? "Identify the smallest useful next CC request from the attached DIR project tree."
                : _lastUserRequest;
        }

        return "";
    }

    private async Task SendCodexChatAsync(string message)
    {
        if (IsBusy)
        {
            return;
        }

        var availability = await _codexHarnessService.CheckAvailabilityAsync();
        ApplyCodexAvailability(availability);
        if (!availability.Available || !availability.IsAuthenticated)
        {
            IsPromptOpen = true;
            PromptModeKey = "codex";
            PhaseTitle = availability.RequiresLogin ? "Codex login required" : "Codex unavailable";
            PhaseDetail = availability.Status;
            ProviderStatus = availability.Status;
            AppendTerminalOutput(availability.Status);
            return;
        }

        var codexCancellation = new CancellationTokenSource();
        _codexCancellation = codexCancellation;
        IsBusy = true;
        IsCodexRequestRunning = true;
        CodexStatus = "Preparing Codex capsule...";
        ChatRequestProgressViewModel? progressItem = null;
        try
        {
            var targetSession = EnsureSelectedChatSession();
            var phase = ResolveCapsulePhase(message);
            var capsuleMessage = phase is ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview
                ? ResolvePatchTaskMessage(message)
                : message;
            if (phase == ContextCapsulePhase.FileRequest && IsMeaningfulTaskPrompt(message))
            {
                _lastUserRequest = message;
            }

            MoveToCcStage(phase switch
            {
                ContextCapsulePhase.FileRequest => CcStageResolve,
                ContextCapsulePhase.SourceAudit or ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview => CcStagePatch,
                _ => CcStageRequest
            });

            var capsuleAttachments = await BuildCapsuleAttachmentsAsync(phase);
            var codexRequest = new CodexHarnessRequest(
                capsuleMessage,
                phase,
                _processService.ContextRoot,
                capsuleAttachments,
                _skillbookService.BuildCodexInstructionText(phase),
                _skillbookService.BuildEnabledInstructionText());
            var diagnosticPrompt = CodexHarnessService.BuildPrompt(codexRequest);
            var attachmentSnapshot = BuildSentAttachmentSnapshot(capsuleAttachments);

            AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
                "user",
                capsuleMessage,
                "Codex CLI",
                FormatCodexPhase(phase),
                BuildCodexCapsuleSummary(diagnosticPrompt, capsuleAttachments),
                attachments: attachmentSnapshot,
                diagnosticPrompt: diagnosticPrompt));
            ConsumeSentAttachments(attachmentSnapshot);
            PromptText = "";
            PhaseTitle = "Codex CC chat";
            PhaseDetail = $"{FormatCodexPhase(phase)} through read-only Codex harness.";
            ProviderStatus = "Codex CLI read-only harness";
            CodexStatus = $"Running {FormatCodexPhase(phase)}...";

            var generationProgress = CreateGenerationProgress(targetSession, "Codex CLI", FormatCodexPhase(phase), isCancellable: true);
            progressItem = generationProgress.Item;
            _codexProgressItem = progressItem;
            var terminal = CreateTerminalProgress();
            terminal.Report($"Sending {FormatCodexPhase(phase)} capsule to Codex CLI...");
            terminal.Report("Codex is instructed to use CC attachments only and avoid repo navigation/actions.");
            ReportCapsuleAttachments(terminal, capsuleAttachments);
            var codexProgress = new Progress<LocalLlmGenerationProgress>(progress =>
            {
                if (!string.IsNullOrWhiteSpace(progress.Status))
                {
                    CodexStatus = progress.Status;
                }

                generationProgress.Progress.Report(progress);
            });

            var result = await _codexHarnessService.SendAsync(
                codexRequest,
                codexProgress,
                terminal,
                codexCancellation.Token);
            var audit = string.IsNullOrWhiteSpace(result.Message)
                ? null
                : CodexPhaseAuditor.Audit(phase, result.Message, _promptBuilder);
            if (audit is not null)
            {
                ReportCodexPhaseAudit(audit);
            }

            var assistantText = BuildCodexAssistantText(result, audit);
            var phaseHandled = false;
            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                var assistant = new LocalLlmChatMessageViewModel(
                    "assistant",
                    assistantText,
                    "Codex CLI",
                    FormatCodexPhase(phase),
                    result.EventTrace);
                AppendChatMessageToSession(targetSession, assistant);
                var latestPatch = assistant.Snippets.LastOrDefault(snippet => snippet.IsPatch);
                if (latestPatch is not null && ReferenceEquals(SelectedChatSession, targetSession))
                {
                    _lastAssistantPatchBlocks = latestPatch.Text;
                }

                if (phase == ContextCapsulePhase.FileRequest)
                {
                    HandleFileRequestAnswer(targetSession, assistant);
                    phaseHandled = true;
                }
                else if (phase == ContextCapsulePhase.PatchWrite && LooksLikeWrongPatchWriteAnswer(assistant))
                {
                    PhaseTitle = "Patch answer missing";
                    PhaseDetail = "Codex answered like DIR/file-request phase even though CC source context was attached.";
                    AppendTerminalOutput("Codex patch write warning: no CC-REPLACE patch blocks were returned.");
                    phaseHandled = true;
                }
            }

            ProviderStatus = result.Status;
            CodexStatus = result.Status;
            if (!result.Succeeded && CodexHarnessService.IsLoginRequiredText(result.Status))
            {
                IsCodexAuthenticated = false;
                IsCodexLoginRequired = true;
                CodexStatus = "Codex login required. Click Login Codex, complete auth, then Refresh Codex.";
            }

            if (!phaseHandled)
            {
                PhaseTitle = result.Succeeded
                    ? audit is { Passed: false }
                        ? "Codex phase mismatch"
                        : audit is { HasWarnings: true }
                            ? "Codex answer needs review"
                            : "Codex answer ready"
                    : CodexHarnessService.IsLoginRequiredText(result.Status) ? "Codex login required"
                    : result.Status.Contains("stopped", StringComparison.OrdinalIgnoreCase) ? "Codex stopped" : "Codex failed";
                PhaseDetail = CodexHarnessService.IsLoginRequiredText(result.Status)
                    ? CodexStatus
                    : audit?.Summary ?? result.Status;
            }

            Log(result.Succeeded ? "ok" : "warn", result.Status);
        }
        catch (OperationCanceledException)
        {
            CodexStatus = "Codex stopped by user.";
            ProviderStatus = CodexStatus;
            PhaseTitle = "Codex stopped";
            PhaseDetail = CodexStatus;
            Log("warn", CodexStatus);
        }
        catch (Exception ex)
        {
            CodexStatus = ex.Message;
            ProviderStatus = ex.Message;
            PhaseTitle = "Codex failed";
            PhaseDetail = ex.Message;
            Log("error", ex.Message);
        }
        finally
        {
            if (progressItem is not null)
            {
                CompleteGenerationProgress(progressItem);
            }

            _codexProgressItem = null;
            _codexCancellation = null;
            codexCancellation.Dispose();
            IsCodexRequestRunning = false;
            IsBusy = false;
        }
    }

    private bool CanCancelCodexRequest(ChatRequestProgressViewModel? item)
    {
        return IsCodexRequestRunning
            && _codexCancellation is { IsCancellationRequested: false }
            && (item is null || ReferenceEquals(item, _codexProgressItem));
    }

    private void CancelCodexRequest(ChatRequestProgressViewModel? item)
    {
        if (!CanCancelCodexRequest(item))
        {
            return;
        }

        CodexStatus = "Stopping Codex...";
        PhaseTitle = "Stopping Codex";
        PhaseDetail = "Cancelling the Codex CLI process.";
        AppendTerminalOutput("Codex cancellation requested.");
        _codexCancellation?.Cancel();
        (CancelCodexRequestCommand as RelayCommand<ChatRequestProgressViewModel>)?.RaiseCanExecuteChanged();
    }

    private async Task RefreshCodexStatusAsync()
    {
        if (IsRefreshingCodexStatus)
        {
            return;
        }

        IsRefreshingCodexStatus = true;
        try
        {
            var result = await _codexHarnessService.CheckAvailabilityAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsRefreshingCodexStatus = false;
                ApplyCodexAvailability(result);
            });
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsRefreshingCodexStatus = false;
                var status = $"Codex CLI status check failed: {ex.Message}";
                CodexStatus = status;
                IsCodexAuthenticated = false;
                IsCodexLoginRequired = true;
                Log("warn", status);
            });
        }
    }

    private void ApplyCodexAvailability(CodexAvailabilityResult result)
    {
        if (IsCodexRequestRunning)
        {
            return;
        }

        CodexStatus = result.Status;
        IsCodexAuthenticated = result.IsAuthenticated;
        IsCodexLoginRequired = result.RequiresLogin || (result.Available && !result.IsAuthenticated);
        Log(result.Available && result.IsAuthenticated ? "ok" : "warn", result.Status);
    }

    private void OpenCodexLogin()
    {
        IsPromptOpen = true;
        PromptModeKey = "codex";
        var result = _codexHarnessService.LaunchInteractiveLogin();
        CodexStatus = result.Status;
        IsCodexAuthenticated = false;
        IsCodexLoginRequired = true;
        PhaseTitle = result.Succeeded ? "Codex login opened" : "Codex login setup";
        PhaseDetail = result.Status;
        ProviderStatus = result.Status;
        AppendTerminalOutput(result.Status);
        Log(result.Succeeded ? "info" : "warn", result.Status);
    }

    private async Task RunCodexDoctorAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsPromptOpen = true;
        PromptModeKey = "terminal";
        PhaseTitle = "Codex doctor";
        PhaseDetail = "Running codex doctor for install/auth/runtime diagnostics.";
        ProviderStatus = "Codex doctor running";
        var terminal = CreateTerminalProgress();
        try
        {
            var result = await _codexHarnessService.RunDoctorAsync(terminal);
            PhaseTitle = result.Succeeded ? "Codex doctor ready" : "Codex doctor issue";
            PhaseDetail = result.Status;
            ProviderStatus = result.Status;
            CodexStatus = result.Status;
            AppendTerminalOutput(result.Status);
            Log(result.Succeeded ? "ok" : "warn", result.Status);
        }
        finally
        {
            IsBusy = false;
            _ = RefreshCodexStatusAsync();
        }
    }

    private async Task SendLocalChatAsync(string message)
    {
        if (!IsAutopilotEnabled)
        {
            await SendRawLocalChatAsync(message);
            return;
        }

        var targetSession = EnsureSelectedChatSession();
        var phase = ResolveCapsulePhase(message);
        var capsuleMessage = phase is ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview
            ? ResolvePatchTaskMessage(message)
            : message;
        if (phase == ContextCapsulePhase.FileRequest && IsMeaningfulTaskPrompt(message))
        {
            _lastUserRequest = message;
        }

        MoveToCcStage(phase switch
        {
            ContextCapsulePhase.FileRequest => CcStageResolve,
            ContextCapsulePhase.SourceAudit or ContextCapsulePhase.PatchWrite or ContextCapsulePhase.PatchReview => CcStagePatch,
            _ => CcStageRequest
        });

        var model = ResolveModelForPhase(phase);
        if (model is not { IsInstalled: true })
        {
            PhaseTitle = "No local model";
            PhaseDetail = "Pull a model from the LLMs tab, then select it for chat.";
            Log("warn", "Local chat cancelled: no installed model selected.");
            return;
        }

        var capsuleAttachments = await BuildCapsuleAttachmentsAsync(phase);
        ContextControlAttachmentViewModel[] imageAttachmentSnapshot = model.IsImageModel
            ? BuildPendingAttachmentSnapshotForKinds("image")
            : [];
        var requestedContextTokens = ResolveRequestedContextTokens(model, phase);
        var capsule = _capsuleBuilder.Build(new ContextCapsuleBuildRequest(
            capsuleMessage,
            phase,
            model.Id,
            model.ComfortableContext,
            requestedContextTokens,
                _skillbookService.BuildEnabledInstructionText(),
                capsuleAttachments));

        var attachmentSnapshot = BuildSentAttachmentSnapshot(capsuleAttachments);
        var displayedAttachmentSnapshot = attachmentSnapshot
            .Concat(imageAttachmentSnapshot)
            .ToArray();
        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "user",
            capsuleMessage,
            model.Id,
            FormatCapsulePhase(phase),
            capsule.Summary,
            attachments: displayedAttachmentSnapshot,
            diagnosticPrompt: capsule.Text));
        ConsumeSentAttachments(attachmentSnapshot);
        ConsumeSentAttachments(imageAttachmentSnapshot);
        PromptText = "";
        PhaseTitle = "Local CC chat";
        PhaseDetail = $"{FormatCapsulePhase(phase)} with {model.DisplayName}; {capsule.Summary}.";
        ProviderStatus = $"Local Ollama: {model.Id}";
        var generationProgress = CreateGenerationProgress(targetSession, model.DisplayName, FormatCapsulePhase(phase));
        var terminal = CreateTerminalProgress();
        try
        {
            terminal.Report($"Sending {FormatCapsulePhase(phase)} capsule to {model.DisplayName} ({model.Id})...");
            terminal.Report($"Requested Ollama context window: {capsule.RequestedContextTokens:N0} tokens.");
            ReportCapsuleAttachments(terminal, capsuleAttachments);
            foreach (var imageAttachment in imageAttachmentSnapshot)
            {
                terminal.Report($"image: {imageAttachment.Path}");
            }

            var result = await _localLlmService.SendChatAsync(
                new LocalLlmRequest(
                    model.Id,
                    capsule.Text,
                    FormatCapsulePhase(phase),
                    displayedAttachmentSnapshot.Select(attachment => attachment.DisplayTitle).ToArray(),
                    capsule.RequestedContextTokens,
                    imageAttachmentSnapshot.Select(attachment => attachment.Path).ToArray()),
                generationProgress.Progress,
                terminal);

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
            {
                var assistant = new LocalLlmChatMessageViewModel(
                    "assistant",
                    result.Message,
                    model.Id,
                    FormatCapsulePhase(phase),
                    capsule.Summary,
                    result.Stats);
                AppendChatMessageToSession(targetSession, assistant);
                var latestPatch = assistant.Snippets.LastOrDefault(snippet => snippet.IsPatch);
                if (latestPatch is not null && ReferenceEquals(SelectedChatSession, targetSession))
                {
                    _lastAssistantPatchBlocks = latestPatch.Text;
                }

                if (phase == ContextCapsulePhase.FileRequest)
                {
                    HandleFileRequestAnswer(targetSession, assistant);
                }
                else if (phase == ContextCapsulePhase.PatchWrite && LooksLikeWrongPatchWriteAnswer(assistant))
                {
                    PhaseTitle = "Patch answer missing";
                    PhaseDetail = "The model answered like DIR/file-request phase even though CC source context was attached.";
                    AppendTerminalOutput("Patch write warning: model returned DIR/FIND/request-list output instead of CC-REPLACE patch blocks.");
                }

                if (assistant.HasThinking)
                {
                    model.MarkThinkingDetected();
                }
            }

            ProviderStatus = result.Status;
            PhaseTitle = result.Succeeded ? "Local answer ready" : "Local chat failed";
            PhaseDetail = result.Status;
            Log(result.Succeeded ? "ok" : "warn", result.Status);
        }
        catch (Exception ex)
        {
            ProviderStatus = ex.Message;
            PhaseTitle = "Local chat failed";
            PhaseDetail = ex.Message;
            Log("error", ex.Message);
        }
        finally
        {
            CompleteGenerationProgress(generationProgress.Item);
        }
    }

    private async Task SendRawLocalChatAsync(string message)
    {
        var targetSession = EnsureSelectedChatSession();
        var model = ResolveModelForPhase(ContextCapsulePhase.Chat);
        if (model is not { IsInstalled: true })
        {
            PhaseTitle = "No local model";
            PhaseDetail = "Pull a model from the LLMs tab, then select it for chat.";
            Log("warn", "Raw chat cancelled: no installed model selected.");
            return;
        }

        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "user",
            message,
            model.Id,
            "raw",
            "clean chat"));
        PromptText = "";
        MoveToCcStage(CcStageRequest);
        PhaseTitle = "Raw chat";
        PhaseDetail = $"Sending clean chat to {model.DisplayName}.";
        ProviderStatus = $"Local Ollama: {model.Id}";

        var requestedContextTokens = ResolveRequestedContextTokens(model, ContextCapsulePhase.Chat);
        var generationProgress = CreateGenerationProgress(targetSession, model.DisplayName, "raw");
        var terminal = CreateTerminalProgress();
        try
        {
            terminal.Report($"Sending raw prompt to {model.DisplayName} ({model.Id})...");
            terminal.Report("No ContextControl capsule, attachments, skillbook, or workflow instructions included.");
            terminal.Report($"Requested Ollama context window: {requestedContextTokens:N0} tokens.");

            var result = await _localLlmService.SendChatAsync(
                new LocalLlmRequest(
                    model.Id,
                    message,
                    "raw",
                    [],
                    requestedContextTokens),
                generationProgress.Progress,
                terminal);

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
            {
                var assistant = new LocalLlmChatMessageViewModel(
                    "assistant",
                    result.Message,
                    model.Id,
                    "raw",
                    "clean chat",
                    result.Stats);
                AppendChatMessageToSession(targetSession, assistant);
                if (assistant.HasThinking)
                {
                    model.MarkThinkingDetected();
                }
            }

            ProviderStatus = result.Status;
            PhaseTitle = result.Succeeded ? "Raw answer ready" : "Raw chat failed";
            PhaseDetail = result.Status;
            Log(result.Succeeded ? "ok" : "warn", result.Status);
        }
        catch (Exception ex)
        {
            ProviderStatus = ex.Message;
            PhaseTitle = "Raw chat failed";
            PhaseDetail = ex.Message;
            Log("error", ex.Message);
        }
        finally
        {
            CompleteGenerationProgress(generationProgress.Item);
        }
    }

    private async Task SendImageGenerationAsync(string prompt)
    {
        if (!IsImageGenConversationKind(_activeConversationKind))
        {
            SwitchConversationKindForWorkspace();
        }

        var targetSession = EnsureSelectedChatSession();
        var capturedConversationKind = ImageGenConversationKind;
        var capturedScopeKey = _chatHistoryScopeKey;
        var model = SelectedImageGenerationModel;
        if (model is null || (!model.IsInstalled && !model.CanUseManualBackend))
        {
            PhaseTitle = "No image gen model";
            PhaseDetail = "Install or ready the required image generation backend, then select a model in Image Gen.";
            Log("warn", "Image generation cancelled: no ready image generation model selected.");
            return;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            PhaseTitle = "No image prompt";
            PhaseDetail = "Describe the image you want to generate.";
            Log("warn", "Image generation cancelled: empty prompt.");
            return;
        }

        AppendChatMessageToSession(targetSession, new LocalLlmChatMessageViewModel(
            "user",
            prompt,
            model.Id,
            "image gen",
            "prompt-only image generation"));
        PromptText = "";
        PhaseTitle = "Image Gen";
        PhaseDetail = $"Generating with {model.DisplayName}.";
        ProviderStatus = model.CanUseManualBackend
            ? $"{model.BackendRequirementLabel} image gen: {model.Id}"
            : $"Local Ollama image gen: {model.Id}";
        var generationProgress = CreateGenerationProgress(targetSession, model.DisplayName, "image gen");
        var terminal = CreateTerminalProgress();
        var outputDirectory = "";
        var generationStartedUtc = DateTime.UtcNow.AddSeconds(-5);
        try
        {
            outputDirectory = ResolveImageGenerationOutputDirectory();
            terminal.Report($"Generating image with {model.DisplayName} ({model.Id})...");
            if (model.UsesHuggingFaceHubDownload && !HasHuggingFaceToken)
            {
                terminal.Report(model.HuggingFaceTokenWarning);
            }

            var result = await _localLlmService.GenerateImageAsync(
                model.Id,
                prompt,
                outputDirectory,
                generationProgress.Progress,
                terminal);
            result = IncludeFallbackDetectedImages(result, outputDirectory, generationStartedUtc, model.Id);

            var hasGeneratedImages = result.ImagePaths.Count > 0;
            var generatedAttachments = result.ImagePaths
                .Select(path => new ContextControlAttachmentViewModel(Path.GetFileName(path), path, "image")
                {
                    IncludeInPrompt = false
                })
                .ToArray();
            var responseText = BuildImageGenerationChatText(result);
            var assistant = new LocalLlmChatMessageViewModel(
                "assistant",
                responseText,
                model.Id,
                result.Succeeded ? "image gen" : hasGeneratedImages ? "image gen warning" : "image gen failed",
                hasGeneratedImages
                    ? $"{result.ImagePaths.Count:N0} generated image(s)"
                    : "no generated image detected",
                attachments: generatedAttachments);
            await AppendChatMessageToCapturedConversationAsync(capturedScopeKey, capturedConversationKind, targetSession, assistant);

            if (hasGeneratedImages)
            {
                LastExportPath = result.ImagePaths.First();
            }

            ProviderStatus = result.Status;
            PhaseTitle = hasGeneratedImages || result.Succeeded ? "Image ready" : "Image generation failed";
            PhaseDetail = result.Status;
            Log(hasGeneratedImages || result.Succeeded ? "ok" : "warn", result.Status);
        }
        catch (Exception ex)
        {
            var fallbackImages = FindRecentGeneratedImageFiles(outputDirectory, generationStartedUtc);
            if (fallbackImages.Count > 0)
            {
                var result = new LocalLlmImageGenerationResult(
                    true,
                    $"Image generated, but the response update hit an error: {ex.Message}",
                    fallbackImages,
                    outputDirectory);
                var assistant = new LocalLlmChatMessageViewModel(
                    "assistant",
                    BuildImageGenerationChatText(result),
                    model.Id,
                    "image gen warning",
                    $"{fallbackImages.Count:N0} generated image(s)",
                    attachments: fallbackImages
                        .Select(path => new ContextControlAttachmentViewModel(Path.GetFileName(path), path, "image")
                        {
                            IncludeInPrompt = false
                        })
                        .ToArray());
                await AppendChatMessageToCapturedConversationAsync(capturedScopeKey, capturedConversationKind, targetSession, assistant);
                LastExportPath = fallbackImages.First();
            }

            ProviderStatus = ex.Message;
            PhaseTitle = "Image generation failed";
            PhaseDetail = ex.Message;
            Log("error", ex.Message);
        }
        finally
        {
            CompleteGenerationProgress(generationProgress.Item);
        }
    }

    private static LocalLlmImageGenerationResult IncludeFallbackDetectedImages(
        LocalLlmImageGenerationResult result,
        string outputDirectory,
        DateTime generationStartedUtc,
        string modelId)
    {
        if (result.ImagePaths.Count > 0)
        {
            return result;
        }

        var fallbackImages = FindRecentGeneratedImageFiles(outputDirectory, generationStartedUtc);
        return fallbackImages.Count == 0
            ? result
            : result with
            {
                Succeeded = true,
                Status = $"Generated {fallbackImages.Count:N0} image(s) with {modelId}.",
                ImagePaths = fallbackImages
            };
    }

    private static IReadOnlyList<string> FindRecentGeneratedImageFiles(string outputDirectory, DateTime generationStartedUtc)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(outputDirectory)
                .Select(path => new FileInfo(path))
                .Where(file => ImageAttachmentExtensions.Contains(file.Extension)
                    && file.LastWriteTimeUtc >= generationStartedUtc)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(4)
                .Select(file => file.FullName)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private string ResolveImageGenerationOutputDirectory()
    {
        var root = string.IsNullOrWhiteSpace(_processService.ContextRoot)
            ? AppContext.BaseDirectory
            : _processService.ContextRoot;
        var directory = Path.Combine(root, "image-gen");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string BuildImageGenerationChatText(LocalLlmImageGenerationResult result)
    {
        var builder = new StringBuilder();
        if (result.ImagePaths.Count > 0)
        {
            if (result.Succeeded)
            {
                builder.AppendLine(result.Status);
            }
            else
            {
                builder.AppendLine("Image generated, but the backend reported a warning.");
                builder.AppendLine(result.Status);
            }

            builder.AppendLine(result.ImagePaths.Count == 1
                ? "Generated image preview attached."
                : "Generated image previews attached.");
        }
        else
        {
            builder.AppendLine(result.Succeeded
                ? result.Status
                : $"Image generation failed: {result.Status}");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool LooksLikeWrongPatchWriteAnswer(LocalLlmChatMessageViewModel assistant)
    {
        if (assistant.Snippets.Any(snippet => snippet.IsPatch))
        {
            return false;
        }

        if (assistant.Snippets.Any(snippet => snippet.IsRequestList))
        {
            return true;
        }

        var text = (assistant.Text ?? "").Trim();
        return text.StartsWith("DIR", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)
            || text.Equals("END", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCodexAssistantText(CodexHarnessResult result, CodexPhaseAuditResult? audit)
    {
        var builder = new StringBuilder();
        var thinking = BuildCodexThinkingText(result, audit);
        if (!string.IsNullOrWhiteSpace(thinking))
        {
            builder.AppendLine("<think>");
            builder.AppendLine(thinking.Trim());
            builder.AppendLine("</think>");
            builder.AppendLine();
        }

        builder.AppendLine(!string.IsNullOrWhiteSpace(result.Message)
            ? result.Message.Trim()
            : result.Status);
        return builder.ToString().TrimEnd();
    }

    private static string BuildCodexThinkingText(CodexHarnessResult result, CodexPhaseAuditResult? audit)
    {
        var parts = new List<string>();
        if (audit is not null)
        {
            parts.Add(audit.ToDiagnosticText());
        }

        if (!string.IsNullOrWhiteSpace(result.Thinking))
        {
            parts.Add(result.Thinking.Trim());
        }

        if (!string.IsNullOrWhiteSpace(result.EventTrace))
        {
            parts.Add("Codex event trace:" + Environment.NewLine + result.EventTrace.Trim());
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private void ReportCodexPhaseAudit(CodexPhaseAuditResult audit)
    {
        AppendTerminalOutput(audit.Summary);
        foreach (var detail in audit.Details.Take(6))
        {
            AppendTerminalOutput($"  - {detail}");
        }
    }

    private static string FormatCodexPhase(ContextCapsulePhase phase)
    {
        return $"codex {FormatCapsulePhase(phase)}";
    }

    private static string BuildCodexCapsuleSummary(string diagnosticPrompt, IReadOnlyList<ContextCapsuleAttachment> attachments)
    {
        var promptTokens = ContextCapsuleBuilder.EstimateTokens(diagnosticPrompt);
        var attachmentTokens = attachments
            .Where(attachment => attachment.Included)
            .Sum(attachment => ContextCapsuleBuilder.EstimateTokens(attachment.Text));
        return $"{promptTokens:N0} prompt tok; {attachmentTokens:N0} attachment tok; read-only harness";
    }

    private void HandleFileRequestAnswer(ChatSessionViewModel targetSession, LocalLlmChatMessageViewModel assistant)
    {
        var requestSnippet = assistant.Snippets.LastOrDefault(snippet => snippet.IsRequestList);
        if (requestSnippet is null)
        {
            PhaseTitle = "No CC lines";
            PhaseDetail = "The model did not return file/FUNCTION/FIND lines. Refine the request or paste exact lines from DIR, then press CC.";
            AppendTerminalOutput("File request stopped: model returned no usable CC request lines.");
            return;
        }

        if (!ReferenceEquals(SelectedChatSession, targetSession))
        {
            return;
        }

        PromptText = EnsureEndsWithEnd(requestSnippet.Text);
        IsPromptOpen = true;
        PromptModeKey = "context";
        PhaseTitle = IsFindOnlyRequestList(requestSnippet.Text) ? "Discovery request ready" : "CC request ready";
        PhaseDetail = IsFindOnlyRequestList(requestSnippet.Text)
            ? "Review the FIND lines, then press CC. FIND returns candidate files only, not source bodies."
            : "Review the file/FUNCTION lines, then press CC to export source.";
        AppendTerminalOutput(IsFindOnlyRequestList(requestSnippet.Text)
            ? "File request ready: FIND discovery lines loaded into the selected chat prompt."
            : "File request ready: exact CC request lines loaded into the selected chat prompt.");
    }

    private static void ReportCapsuleAttachments(IProgress<string> terminal, IReadOnlyList<ContextCapsuleAttachment> attachments)
    {
        var included = attachments.Where(attachment => attachment.Included).ToArray();
        if (included.Length == 0)
        {
            terminal.Report("No raw attachments included in this capsule.");
            return;
        }

        foreach (var attachment in included)
        {
            var text = attachment.Text ?? "";
            terminal.Report($"Raw attachment included: {attachment.Label} ({attachment.Kind}, {text.Length:N0} chars, ~{ContextCapsuleBuilder.EstimateTokens(text):N0} tok)");
        }
    }

}
