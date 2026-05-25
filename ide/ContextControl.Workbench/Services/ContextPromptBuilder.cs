// CC-DESC: Builds Context Control prompt payloads and extracts patch blocks.

using System.Text;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class ContextPromptBuilder
{
    public string BuildFreshChatPrompt(string userPrompt, string directoryExportText, string routeLabel)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Context Control fresh-chat request");
        builder.AppendLine();
        builder.AppendLine($"Route: {routeLabel}");
        builder.AppendLine("Flow: read the DIR export, ask only for the smallest safe file/function/FIND list, then wait for the CC export before producing patch work.");
        builder.AppendLine();
        builder.AppendLine("User request:");
        builder.AppendLine(string.IsNullOrWhiteSpace(userPrompt) ? "(no request text yet)" : userPrompt.Trim());
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(directoryExportText))
        {
            builder.AppendLine("DIR export:");
            builder.AppendLine(directoryExportText.TrimEnd());
        }

        return builder.ToString();
    }

    public IReadOnlyList<string> BuildCodeExportRequestLines(string promptText)
    {
        return (promptText ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeCodeExportRequestLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !string.Equals(line, "END", StringComparison.OrdinalIgnoreCase))
            .Where(IsCodeExportRequestLine)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizeCodeExportRequestLine(string? line)
    {
        var clean = (line ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        if (clean.StartsWith("- ", StringComparison.Ordinal))
        {
            clean = clean[2..].Trim();
        }

        clean = clean.Trim().TrimStart('[').TrimEnd(']').Trim();
        clean = clean.TrimEnd(',', ';').Trim();
        clean = clean.Trim('"', '\'', '`').Trim();
        clean = clean.TrimEnd(',', ';').Trim();

        return clean;
    }

    public static bool IsCodeExportRequestLine(string? line)
    {
        var clean = NormalizeCodeExportRequestLine(line);
        if (string.IsNullOrWhiteSpace(clean))
        {
            return false;
        }

        return clean.StartsWith("FUNCTION ", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("FUNC:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("SYMBOL:", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("FIND:", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".cc", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".cxx", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".c", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".h", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".vert", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".frag", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)
            || clean.Contains('/', StringComparison.Ordinal)
            || clean.Contains('\\', StringComparison.Ordinal);
    }

    public string ExtractPatchBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var matches = PatchBlockRegex().Matches(text);
        if (matches.Count == 0)
        {
            return "";
        }

        return string.Join(Environment.NewLine + Environment.NewLine, matches.Select(match => match.Value.Trim()));
    }

    public string BuildPatchSummary(string jsonPlanOutput)
    {
        if (string.IsNullOrWhiteSpace(jsonPlanOutput))
        {
            return "No patch plan returned.";
        }

        var effectiveMatch = Regex.Match(jsonPlanOutput, "\"EffectiveCount\"\\s*:\\s*(\\d+)");
        var duplicateMatch = Regex.Match(jsonPlanOutput, "\"DuplicateCount\"\\s*:\\s*(\\d+)");
        var fileMatch = Regex.Match(jsonPlanOutput, "\"FileCount\"\\s*:\\s*(\\d+)");

        var effective = effectiveMatch.Success ? effectiveMatch.Groups[1].Value : "?";
        var duplicates = duplicateMatch.Success ? duplicateMatch.Groups[1].Value : "?";
        var files = fileMatch.Success ? fileMatch.Groups[1].Value : "?";

        return $"Patch plan: {effective} effective, {duplicates} duplicate, {files} files.";
    }

    [GeneratedRegex("(?ms)^\\s*BEGIN\\s+CC-REPLACE\\s*$.*?^\\s*END\\s+CC-REPLACE\\s*$")]
    private static partial Regex PatchBlockRegex();
}
