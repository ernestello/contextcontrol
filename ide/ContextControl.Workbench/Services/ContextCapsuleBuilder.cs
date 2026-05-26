// CC-DESC: Builds compact CC-native prompts for local LLM workflow phases.

using System.Text;

namespace ContextControl.Workbench.Services;

public enum ContextCapsulePhase
{
    Chat,
    FileRequest,
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
                DIR semantic map/tree context is attached.
                Output only the smallest safe CC request list, ending with END.
                Valid lines: exact file, FUNCTION path :: symbol, FIND: exactText.
                Every exact file path must be copied from the attached DIR tree exactly.
                If the user's named path is absent from the DIR tree, treat it as a hint and return real nearby tree paths or FIND: terms.
                Never invent src/, .xaml.cs, .csproj, or framework-style paths that are not present in the tree.
                Ignore diagnostic questions about whether attachments were received; the inventory above is authoritative, and your output must still be a useful CC request list.
                FIND terms must come from the user's real task, not from capsule headings such as local LLM capsule, attachment, semantic map, or context.
                Never return END by itself. If unsure, output 2-5 FIND: terms from the user request, then END.
                """,
            ContextCapsulePhase.PatchWrite => """
                Phase contract:
                CC source/function context is attached.
                Answer from the provided source or emit raw CC-REPLACE blocks.
                Do not invent APIs not shown in context.
                """,
            ContextCapsulePhase.PatchReview => """
                Phase contract:
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
            ContextCapsulePhase.PatchWrite => "patch-write",
            ContextCapsulePhase.PatchReview => "patch-review",
            _ => "chat"
        };
    }

    private static int EstimateComfortableContextTokens(string contextLabel) => EstimateContextTokens(contextLabel);
}
