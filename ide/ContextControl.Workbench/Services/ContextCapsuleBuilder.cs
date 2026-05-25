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
    private const int DefaultComfortableContextTokens = 4096;
    private const int DefaultOutputReserveTokens = 900;
    private const int MaxAttachmentCharacters = 28_000;

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
        builder.AppendLine("Non-negotiable workflow:");
        builder.AppendLine("- You are a ContextControl patch/chat worker, not a filesystem agent.");
        builder.AppendLine("- Use only text included in this capsule.");
        builder.AppendLine("- Do not claim to inspect project files unless their contents are included below.");
        builder.AppendLine("- Always include a short visible 'Decision summary:' before any final artifact.");
        builder.AppendLine("- If you emit private-style thinking tags such as <think>, keep them concise.");
        builder.AppendLine();
        builder.AppendLine(BuildPhaseContract(request.Phase));
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.SkillbookInstructions))
        {
            builder.AppendLine("Skillbook instructions:");
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
                    clipped = clipped[..remainingAttachmentChars] + Environment.NewLine + "[attachment clipped by local context budget]";
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
                The attached context is a DIR/tree export. Reply with the smallest safe cc.ps1 request list only after the decision summary.
                Valid request lines are exact files, FUNCTION path :: symbol, wildcard FUNCTION only when needed, or FIND: exactText.
                End request lists with END.
                """,
            ContextCapsulePhase.PatchWrite => """
                Phase contract:
                The attached context is source/function context from CC. Produce raw BEGIN CC-REPLACE blocks for the requested edit.
                Keep commentary outside patch blocks short. Do not invent APIs not shown in context.
                """,
            ContextCapsulePhase.PatchReview => """
                Phase contract:
                The attached context includes a patch. Review or repair it using only visible context. If repaired, emit complete CC-REPLACE blocks.
                """,
            _ => """
                Phase contract:
                Normal local CC chat. Answer using the included instructions and attachments. Ask for DIR/CC context when code evidence is needed.
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
