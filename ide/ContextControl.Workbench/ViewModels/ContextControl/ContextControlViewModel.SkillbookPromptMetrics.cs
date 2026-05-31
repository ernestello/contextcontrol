// CC-DESC: Skillbook and prompt tokenomics bindable metrics.

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
    public string SkillbookSummary
    {
        get
        {
            var codex = SkillbookEntries.Count(entry => entry.Source.Equals("codex", StringComparison.OrdinalIgnoreCase));
            var skillflow = SkillbookEntries.Count(entry => entry.Source.Equals("skillflow", StringComparison.OrdinalIgnoreCase));
            return $"{SkillbookEntries.Count} instruction(s); {codex} Codex, {skillflow} Skillflow; project overrides global entries";
        }
    }

    public string SkillbookCodexSummary =>
        $"{SkillbookEntries.Count(entry => entry.Source.Equals("codex", StringComparison.OrdinalIgnoreCase)):N0} harness instruction(s)";

    public string SkillbookSkillflowSummary =>
        $"{SkillbookEntries.Count(entry => entry.Source.Equals("skillflow", StringComparison.OrdinalIgnoreCase)):N0} development phase(s)";

    public string SkillbookUserSummary
    {
        get
        {
            var project = SkillbookEntries.Count(entry => entry.Source.Equals("project", StringComparison.OrdinalIgnoreCase));
            var global = SkillbookEntries.Count(entry => entry.Source.Equals("global", StringComparison.OrdinalIgnoreCase));
            return $"{project:N0} project / {global:N0} global instruction(s)";
        }
    }

    public string SkillbookProjectPath => _skillbookService.ProjectRoot;

    public string SkillbookGlobalPath => _skillbookService.GlobalRoot;

    public string PromptTokenomicsLabel
    {
        get
        {
            if (IsImageGenWorkspaceActive)
            {
                var promptTokensOnly = ContextCapsuleBuilder.EstimateTokens(PromptText);
                return $"{promptTokensOnly:N0} prompt tok; no CC context";
            }

            if (IsCodexPromptMode)
            {
                var codexPromptTokens = ContextCapsuleBuilder.EstimateTokens(PromptText);
                var codexAttachments = EstimateAttachmentBudget();
                return codexAttachments.IsClipped
                    ? $"{codexPromptTokens:N0} prompt tok; {codexAttachments.SentTokens:N0} sent attachment tok ({codexAttachments.FullTokens:N0} full); Codex"
                    : $"{codexPromptTokens:N0} prompt tok; {codexAttachments.SentTokens:N0} attachment tok; Codex";
            }

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
            if (IsImageGenWorkspaceActive)
            {
                var imageGenerationModelLabel = SelectedImageGenerationModel?.DisplayName ?? "No image gen model";
                return $"{imageGenerationModelLabel}: prompt-only image generation";
            }

            if (IsCodexPromptMode)
            {
                var codexAttachmentBudget = EstimateAttachmentBudget();
                var codexTotal = ContextCapsuleBuilder.EstimateTokens(PromptText)
                    + codexAttachmentBudget.SentTokens
                    + ContextCapsuleBuilder.EstimateTokens(_skillbookService.BuildCodexInstructionText(ResolveCapsulePhase(PromptText)));
                var codexSuffix = codexAttachmentBudget.IsClipped ? "; clipped" : "";
                return $"Codex CLI: {codexTotal:N0} tok est; read-only harness; no repo browsing{codexSuffix}";
            }

            var phase = ResolveCapsulePhase(PromptText);
            var model = ResolveModelForPhase(phase) ?? SelectedLocalModel;
            var comfortableBudget = EstimateComfortableBudget(model?.ComfortableContext);
            var requestedBudget = ResolveRequestedContextTokens(model, phase);
            var attachmentBudget = EstimateAttachmentBudget();
            var total = ContextCapsuleBuilder.EstimateTokens(PromptText)
                + attachmentBudget.SentTokens
                + ContextCapsuleBuilder.DefaultOutputReserveTokens;
            var requestedPressure = requestedBudget <= 0 ? 0 : total * 100d / requestedBudget;
            var comfortablePressure = comfortableBudget <= 0 ? 0 : total * 100d / comfortableBudget;
            var modelLabel = model?.DisplayName ?? SelectedLocalModelLabel;
            var suffix = attachmentBudget.IsClipped ? "; clipped" : "";
            return requestedBudget > comfortableBudget
                ? $"{modelLabel}: {total:N0}/{requestedBudget:N0} tok send ({requestedPressure:0.#}% req; {comfortablePressure:0.#}% comfy{suffix})"
                : $"{modelLabel}: {total:N0}/{comfortableBudget:N0} tok est ({comfortablePressure:0.#}%{suffix})";
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
        private set
        {
            if (SetProperty(ref _hardwareSummary, value))
            {
                OnPropertyChanged(nameof(LlmCompactInfoLabel));
            }
        }
    }

    public string LocalLlmStatus
    {
        get => _localLlmStatus;
        private set
        {
            if (SetProperty(ref _localLlmStatus, value))
            {
                OnPropertyChanged(nameof(LlmCompactInfoLabel));
                UpdateOllamaBackendDependency();
            }
        }
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

    public string AttachmentSummary
    {
        get
        {
            if (Attachments.Count == 0)
            {
                return "No pending attachments";
            }

            return $"{Attachments.Count} pending attachment(s)";
        }
    }

    public string PromptFooterSummary => IsLargePrompt
        ? $"{AttachmentSummary} - large prompt mode - {PromptTokenomicsLabel}"
        : IsImageGenWorkspaceActive
        ? $"{AttachmentSummary} - {SelectedLocalModelLabel}"
        : IsCodexPromptMode
        ? $"{AttachmentSummary} - Codex CC flow - {PromptTokenomicsLabel}"
        : $"{AttachmentSummary} - {PromptTokenomicsLabel}";

    public string ContextRootPath => _processService.ContextRoot;

    public string ContextRootLabel => string.IsNullOrWhiteSpace(_processService.ContextRoot)
        ? "context root not resolved"
        : _processService.ContextRoot;

    public string ActiveProjectRoot
    {
        get => _activeProjectRoot;
        set => SetActiveProject(value, ActiveProjectRulesPath);
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
        set => SetActiveProject(ActiveProjectRoot, value);
    }

    public string ActiveProjectRulesPathLabel => string.IsNullOrWhiteSpace(ActiveProjectRulesPath)
        ? "project rules not resolved"
        : ActiveProjectRulesPath;

}
