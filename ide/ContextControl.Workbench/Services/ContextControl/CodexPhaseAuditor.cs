// CC-DESC: Validates Codex CLI output against the active ContextControl workflow phase.

using System.Text;

namespace ContextControl.Workbench.Services;

public enum CodexPhaseAuditLevel
{
    Pass,
    Warning,
    Error
}

public sealed record CodexPhaseAuditResult(
    ContextCapsulePhase Phase,
    CodexPhaseAuditLevel Level,
    string Summary,
    IReadOnlyList<string> Details,
    IReadOnlyList<string> RequestLines,
    int PatchBlockCount)
{
    public bool Passed => Level != CodexPhaseAuditLevel.Error;
    public bool HasWarnings => Level == CodexPhaseAuditLevel.Warning;
    public bool HasRequestLines => RequestLines.Count > 0;
    public bool HasPatchBlocks => PatchBlockCount > 0;

    public string ToDiagnosticText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Codex phase audit: {Level}");
        builder.AppendLine(Summary);

        foreach (var detail in Details.Take(8))
        {
            builder.AppendLine($"- {detail}");
        }

        if (RequestLines.Count > 0)
        {
            builder.AppendLine("Usable CC request lines:");
            foreach (var requestLine in RequestLines.Take(12))
            {
                builder.AppendLine(requestLine);
            }
        }

        if (PatchBlockCount > 0)
        {
            builder.AppendLine($"CC-REPLACE blocks: {PatchBlockCount:N0}");
        }

        return builder.ToString().TrimEnd();
    }
}

public static class CodexPhaseAuditor
{
    public static CodexPhaseAuditResult Audit(ContextCapsulePhase phase, string message, ContextPromptBuilder? promptBuilder = null)
    {
        promptBuilder ??= new ContextPromptBuilder();
        var text = message ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return Create(
                phase,
                CodexPhaseAuditLevel.Error,
                "Codex returned no visible phase output.",
                ["The harness process completed without a final message."],
                [],
                0);
        }

        var patchText = promptBuilder.ExtractPatchBlocks(text);
        var patchBlocks = SplitPatchBlocks(patchText);
        var requestAudit = AuditRequestLines(text);
        var directActionWarnings = FindDirectActionClaims(text);

        return phase switch
        {
            ContextCapsulePhase.FileRequest => AuditFileRequest(phase, requestAudit, patchBlocks.Count, directActionWarnings),
            ContextCapsulePhase.SourceAudit => AuditSourceAudit(phase, text, requestAudit, patchBlocks, patchText, promptBuilder, directActionWarnings),
            ContextCapsulePhase.PatchWrite => AuditPatchWrite(phase, text, requestAudit, patchBlocks, patchText, promptBuilder, directActionWarnings),
            ContextCapsulePhase.PatchReview => AuditPatchReview(phase, text, patchBlocks, patchText, promptBuilder, directActionWarnings),
            _ => AuditChat(phase, directActionWarnings)
        };
    }

    private static CodexPhaseAuditResult AuditFileRequest(
        ContextCapsulePhase phase,
        RequestLineAudit requestAudit,
        int patchBlockCount,
        IReadOnlyList<string> directActionWarnings)
    {
        var details = new List<string>();
        details.AddRange(directActionWarnings);

        if (patchBlockCount > 0)
        {
            details.Add("File-request phase returned CC-REPLACE blocks; this phase should only select the next CC source request.");
        }

        if (requestAudit.RequestLines.Count == 0)
        {
            details.Add("No exact file, FUNCTION, SYMBOL, FUNC, or FIND lines were found.");
            return Create(
                phase,
                CodexPhaseAuditLevel.Error,
                "Codex phase audit failed: file-request output has no usable CC request lines.",
                details,
                requestAudit.RequestLines,
                patchBlockCount);
        }

        if (!requestAudit.EndsWithEnd)
        {
            details.Add("The request list did not end with END; ContextControl can normalize the snippet, but the phase contract was not followed exactly.");
        }

        foreach (var extra in requestAudit.ExtraLines.Take(4))
        {
            details.Add($"Extra non-request line in file-request output: {extra}");
        }

        var level = details.Count == 0 ? CodexPhaseAuditLevel.Pass : CodexPhaseAuditLevel.Warning;
        var summary = level == CodexPhaseAuditLevel.Pass
            ? $"Codex phase audit passed: {requestAudit.RequestLines.Count:N0} CC request line(s) ending with END."
            : $"Codex phase audit warning: {requestAudit.RequestLines.Count:N0} usable CC request line(s), but the response needs review.";
        return Create(phase, level, summary, details, requestAudit.RequestLines, patchBlockCount);
    }

    private static CodexPhaseAuditResult AuditSourceAudit(
        ContextCapsulePhase phase,
        string text,
        RequestLineAudit requestAudit,
        IReadOnlyList<string> patchBlocks,
        string patchText,
        ContextPromptBuilder promptBuilder,
        IReadOnlyList<string> directActionWarnings)
    {
        var details = new List<string>();
        details.AddRange(directActionWarnings);

        if (patchBlocks.Count > 0)
        {
            var patchError = promptBuilder.ValidatePatchBlocks(patchText);
            if (!string.IsNullOrWhiteSpace(patchError))
            {
                details.Add(patchError);
                return Create(
                    phase,
                    CodexPhaseAuditLevel.Error,
                    "Codex phase audit failed: source-audit returned malformed CC-REPLACE blocks.",
                    details,
                    requestAudit.RequestLines,
                    patchBlocks.Count);
            }

            details.Add("Source-audit phase returned patch blocks; verify the user explicitly asked for code changes before pressing GO.");
        }

        if (requestAudit.RequestLines.Count > 0 && !requestAudit.EndsWithEnd)
        {
            details.Add("The context request list did not end with END.");
        }

        var level = details.Count == 0 ? CodexPhaseAuditLevel.Pass : CodexPhaseAuditLevel.Warning;
        var summary = string.IsNullOrWhiteSpace(text)
            ? "Codex phase audit failed: source-audit returned no analysis."
            : level == CodexPhaseAuditLevel.Pass
                ? "Codex phase audit passed: source-audit returned review output."
                : "Codex phase audit warning: source-audit output needs review.";
        return Create(phase, level, summary, details, requestAudit.RequestLines, patchBlocks.Count);
    }

    private static CodexPhaseAuditResult AuditPatchWrite(
        ContextCapsulePhase phase,
        string text,
        RequestLineAudit requestAudit,
        IReadOnlyList<string> patchBlocks,
        string patchText,
        ContextPromptBuilder promptBuilder,
        IReadOnlyList<string> directActionWarnings)
    {
        var details = new List<string>();
        details.AddRange(directActionWarnings);

        if (patchBlocks.Count > 0)
        {
            var patchError = promptBuilder.ValidatePatchBlocks(patchText);
            if (!string.IsNullOrWhiteSpace(patchError))
            {
                details.Add(patchError);
                return Create(
                    phase,
                    CodexPhaseAuditLevel.Error,
                    "Codex phase audit failed: patch-write returned malformed CC-REPLACE blocks.",
                    details,
                    requestAudit.RequestLines,
                    patchBlocks.Count);
            }

            foreach (var warning in FindPatchHeaderWarnings(patchBlocks).Take(4))
            {
                details.Add(warning);
            }

            var level = details.Count == 0 ? CodexPhaseAuditLevel.Pass : CodexPhaseAuditLevel.Warning;
            var summary = level == CodexPhaseAuditLevel.Pass
                ? $"Codex phase audit passed: {patchBlocks.Count:N0} valid CC-REPLACE block(s)."
                : $"Codex phase audit warning: {patchBlocks.Count:N0} CC-REPLACE block(s) are usable, but headers/actions need review.";
            return Create(phase, level, summary, details, requestAudit.RequestLines, patchBlocks.Count);
        }

        if (text.Contains("BEGIN CC-REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            details.Add(promptBuilder.ValidatePatchBlocks(text));
            return Create(
                phase,
                CodexPhaseAuditLevel.Error,
                "Codex phase audit failed: patch-write attempted malformed CC-REPLACE output.",
                details.Where(detail => !string.IsNullOrWhiteSpace(detail)).ToArray(),
                requestAudit.RequestLines,
                0);
        }

        if (HasNeedMoreContext(text))
        {
            if (requestAudit.RequestLines.Count == 0)
            {
                details.Add("NEED_MORE_CONTEXT was present, but no usable CC request lines followed it.");
                return Create(
                    phase,
                    CodexPhaseAuditLevel.Error,
                    "Codex phase audit failed: NEED_MORE_CONTEXT did not include CC request lines.",
                    details,
                    requestAudit.RequestLines,
                    0);
            }

            if (!requestAudit.EndsWithEnd)
            {
                details.Add("NEED_MORE_CONTEXT request lines did not end with END.");
            }

            var level = details.Count == 0 ? CodexPhaseAuditLevel.Pass : CodexPhaseAuditLevel.Warning;
            var summary = level == CodexPhaseAuditLevel.Pass
                ? $"Codex phase audit passed: patch-write asked for {requestAudit.RequestLines.Count:N0} more context line(s)."
                : "Codex phase audit warning: NEED_MORE_CONTEXT output needs review.";
            return Create(phase, level, summary, details, requestAudit.RequestLines, 0);
        }

        if (requestAudit.RequestLines.Count > 0)
        {
            details.Add("Patch-write returned request lines without the required NEED_MORE_CONTEXT marker.");
            return Create(
                phase,
                CodexPhaseAuditLevel.Error,
                "Codex phase audit failed: patch-write answered like file-request phase.",
                details,
                requestAudit.RequestLines,
                0);
        }

        details.Add("Patch-write must return raw CC-REPLACE blocks, or NEED_MORE_CONTEXT plus valid CC request lines ending with END.");
        return Create(
            phase,
            CodexPhaseAuditLevel.Error,
            "Codex phase audit failed: patch-write returned no patch or context request.",
            details,
            requestAudit.RequestLines,
            0);
    }

    private static CodexPhaseAuditResult AuditPatchReview(
        ContextCapsulePhase phase,
        string text,
        IReadOnlyList<string> patchBlocks,
        string patchText,
        ContextPromptBuilder promptBuilder,
        IReadOnlyList<string> directActionWarnings)
    {
        var details = new List<string>();
        details.AddRange(directActionWarnings);

        if (patchBlocks.Count > 0)
        {
            var patchError = promptBuilder.ValidatePatchBlocks(patchText);
            if (!string.IsNullOrWhiteSpace(patchError))
            {
                details.Add(patchError);
                return Create(
                    phase,
                    CodexPhaseAuditLevel.Error,
                    "Codex phase audit failed: patch-review returned malformed CC-REPLACE blocks.",
                    details,
                    [],
                    patchBlocks.Count);
            }
        }

        var level = details.Count == 0 ? CodexPhaseAuditLevel.Pass : CodexPhaseAuditLevel.Warning;
        var summary = patchBlocks.Count > 0
            ? $"Codex phase audit passed: patch-review returned {patchBlocks.Count:N0} valid repair block(s)."
            : string.IsNullOrWhiteSpace(text)
                ? "Codex phase audit failed: patch-review returned no review."
                : "Codex phase audit passed: patch-review returned review output.";
        return Create(phase, level, summary, details, [], patchBlocks.Count);
    }

    private static CodexPhaseAuditResult AuditChat(
        ContextCapsulePhase phase,
        IReadOnlyList<string> directActionWarnings)
    {
        var level = directActionWarnings.Count == 0 ? CodexPhaseAuditLevel.Pass : CodexPhaseAuditLevel.Warning;
        var summary = level == CodexPhaseAuditLevel.Pass
            ? "Codex phase audit passed: chat output returned normally."
            : "Codex phase audit warning: chat output claimed direct repo actions.";
        return Create(phase, level, summary, directActionWarnings, [], 0);
    }

    private static RequestLineAudit AuditRequestLines(string text)
    {
        var requestLines = new List<string>();
        var extraLines = new List<string>();
        var meaningfulLines = new List<string>();
        foreach (var rawLine in NormalizeLines(text))
        {
            var trimmed = rawLine.Trim();
            var normalized = ContextPromptBuilder.NormalizeCodeExportRequestLine(trimmed);
            if (string.IsNullOrWhiteSpace(trimmed) || IsIgnoredRequestAuditLine(trimmed, normalized))
            {
                continue;
            }

            meaningfulLines.Add(normalized);
            if (normalized.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ContextPromptBuilder.IsCodeExportRequestLine(normalized))
            {
                if (!requestLines.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    requestLines.Add(normalized);
                }

                continue;
            }

            extraLines.Add(trimmed);
        }

        var endsWithEnd = meaningfulLines.LastOrDefault()?.Equals("END", StringComparison.OrdinalIgnoreCase) == true;
        return new RequestLineAudit(requestLines, endsWithEnd, extraLines);
    }

    private static bool IsIgnoredRequestAuditLine(string rawLine, string normalizedLine)
    {
        var clean = rawLine.Trim();
        return clean.StartsWith("```", StringComparison.Ordinal)
            || clean.StartsWith("~~~", StringComparison.Ordinal)
            || normalizedLine.StartsWith("Research scope:", StringComparison.OrdinalIgnoreCase)
            || normalizedLine.Equals("NEED_MORE_CONTEXT", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SplitPatchBlocks(string patchText)
    {
        var blocks = new List<string>();
        if (string.IsNullOrWhiteSpace(patchText))
        {
            return blocks;
        }

        var builder = new StringBuilder();
        var inBlock = false;
        foreach (var rawLine in NormalizeLines(patchText))
        {
            var line = rawLine.Trim();
            if (line.Equals("BEGIN CC-REPLACE", StringComparison.OrdinalIgnoreCase))
            {
                inBlock = true;
                builder.Clear();
            }

            if (!inBlock)
            {
                continue;
            }

            builder.AppendLine(rawLine);
            if (line.Equals("END CC-REPLACE", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(builder.ToString().TrimEnd());
                inBlock = false;
                builder.Clear();
            }
        }

        return blocks;
    }

    private static IReadOnlyList<string> FindPatchHeaderWarnings(IReadOnlyList<string> patchBlocks)
    {
        var warnings = new List<string>();
        for (var index = 0; index < patchBlocks.Count; index++)
        {
            var block = patchBlocks[index];
            var headers = ReadPatchHeaders(block);
            if (!headers.ContainsKey("FILE"))
            {
                warnings.Add($"CC-REPLACE block {index + 1:N0} should use FILE: for Codex output even though GO may accept legacy aliases.");
            }
        }

        return warnings;
    }

    private static Dictionary<string, string> ReadPatchHeaders(string block)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in NormalizeLines(block).Skip(1))
        {
            var line = rawLine.Trim();
            if (line.Equals("---", StringComparison.Ordinal)
                || line.Equals("END CC-REPLACE", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                headers[key] = value;
            }
        }

        return headers;
    }

    private static IReadOnlyList<string> FindDirectActionClaims(string text)
    {
        var warnings = new List<string>();
        var clean = text ?? "";
        string[] markers =
        [
            "apply_patch",
            "I ran ",
            "I've run ",
            "I used rg",
            "I edited ",
            "I've edited ",
            "I changed ",
            "I've changed ",
            "I wrote ",
            "I created ",
            "I deleted ",
            "I committed "
        ];

        foreach (var marker in markers)
        {
            if (clean.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Codex claimed a direct repo action inside the read-only harness: {marker.Trim()}");
                break;
            }
        }

        return warnings;
    }

    private static bool HasNeedMoreContext(string text)
    {
        return NormalizeLines(text).Any(line => line.Trim().Equals("NEED_MORE_CONTEXT", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> NormalizeLines(string text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static CodexPhaseAuditResult Create(
        ContextCapsulePhase phase,
        CodexPhaseAuditLevel level,
        string summary,
        IReadOnlyList<string> details,
        IReadOnlyList<string> requestLines,
        int patchBlockCount)
    {
        return new CodexPhaseAuditResult(
            phase,
            level,
            summary,
            details.Where(detail => !string.IsNullOrWhiteSpace(detail)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            requestLines.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            patchBlockCount);
    }

    private sealed record RequestLineAudit(
        IReadOnlyList<string> RequestLines,
        bool EndsWithEnd,
        IReadOnlyList<string> ExtraLines);
}
