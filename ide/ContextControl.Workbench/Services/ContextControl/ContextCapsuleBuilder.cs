// CC-DESC: Builds compact CC-native prompts for local LLM workflow phases.

using System.Text;

namespace ContextControl.Workbench.Services;

public enum ContextCapsulePhase
{
    Chat,
    FileRequest,
    SourceAudit,
    PatchWrite,
    PatchReview
}

public sealed record ContextCapsuleAttachment(
    string Label,
    string Kind,
    string Path,
    string Text,
    bool Included);

public sealed record ContextCapsuleBuildRequest(
    string UserMessage,
    ContextCapsulePhase Phase,
    string ModelId,
    string ModelContextLabel,
    int TargetContextTokens,
    string SkillbookInstructions,
    IReadOnlyList<ContextCapsuleAttachment> Attachments);

public sealed record ContextCapsule(
    string Text,
    ContextCapsulePhase Phase,
    int EstimatedInputTokens,
    int EstimatedAttachmentTokens,
    int ComfortableContextTokens,
    int RequestedContextTokens,
    int OutputReserveTokens,
    double ContextPressurePercent,
    string Summary);

public sealed class ContextCapsuleBuilder
{
    public const int DefaultComfortableContextTokens = 4096;
    public const int DefaultOutputReserveTokens = 900;
    public const int MaxAttachmentCharacters = 28_000;
    public const int MaxExpandedAttachmentCharacters = 512_000;
    public const string AttachmentClipMarker = "[attachment clipped by local context budget]";

    public ContextCapsule Build(ContextCapsuleBuildRequest request)
    {
        var comfortableTokens = EstimateContextTokens(request.ModelContextLabel, DefaultComfortableContextTokens);
        var requestedTokens = Math.Max(comfortableTokens, request.TargetContextTokens);
        var outputReserve = DefaultOutputReserveTokens;
        var builder = new StringBuilder();

        builder.AppendLine("ContextControl local LLM capsule");
        builder.AppendLine();
        builder.AppendLine($"Model: {request.ModelId}");
        builder.AppendLine($"Phase: {FormatPhase(request.Phase)}");
        builder.AppendLine($"Comfortable context target: {comfortableTokens} tokens");
        builder.AppendLine($"Requested Ollama context: {requestedTokens} tokens");
        builder.AppendLine();
        builder.AppendLine("Core rule: the visible project context is included below as attachment text.");
        builder.AppendLine("Use that text directly. You cannot access anything outside this capsule or run tools.");
        builder.AppendLine("This is an authorized local project editing workflow; do not give a generic refusal when the requested edit can be answered from visible context.");
        builder.AppendLine();
        builder.AppendLine(BuildPhaseContract(request.Phase));
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.SkillbookInstructions))
        {
            builder.AppendLine("Enabled Skillbook entries:");
            builder.AppendLine(request.SkillbookInstructions.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("User request:");
        builder.AppendLine(string.IsNullOrWhiteSpace(request.UserMessage) ? "(empty)" : request.UserMessage.Trim());
        builder.AppendLine();

        var attachmentTokens = 0;
        var remainingAttachmentChars = EstimateAttachmentCharacterLimit(requestedTokens, outputReserve);
        var included = request.Attachments.Where(attachment => attachment.Included).ToArray();
        if (included.Length > 0)
        {
            builder.AppendLine("Attachment inventory:");
            foreach (var attachment in included)
            {
                var text = attachment.Text ?? "";
                builder.AppendLine($"- {attachment.Label} ({attachment.Kind}) PATH: {attachment.Path}; BODY_CHARS: {text.Length}; EST_TOKENS: {EstimateTokens(text)}");
            }

            builder.AppendLine();
            builder.AppendLine("Included ContextControl attachments:");
            builder.AppendLine("The attachment bodies below are the actual visible context. Do not claim an attachment is empty when text appears between its markers.");
            foreach (var attachment in included)
            {
                var text = attachment.Text ?? "";
                var clipped = text;
                if (remainingAttachmentChars <= 0)
                {
                    clipped = "";
                }
                else if (clipped.Length > remainingAttachmentChars)
                {
                    clipped = clipped[..remainingAttachmentChars] + Environment.NewLine + AttachmentClipMarker;
                }

                remainingAttachmentChars -= Math.Max(0, clipped.Length);
                attachmentTokens += EstimateTokens(clipped);

                builder.AppendLine($"--- ATTACHMENT {attachment.Kind}: {attachment.Label}");
                builder.AppendLine($"PATH: {attachment.Path}");
                builder.AppendLine(clipped.TrimEnd());
                builder.AppendLine($"--- END ATTACHMENT {attachment.Label}");
                builder.AppendLine();
            }
        }

        var textOut = builder.ToString().TrimEnd();
        var inputTokens = EstimateTokens(textOut);
        var pressure = requestedTokens <= 0
            ? 0
            : Math.Clamp(inputTokens * 100d / Math.Max(1, requestedTokens - outputReserve), 0, 999);
        var summary = requestedTokens > comfortableTokens
            ? $"{inputTokens:N0} in tok; {attachmentTokens:N0} attachment tok; {outputReserve:N0} reserve; {pressure:0.#}% of requested ctx; comfy {comfortableTokens:N0}"
            : $"{inputTokens:N0} in tok; {attachmentTokens:N0} attachment tok; {outputReserve:N0} reserve; {pressure:0.#}% of comfortable context";

        return new ContextCapsule(
            textOut,
            request.Phase,
            inputTokens,
            attachmentTokens,
            comfortableTokens,
            requestedTokens,
            outputReserve,
            pressure,
            summary);
    }

    public static int EstimateTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4d);
    }

    public static int EstimateContextTokens(string? contextLabel, int fallbackTokens = DefaultComfortableContextTokens)
    {
        var label = contextLabel ?? "";
        var digits = new StringBuilder();
        for (var index = 0; index < label.Length; index++)
        {
            var ch = label[index];
            if (char.IsDigit(ch))
            {
                digits.Append(ch);
                continue;
            }

            if (digits.Length > 0)
            {
                var unit = ch;
                if (char.ToUpperInvariant(unit) == 'K'
                    && int.TryParse(digits.ToString(), out var thousands)
                    && thousands > 0)
                {
                    return thousands * 1024;
                }

                if (char.ToUpperInvariant(unit) == 'M'
                    && int.TryParse(digits.ToString(), out var millions)
                    && millions > 0)
                {
                    return millions * 1024 * 1024;
                }

                digits.Clear();
            }
        }

        return fallbackTokens;
    }

    public static int EstimateAttachmentCharacterLimit(int contextTokens, int outputReserveTokens = DefaultOutputReserveTokens)
    {
        var usableTokens = Math.Max(DefaultComfortableContextTokens - outputReserveTokens, contextTokens - outputReserveTokens);
        var characters = Math.Max(MaxAttachmentCharacters, usableTokens * 4);
        return Math.Min(MaxExpandedAttachmentCharacters, characters);
    }

    private static string BuildPhaseContract(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => """
                Phase contract:
                Bus stop: DIR -> CC request selection.
                DIR project-tree context is attached. Do not solve the user task yet.
                Output only the smallest safe CC request list.
                Format:
                Research scope: one short line
                ide/ContextControl.Workbench/Views/MainWindow.axaml
                FUNCTION ide/ContextControl.Workbench/ViewModels/ContextControlViewModel.cs :: SymbolName
                END
                Valid lines: exact file, FUNCTION path :: symbol, FIND: exactText.
                Do not output PATH:, FILE:, DIR:, labels, absolute paths, attachment paths, or placeholder lines.
                Every exact file path must be copied from the attached DIR tree exactly.
                If the user's named path is absent from the DIR tree, treat it as a hint and return real nearby tree paths or FIND: terms.
                Never invent src/, .xaml.cs, .csproj, or framework-style paths that are not present in the tree.
                Ignore diagnostic questions about whether attachments were received; the inventory above is authoritative, and your output must still be a useful CC request list.
                FIND terms must come from the user's real task, not from capsule headings such as local LLM capsule, attachment, project tree, or context.
                FIND is discovery only: it lists candidate files and occurrence previews, but it never exports source bodies. After FIND results, ask for exact file paths or FUNCTION lines before writing a patch.
                Never return END by itself. If unsure, output 2-5 FIND: terms from the user request, then END.
                """,
            ContextCapsulePhase.SourceAudit => """
                Phase contract:
                Bus stop: CC source audit / evidence review.
                CC source/function context is attached. Do not patch unless the user explicitly asks for code changes.
                Analyze only the visible source context and the user's task.
                Treat complex work as gated stages. Do not advance a later stage until the current stage has a mini-report.
                For each candidate or finding, use this structure:
                Candidate ID:
                Title:
                Affected files/functions:
                External or user-controlled input:
                Trust boundary:
                Normal operation path:
                Invariant or requirement violated:
                Exact code evidence:
                Impact:
                Extra files/functions needed:
                Confidence:
                Disprove conditions:
                If context is insufficient, request only exact files, FUNCTION path :: symbol, or FIND: exactText lines, ending with END.
                Keep claims conservative. Separate proven primitive from reachability whenever the impact depends on a later path.
                """,
            ContextCapsulePhase.PatchWrite => """
                Phase contract:
                Bus stop: CC patch authoring.
                CC source/function context is attached.
                You are NOT in DIR/file-request phase. File selection is already complete.
                Do not reply with DIR, FIND, END, or a file request list.
                Answer from the provided source, or emit raw CC-REPLACE blocks only.
                Every CC-REPLACE block must include FILE, MODE, and usually the --- separator.
                Never put a bare path directly after BEGIN CC-REPLACE.
                Use paths relative to the project root, copied from the CC source export header.
                Required patch shape:
                BEGIN CC-REPLACE
                FILE: path/relative/to/project
                MODE: function|replace_region|whole_file|append_to_file
                NAME: required_for_function_or_replace_region
                ---
                replacement text
                END CC-REPLACE
                For MODE: insert_include, use HEADER: <...> or HEADER: "..." and omit the body/separator.
                Do not invent APIs not shown in context.
                If the visible CC source context is insufficient, reply with NEED_MORE_CONTEXT followed by valid CC request lines and END.
                """,
            ContextCapsulePhase.PatchReview => """
                Phase contract:
                Bus stop: patch review / repair.
                Patch context is attached.
                Review or repair it using only visible context.
                If repaired, emit complete CC-REPLACE blocks.
                """,
            _ => """
                Phase contract:
                Normal local chat. Ask for DIR/CC context when code evidence is needed.
                """
        };
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

    private static int EstimateComfortableContextTokens(string contextLabel) => EstimateContextTokens(contextLabel);
}
