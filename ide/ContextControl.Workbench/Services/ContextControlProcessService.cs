// CC-DESC: Runs Context Control PowerShell scripts for the native workbench.

using System.Diagnostics;
using System.Text;

namespace ContextControl.Workbench.Services;

public sealed class ContextControlProcessService
{
    private const string DirectoryExportFileName = "cc_project_dir.md";
    private const string CodeExportFileName = "cc_code_export.md";
    private const string PatchFileName = "patch.txt";

    public ContextControlProcessService(string contextRoot)
    {
        ContextRoot = string.IsNullOrWhiteSpace(contextRoot)
            ? FindContextControlRoot(Directory.GetCurrentDirectory()) ?? AppContext.BaseDirectory
            : Path.GetFullPath(contextRoot);
    }

    public string ContextRoot { get; }
    public string DirectoryExportPath => Path.Combine(ContextRoot, DirectoryExportFileName);
    public string CodeExportPath => Path.Combine(ContextRoot, CodeExportFileName);
    public string PatchPath => Path.Combine(ContextRoot, PatchFileName);

    public static string? FindContextControlRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directory = File.Exists(startPath)
            ? new DirectoryInfo(Path.GetDirectoryName(startPath) ?? "")
            : new DirectoryInfo(startPath);

        while (directory is not null)
        {
            if (LooksLikeContextControl(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public async Task<ContextControlCommandResult> RunDirectoryExportAsync(CancellationToken cancellationToken = default)
    {
        return await RunPowerShellScriptAsync(
            "DIR",
            "ccDir.ps1",
            ["-OutputFile", DirectoryExportPath],
            null,
            DirectoryExportPath,
            cancellationToken);
    }

    public async Task<ContextControlCommandResult> RunCodeExportAsync(IEnumerable<string> requestLines, CancellationToken cancellationToken = default)
    {
        var input = NormalizeRequestInput(requestLines);
        return await RunPowerShellScriptAsync(
            "CC",
            "cc.ps1",
            ["-OutputFile", CodeExportPath],
            input,
            CodeExportPath,
            cancellationToken);
    }

    public async Task WritePatchAsync(string patchText, CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(PatchPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await File.WriteAllTextAsync(PatchPath, patchText ?? "", new UTF8Encoding(false), cancellationToken);
    }

    public async Task<ContextControlCommandResult> PreviewPatchAsync(CancellationToken cancellationToken = default)
    {
        return await RunPowerShellScriptAsync(
            "GO preview",
            "ccReplace.ps1",
            ["-InputFile", PatchPath, "-PlanOnly", "-Json"],
            null,
            PatchPath,
            cancellationToken);
    }

    public async Task<ContextControlCommandResult> ApplyPatchAsync(string decision, CancellationToken cancellationToken = default)
    {
        var cleanDecision = string.Equals(decision, "all", StringComparison.OrdinalIgnoreCase)
            ? "all"
            : "effective";

        return await RunPowerShellScriptAsync(
            "GO apply",
            "ccReplace.ps1",
            ["-InputFile", PatchPath, "-Apply", cleanDecision],
            null,
            PatchPath,
            cancellationToken);
    }

    public async Task<string> ReadOutputFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private async Task<ContextControlCommandResult> RunPowerShellScriptAsync(
        string command,
        string scriptName,
        IReadOnlyList<string> arguments,
        string? standardInput,
        string? outputFile,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(ContextRoot, scriptName);
        if (!File.Exists(scriptPath))
        {
            return new ContextControlCommandResult(command, 1, "", $"Script not found: {scriptPath}", outputFile);
        }

        try
        {
            return await RunProcessAsync("pwsh", command, scriptPath, arguments, standardInput, outputFile, cancellationToken);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return await RunProcessAsync("powershell", command, scriptPath, arguments, standardInput, outputFile, cancellationToken);
        }
    }

    private async Task<ContextControlCommandResult> RunProcessAsync(
        string executable,
        string command,
        string scriptPath,
        IReadOnlyList<string> arguments,
        string? standardInput,
        string? outputFile,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = ContextRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        if (!string.IsNullOrWhiteSpace(standardInput))
        {
            await process.StandardInput.WriteAsync(standardInput);
        }

        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        return new ContextControlCommandResult(command, process.ExitCode, output, error, outputFile);
    }

    private static string NormalizeRequestInput(IEnumerable<string> requestLines)
    {
        var lines = requestLines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !string.Equals(line, "END", StringComparison.OrdinalIgnoreCase))
            .ToList();

        lines.Add("END");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static bool LooksLikeContextControl(string path)
    {
        return Directory.Exists(path)
            && File.Exists(Path.Combine(path, "ccStart.ps1"))
            && File.Exists(Path.Combine(path, "ccDir.ps1"))
            && File.Exists(Path.Combine(path, "cc.ps1"))
            && File.Exists(Path.Combine(path, "ccReplace.ps1"));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Cancellation cleanup is best-effort.
        }
    }
}
