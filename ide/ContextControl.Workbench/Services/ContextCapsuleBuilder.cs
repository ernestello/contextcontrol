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
    string SkillbookInstructions,
    IReadOnlyList<ContextCapsuleAttachment> Attachments);

public sealed record ContextCapsule(
    string Text,
    ContextCapsulePhase Phase,
    int EstimatedInputTokens,
    int EstimatedAttachmentTokens,
    int ComfortableContextTokens,
    int OutputReserveTokens,
    double ContextPressurePercent,
    string Summary);

public sealed class ContextCapsuleBuilder
{
    public const int DefaultComfortableContextTokens = 4096;
    public const int DefaultOutputReserveTokens = 900;
    public const int MaxAttachmentCharacters = 28_000;
    public const string AttachmentClipMarker = "[attachment clipped by local context budget]";

    public ContextCapsule Build(ContextCapsuleBuildRequest request)
    {
        var comfortableTokens = EstimateComfortableContextTokens(request.ModelContextLabel);
        var outputReserve = DefaultOutputReserveTokens;
        var builder = new StringBuilder();

        builder.AppendLine("ContextControl local LLM capsule");
        builder.AppendLine();
        builder.AppendLine($"Model: {request.ModelId}");
        builder.AppendLine($"Phase: {FormatPhase(request.Phase)}");
        builder.AppendLine($"Comfortable context target: {comfortableTokens} tokens");
        builder.AppendLine();
        builder.AppendLine("Core rule: the visible project context is included below as attachment text.");
        builder.AppendLine("Use that text directly. You cannot access anything outside this capsule or run tools.");
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
        var remainingAttachmentChars = MaxAttachmentCharacters;
        var included = request.Attachments.Where(attachment => attachment.Included).ToArray();
        if (included.Length > 0)
        {
            builder.AppendLine("Included ContextControl attachments:");
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
        var pressure = comfortableTokens <= 0
            ? 0
            : Math.Clamp(inputTokens * 100d / Math.Max(1, comfortableTokens - outputReserve), 0, 999);
        var summary = $"{inputTokens:N0} in tok; {attachmentTokens:N0} attachment tok; {outputReserve:N0} reserve; {pressure:0.#}% of comfortable context";

        return new ContextCapsule(
            textOut,
            request.Phase,
            inputTokens,
            attachmentTokens,
            comfortableTokens,
            outputReserve,
            pressure,
            summary);
    }

    public static int EstimateTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4d);
    }

    private static string BuildPhaseContract(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => """
                Phase contract:
                DIR/tree context is attached.
                Output only the smallest safe CC request list, ending with END.
                Valid lines: exact file, FUNCTION path :: symbol, FIND: exactText.
                Every exact file path must be copied from the attached DIR tree exactly.
                If the user's named path is absent from the DIR tree, treat it as a hint and return real nearby tree paths or FIND: terms.
                Never invent src/, .xaml.cs, .csproj, or framework-style paths that are not present in the tree.
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

    private static int EstimateComfortableContextTokens(string contextLabel)
    {
        var label = contextLabel ?? "";
        if (label.Contains("8K", StringComparison.OrdinalIgnoreCase))
        {
            return 8192;
        }

        if (label.Contains("4K", StringComparison.OrdinalIgnoreCase))
        {
            return 4096;
        }

        return DefaultComfortableContextTokens;
    }
}
