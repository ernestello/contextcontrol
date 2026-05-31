// CC-DESC: Defines visible Codex harness instructions and Skillflow phases.

using System.Text;

namespace ContextControl.Workbench.Services;

public static class CodexInstructionCatalog
{
    private static readonly SkillbookEntry[] HarnessInstructions =
    [
        new(
            "codex-harness-purpose",
            "Codex harness purpose",
            """
            Codex is the reasoning engine inside ContextControl, not the write authority.
            ContextControl owns DIR, CC, GO, patch preview, and patch apply.
            Codex should spend its effort on choosing focused context and writing source-grounded answers.
            """,
            "codex",
            true),
        new(
            "codex-no-repo-navigation",
            "Codex no repo navigation",
            """
            Use the visible ContextControl capsule and attachments as the source of truth.
            Do not browse the repository, run search commands, inspect git state, or read files outside the capsule for normal CC phases.
            If the attached context is insufficient, ask for exact CC request lines instead of exploring.
            """,
            "codex",
            true),
        new(
            "codex-bus-stop-discipline",
            "Codex bus stop discipline",
            """
            Stay at the current CC bus stop.
            DIR context means request the smallest next CC export.
            CC source context means audit, explain, or write CC-REPLACE blocks from visible source.
            Patch context means review or repair the patch.
            Do not skip ahead to later phases unless the user explicitly asks for that phase.
            """,
            "codex",
            true),
        new(
            "codex-file-request-shape",
            "Codex file request shape",
            """
            In file-request phase, output only useful CC request lines.
            Valid lines are exact relative file paths copied from DIR, FUNCTION path :: symbol, FUNC: symbol, SYMBOL: name, or FIND: exactText.
            End with END.
            Do not output prose, absolute paths, broad folders, invented paths, PATH:, FILE:, or DIR: labels.
            """,
            "codex",
            true),
        new(
            "codex-patch-shape",
            "Codex patch shape",
            """
            In patch-write phase, emit raw BEGIN CC-REPLACE blocks when code changes are requested.
            Every block needs FILE and MODE, and function/region modes need NAME.
            Use insert_include for C/C++ includes, replace_region when markers exist, and whole_file only for small/new files or when safer than a targeted block.
            Do not emit git patches, apply_patch patches, shell write commands, or instructions that bypass ccReplace.
            """,
            "codex",
            true),
        new(
            "codex-minimality",
            "Codex minimality",
            """
            Remove unnecessary actions from the reasoning loop.
            Prefer the smallest safe file/function set, the shortest sufficient answer, and the least invasive patch shape.
            Do not ask for build files, tests, generated output, binary assets, or dependency folders unless they are truly needed.
            """,
            "codex",
            true),
        new(
            "codex-evidence-boundary",
            "Codex evidence boundary",
            """
            Separate what the visible code proves from what is inferred.
            If a claim depends on missing source, request the missing CC context instead of guessing.
            If a patch depends on an API not visible in the CC export, stop and ask for the exact file/function context.
            """,
            "codex",
            true)
    ];

    private static readonly SkillbookEntry[] SkillflowEntries =
    [
        new(
            "skillflow-01-request",
            "Skillflow 01 - Request",
            """
            User action: write the concrete task in the prompt bar.
            User expectation: the task becomes the durable request for later DIR, CC, and patch phases.
            Codex expectation: preserve the task intent and do not start editing before context exists.
            """,
            "skillflow",
            true),
        new(
            "skillflow-02-dir",
            "Skillflow 02 - DIR",
            """
            User action: press DIR.
            User expectation: ContextControl exports the filtered project tree and attaches it to the chat.
            Codex expectation: read the DIR attachment and return the smallest safe CC request list.
            """,
            "skillflow",
            true),
        new(
            "skillflow-03-resolve",
            "Skillflow 03 - Resolve",
            """
            User action: send the DIR attachment in Codex mode or review locally resolved lines.
            User expectation: receive exact file paths, FUNCTION lines, SYMBOL lines, or FIND terms ending with END.
            Codex expectation: no prose and no patching; only request the next source context.
            """,
            "skillflow",
            true),
        new(
            "skillflow-04-cc",
            "Skillflow 04 - CC",
            """
            User action: press CC with the approved request lines.
            User expectation: ContextControl exports selected source/function context and attaches it.
            Codex expectation: treat attached source as the complete evidence set for audit or patch writing.
            """,
            "skillflow",
            true),
        new(
            "skillflow-05-patch",
            "Skillflow 05 - Patch",
            """
            User action: ask for an audit, implementation, or repair from the attached CC source.
            User expectation: receive a source-grounded answer or raw CC-REPLACE blocks.
            Codex expectation: use the CC patch shape and never write files directly.
            """,
            "skillflow",
            true),
        new(
            "skillflow-06-go",
            "Skillflow 06 - GO",
            """
            User action: press GO on a patch snippet or prompt text containing CC-REPLACE blocks.
            User expectation: ccReplace writes patch.txt and previews the plan without changing source files.
            Codex expectation: if GO fails, repair the patch from the visible failure and source context.
            """,
            "skillflow",
            true),
        new(
            "skillflow-07-apply",
            "Skillflow 07 - Apply",
            """
            User action: apply effective or all previewed actions.
            User expectation: ccReplace performs the write and keeps the patch event auditable.
            Codex expectation: discuss validation or follow-up only after ContextControl reports the apply result.
            """,
            "skillflow",
            true)
    ];

    public static IReadOnlyList<SkillbookEntry> SkillbookEntries =>
        HarnessInstructions.Concat(SkillflowEntries).ToArray();

    public static string BuildCodexInstructionText(ContextCapsulePhase phase)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Visible Codex harness instructions:");
        foreach (var entry in HarnessInstructions)
        {
            builder.AppendLine();
            builder.AppendLine($"## {entry.Title}");
            builder.AppendLine(entry.Text.Trim());
        }

        builder.AppendLine();
        builder.AppendLine("## Active phase contract");
        builder.AppendLine(BuildPhaseContract(phase));
        builder.AppendLine();
        builder.AppendLine("## Skillflow phase");
        builder.AppendLine(BuildSkillflowPhase(phase));

        return builder.ToString().TrimEnd();
    }

    public static string BuildPhaseContract(ContextCapsulePhase phase)
    {
        return phase switch
        {
            ContextCapsulePhase.FileRequest => """
                Output only CC request lines copied from DIR or valid discovery terms.
                End with END.
                Do not solve the task, write code, summarize the tree, or add commentary.
                """,
            ContextCapsulePhase.SourceAudit => """
                Analyze only the attached CC source context.
                Keep findings grounded in exact visible code evidence.
                If source is missing, ask for valid CC request lines ending with END.
                """,
            ContextCapsulePhase.PatchWrite => """
                Write raw CC-REPLACE blocks only when code changes are requested.
                If visible source is insufficient, output NEED_MORE_CONTEXT followed by valid CC request lines and END.
                Do not use shell commands, git patches, or direct file edits.
                """,
            ContextCapsulePhase.PatchReview => """
                Review or repair the attached patch using only visible patch/source context.
                If repaired, emit complete CC-REPLACE blocks.
                """,
            _ => """
                Normal chat is allowed, but ask for DIR/CC context before making code claims.
                """
        };
    }

    private static string BuildSkillflowPhase(ContextCapsulePhase phase)
    {
        var key = phase switch
        {
            ContextCapsulePhase.FileRequest => "skillflow-03-resolve",
            ContextCapsulePhase.SourceAudit => "skillflow-05-patch",
            ContextCapsulePhase.PatchWrite => "skillflow-05-patch",
            ContextCapsulePhase.PatchReview => "skillflow-06-go",
            _ => "skillflow-01-request"
        };

        return SkillflowEntries.First(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Text.Trim();
    }
}
