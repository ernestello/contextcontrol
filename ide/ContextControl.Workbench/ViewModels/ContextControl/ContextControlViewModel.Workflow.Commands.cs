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

            var pendingTask = PromptText.Trim();
            if (IsMeaningfulTaskPrompt(pendingTask))
            {
                _lastUserRequest = pendingTask;
            }

            _semanticIndex = null;

            RemoveAttachmentsByKind("code", "patch");
            _lastAssistantPatchBlocks = "";
            IsPatchPlanReady = false;
            PatchSummary = "No patch loaded.";
            UpdatePatchPlanActions(null);
            AddAttachment(Path.GetFileName(_processService.DirectoryExportPath), _processService.DirectoryExportPath, "dir");
            LastExportPath = _processService.DirectoryExportPath;
            PromptText = StripLegacyDirPayload(PromptText);
            PhaseTitle = "DIR ready";
            PhaseDetail = IsAutopilotEnabled
                ? "Bus stop 2: project tree attached. Press Send so the model returns exact CC file/FUNCTION/FIND lines."
                : "Project tree attached. Press Send to ask the model for CC request lines, or paste your own lines and press CC.";
            AppendTerminalOutput($"DIR export ready: {Path.GetFileName(_processService.DirectoryExportPath)} attached.");
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

            if (requestLines.All(line => line.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)))
            {
                var discoveryText = await _processService.ReadOutputFileAsync(_processService.CodeExportPath);
                var matchedFiles = ExtractMatchedFileRequestsFromFindExport(discoveryText);
                RemoveAttachmentsByKind("dir", "code", "patch");
                LastExportPath = _processService.CodeExportPath;
                IsPatchPlanReady = false;
                UpdatePatchPlanActions(null);

                if (matchedFiles.Count == 0)
                {
                    PromptText = "";
                    PhaseTitle = "FIND found nothing";
                    PhaseDetail = "Discovery exported no matching source files. Try a narrower exact term from the DIR tree.";
                    AppendTerminalOutput("CC FIND discovery complete: no matching code files found.");
                    AppendChatMessage(new LocalLlmChatMessageViewModel(
                        "assistant",
                        "FIND discovery returned no matching code files. Run DIR again or try a more exact FIND: term from the project tree.",
                        "ContextControl",
                        "CC discovery"));
                    return;
                }

                PromptText = EnsureEndsWithEnd(string.Join(Environment.NewLine, matchedFiles));
                PhaseTitle = "FIND matched files";
                PhaseDetail = $"Discovery found {matchedFiles.Count:N0} candidate file(s). Review the prompt lines, then press CC again to export source.";
                AppendTerminalOutput($"CC FIND discovery complete: loaded {matchedFiles.Count:N0} exact file request line(s) into the prompt.");
                AppendChatMessage(new LocalLlmChatMessageViewModel(
                    "assistant",
                    BuildFindDiscoveryChatText(matchedFiles),
                    "ContextControl",
                    "CC discovery"));
                return;
            }

            RemoveAttachmentsByKind("dir", "patch");
            IsPatchPlanReady = false;
            UpdatePatchPlanActions(null);
            AddAttachment(Path.GetFileName(_processService.CodeExportPath), _processService.CodeExportPath, "code");
            LastExportPath = _processService.CodeExportPath;
            PromptText = string.IsNullOrWhiteSpace(_lastUserRequest)
                ? ""
                : _lastUserRequest;
            PhaseTitle = "Context ready";
            PhaseDetail = IsAutopilotEnabled
                ? "Bus stop 4: source context attached. Press Send for source audit; ask explicitly for a patch only when needed."
                : "Source context attached. Press Send for audit/analysis, or ask explicitly for CC-REPLACE patch blocks.";
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

            var shapeError = _promptBuilder.ValidatePatchBlocks(patchText);
            if (!string.IsNullOrWhiteSpace(shapeError))
            {
                PhaseTitle = "GO patch shape";
                PhaseDetail = shapeError;
                Log("warn", $"GO preview cancelled: {shapeError}");
                AppendTerminalOutput($"GO cancelled: {shapeError}");
                UpdatePatchPlanActions(null);
                IsPatchPlanReady = false;
                PatchSummary = shapeError;
                AppendChatMessage(new LocalLlmChatMessageViewModel(
                    "assistant",
                    BuildPatchShapeFailureChatText(shapeError),
                    "ContextControl",
                    "GO preview"));
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
            var failureDetail = BuildPatchFailureDetail(result, summary);
            IsPatchPlanReady = planReady;
            PatchSummary = planReady ? summary.CompactLabel : failureDetail;
            UpdatePatchPlanActions(planReady ? summary : null);
            PhaseTitle = planReady ? "Patch planned" : "Patch preview failed";
            PhaseDetail = planReady
                ? "Review the plan, then apply effective edits from the dock."
                : failureDetail;
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

}
