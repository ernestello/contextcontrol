// CC-DESC: Local LLM service slice extracted from LocalLlmService.cs.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    public async Task<LocalLlmImageGenerationResult> GenerateImageAsync(
        string modelId,
        string prompt,
        string outputDirectory,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new LocalLlmImageGenerationResult(false, "Choose an installed image generation model first.", [], "");
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new LocalLlmImageGenerationResult(false, "Write an image prompt first.", [], "");
        }

        var resolvedOutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ContextControl", "image-gen")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(resolvedOutputDirectory);

        if (IsDiffusersImageModel(modelId))
        {
            return await GenerateImageWithDiffusersAsync(
                modelId,
                prompt,
                resolvedOutputDirectory,
                progress,
                terminal,
                cancellationToken).ConfigureAwait(false);
        }

        if (IsStableDiffusionCppImageModel(modelId))
        {
            return await GenerateImageWithStableDiffusionCppAsync(
                modelId,
                prompt,
                resolvedOutputDirectory,
                progress,
                terminal,
                cancellationToken).ConfigureAwait(false);
        }

        var executablePath = ResolveOllamaExecutable();
        if (executablePath is null)
        {
            return new LocalLlmImageGenerationResult(false, "Ollama command was not found. Install Ollama before generating images.", [], resolvedOutputDirectory);
        }

        var before = SnapshotImageFiles(resolvedOutputDirectory);
        var startedUtc = DateTime.UtcNow.AddSeconds(-2);
        progress?.Report(new LocalLlmGenerationProgress("Preparing image generation...", null, null, null, null, null, null, null, false));
        terminal?.Report($"> ollama run {modelId} \"{prompt.Trim()}\"");

        var result = await RunProcessStreamingAsync(
            executablePath,
            ["run", modelId, prompt.Trim()],
            TimeSpan.FromMinutes(30),
            chunk =>
            {
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                }

                progress?.Report(new LocalLlmGenerationProgress("Generating image...", null, null, null, null, null, null, null, false));
            },
            cancellationToken,
            resolvedOutputDirectory).ConfigureAwait(false);

        var images = FindGeneratedImageFiles(resolvedOutputDirectory, before, startedUtc);
        progress?.Report(new LocalLlmGenerationProgress("Image generation complete.", null, null, null, null, null, null, null, true));

        if (!result.Started)
        {
            return new LocalLlmImageGenerationResult(false, "Ollama could not be started.", [], resolvedOutputDirectory);
        }

        if (result.ExitCode != 0)
        {
            var reason = FirstFailureLine(result.StandardError) ?? FirstFailureLine(result.StandardOutput) ?? $"ollama run exited {result.ExitCode}.";
            return new LocalLlmImageGenerationResult(false, reason, images, resolvedOutputDirectory, result.StandardOutput);
        }

        if (images.Count == 0)
        {
            return new LocalLlmImageGenerationResult(
                false,
                "Ollama finished, but no generated image file was detected.",
                [],
                resolvedOutputDirectory,
                result.StandardOutput);
        }

        return new LocalLlmImageGenerationResult(
            true,
            $"Generated {images.Count:N0} image(s) with {modelId}.",
            images,
            resolvedOutputDirectory,
            result.StandardOutput);
    }

    private static async Task<LocalLlmImageGenerationResult> GenerateImageWithDiffusersAsync(
        string modelId,
        string prompt,
        string resolvedOutputDirectory,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken)
    {
        var pythonResolution = await ResolvePythonForModulesAsync(DiffusersPythonRequirements, cancellationToken).ConfigureAwait(false);
        if (!pythonResolution.Succeeded || pythonResolution.Executable is null)
        {
            return new LocalLlmImageGenerationResult(false, pythonResolution.Status, [], resolvedOutputDirectory);
        }

        var python = pythonResolution.Executable;
        var pythonEnvironment = pythonResolution.IsManaged
            ? PythonDependencyEnvironment.ManagedProcessEnvironment
            : null;
        var before = SnapshotImageFiles(resolvedOutputDirectory);
        var startedUtc = DateTime.UtcNow.AddSeconds(-2);
        var outputPath = Path.Combine(resolvedOutputDirectory, $"cc-image-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var script = """
import sys, torch
from diffusers import AutoPipelineForText2Image

model_id, prompt, output_path = sys.argv[1], sys.argv[2], sys.argv[3]
dtype = torch.float16 if torch.cuda.is_available() else torch.float32
pipe = AutoPipelineForText2Image.from_pretrained(model_id, torch_dtype=dtype, safety_checker=None)
if torch.cuda.is_available():
    pipe = pipe.to("cuda")
    pipe.enable_attention_slicing()
else:
    pipe.enable_attention_slicing()
try:
    pipe.enable_model_cpu_offload()
except Exception:
    pass
steps = 8 if "LCM" in model_id or "turbo" in model_id.lower() else 24
image = pipe(prompt, num_inference_steps=steps, guidance_scale=0.0 if "turbo" in model_id.lower() else 7.0).images[0]
image.save(output_path)
print("Generated image.")
""";

        progress?.Report(new LocalLlmGenerationProgress("Preparing Diffusers image generation...", null, null, null, null, null, null, null, false));
        terminal?.Report($"> {Path.GetFileName(python)} -c <diffusers> {modelId}");
        var result = await RunProcessStreamingAsync(
            python,
            ["-c", script, modelId, prompt.Trim(), outputPath],
            TimeSpan.FromMinutes(45),
            chunk =>
            {
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                }

                progress?.Report(new LocalLlmGenerationProgress("Generating image with Diffusers...", null, null, null, null, null, null, null, false));
            },
            cancellationToken,
            resolvedOutputDirectory,
            pythonEnvironment).ConfigureAwait(false);

        var images = FindGeneratedImageFiles(resolvedOutputDirectory, before, startedUtc);
        progress?.Report(new LocalLlmGenerationProgress("Image generation complete.", null, null, null, null, null, null, null, true));
        if (!result.Started)
        {
            return new LocalLlmImageGenerationResult(false, "Python could not be started.", [], resolvedOutputDirectory);
        }

        if (result.ExitCode != 0)
        {
            var reason = FirstFailureLine(result.StandardError) ?? FirstFailureLine(result.StandardOutput) ?? $"Diffusers exited {result.ExitCode}.";
            return new LocalLlmImageGenerationResult(false, reason, images, resolvedOutputDirectory, result.StandardOutput);
        }

        return images.Count == 0
            ? new LocalLlmImageGenerationResult(false, "Diffusers finished, but no generated image file was detected.", [], resolvedOutputDirectory, result.StandardOutput)
            : new LocalLlmImageGenerationResult(true, $"Generated {images.Count:N0} image(s) with {modelId}.", images, resolvedOutputDirectory, result.StandardOutput);
    }

    private static readonly PythonModuleRequirement[] DiffusersPythonRequirements =
    [
        new("torch", "torch"),
        new("diffusers", "diffusers"),
        new("transformers", "transformers"),
        new("accelerate", "accelerate"),
        new("safetensors", "safetensors"),
        new("Pillow", "PIL")
    ];

    private static async Task<PythonExecutableResolution> ResolvePythonForModulesAsync(
        IReadOnlyList<PythonModuleRequirement> requirements,
        CancellationToken cancellationToken)
    {
        var candidates = PythonDependencyEnvironment.FindPythonCandidatesForDetection("diffusers");
        if (candidates.Count == 0)
        {
            return PythonExecutableResolution.Failed("Python was not found. Install Python, then install Diffusers from the Dependencies page to create a managed ContextControl venv.");
        }

        var failures = new List<string>();
        var script = PythonDependencyEnvironment.BuildPythonModuleImportScript(
            requirements.ToDictionary(requirement => requirement.DisplayName, requirement => requirement.ModuleName, StringComparer.OrdinalIgnoreCase));
        foreach (var candidate in candidates)
        {
            var result = await RunProcessAsync(
                candidate.Executable,
                ["-c", script],
                TimeSpan.FromSeconds(15),
                cancellationToken,
                candidate.IsManaged ? PythonDependencyEnvironment.ManagedProcessEnvironment : null).ConfigureAwait(false);

            if (!result.Started)
            {
                failures.Add($"{candidate.Executable}: could not be started");
                continue;
            }

            if (result.ExitCode == 0)
            {
                return PythonExecutableResolution.Ready(candidate.Executable, FirstLine(result.StandardOutput) ?? candidate.Executable, candidate.IsManaged);
            }

            var reason = FirstFailureLine(result.StandardError) ?? FirstFailureLine(result.StandardOutput) ?? $"exited {result.ExitCode}";
            failures.Add($"{candidate.Executable}: {reason}");
        }

        var firstFailure = failures.FirstOrDefault();
        var status = firstFailure is null
            ? "Python was found, but Diffusers dependencies could not be validated."
            : $"No Python environment could import Diffusers dependencies. First failure: {firstFailure}";
        return PythonExecutableResolution.Failed(status);
    }

    private static IReadOnlyList<string> FindPythonExecutableCandidates()
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(Environment.GetEnvironmentVariable("CC_PYTHON"));

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var executableName in new[] { "python.exe", "python", "py.exe", "py" })
                {
                    AddCandidate(Path.Combine(directory, executableName));
                }
            }
        }

        return candidates;

        void AddCandidate(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            try
            {
                var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate.Trim('"')));
                if (File.Exists(fullPath) && seen.Add(fullPath))
                {
                    candidates.Add(fullPath);
                }
            }
            catch
            {
                // Ignore malformed PATH or CC_PYTHON entries.
            }
        }
    }

    private static string BuildPythonModuleImportScript(IReadOnlyList<PythonModuleRequirement> requirements)
    {
        var packageLines = string.Join(
            "\n",
            requirements.Select(requirement => $"    ({EscapePythonString(requirement.DisplayName)}, {EscapePythonString(requirement.ModuleName)}),"));

        return $$"""
import importlib
import sys

packages = [
{{packageLines}}
]
errors = []
for name, module in packages:
    try:
        importlib.import_module(module)
    except ModuleNotFoundError as exc:
        if exc.name == module or module.startswith(exc.name + "."):
            errors.append(f"Missing {name}")
        else:
            errors.append(f"{name}: {type(exc).__name__}: {exc}")
    except Exception as exc:
        errors.append(f"{name}: {type(exc).__name__}: {exc}")
if errors:
    print("; ".join(errors))
    sys.exit(1)
print(sys.executable)
""";
    }

    private static string EscapePythonString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static async Task<LocalLlmImageGenerationResult> GenerateImageWithStableDiffusionCppAsync(
        string modelId,
        string prompt,
        string resolvedOutputDirectory,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken)
    {
        var executableNames = new[] { "sd.exe", "sd", "stable-diffusion-cli.exe", "stable-diffusion-cli" };
        var executable = NativeDependencyEnvironment.FindManagedExecutable("stable_diffusion_cpp", executableNames)
            ?? FindExecutableOnPath("sd.exe")
            ?? FindExecutableOnPath("sd")
            ?? FindExecutableOnPath("stable-diffusion-cli.exe")
            ?? FindExecutableOnPath("stable-diffusion-cli");
        if (executable is null)
        {
            return new LocalLlmImageGenerationResult(false, "stable-diffusion.cpp was not found in ContextControl's managed native store or on PATH. Install it and run with a compatible GGUF model file.", [], resolvedOutputDirectory);
        }

        var modelPath = Environment.GetEnvironmentVariable("CC_IMAGE_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            return new LocalLlmImageGenerationResult(false, "Set CC_IMAGE_MODEL_PATH to the local GGUF diffusion model file before using this quantized image route.", [], resolvedOutputDirectory);
        }

        var before = SnapshotImageFiles(resolvedOutputDirectory);
        var startedUtc = DateTime.UtcNow.AddSeconds(-2);
        var outputPath = Path.Combine(resolvedOutputDirectory, $"cc-image-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        progress?.Report(new LocalLlmGenerationProgress("Preparing stable-diffusion.cpp generation...", null, null, null, null, null, null, null, false));
        terminal?.Report($"> {Path.GetFileName(executable)} -m {modelPath} -p \"{prompt.Trim()}\"");
        var result = await RunProcessStreamingAsync(
            executable,
            ["-m", modelPath, "-p", prompt.Trim(), "-o", outputPath],
            TimeSpan.FromMinutes(45),
            chunk =>
            {
                var clean = CleanProgressText(chunk);
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    terminal?.Report(clean);
                }

                progress?.Report(new LocalLlmGenerationProgress("Generating image with stable-diffusion.cpp...", null, null, null, null, null, null, null, false));
            },
            cancellationToken,
            resolvedOutputDirectory).ConfigureAwait(false);

        var images = FindGeneratedImageFiles(resolvedOutputDirectory, before, startedUtc);
        progress?.Report(new LocalLlmGenerationProgress("Image generation complete.", null, null, null, null, null, null, null, true));
        if (!result.Started)
        {
            return new LocalLlmImageGenerationResult(false, "stable-diffusion.cpp could not be started.", [], resolvedOutputDirectory);
        }

        if (result.ExitCode != 0)
        {
            var reason = FirstFailureLine(result.StandardError) ?? FirstFailureLine(result.StandardOutput) ?? $"stable-diffusion.cpp exited {result.ExitCode}.";
            return new LocalLlmImageGenerationResult(false, reason, images, resolvedOutputDirectory, result.StandardOutput);
        }

        return images.Count == 0
            ? new LocalLlmImageGenerationResult(false, "stable-diffusion.cpp finished, but no generated image file was detected.", [], resolvedOutputDirectory, result.StandardOutput)
            : new LocalLlmImageGenerationResult(true, $"Generated {images.Count:N0} image(s) with {modelId}.", images, resolvedOutputDirectory, result.StandardOutput);
    }

    private static bool IsDiffusersImageModel(string modelId)
    {
        return modelId.Contains('/', StringComparison.Ordinal) && !modelId.StartsWith("x/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStableDiffusionCppImageModel(string modelId)
    {
        return modelId.Contains("flux1", StringComparison.OrdinalIgnoreCase)
            || modelId.Contains("gguf", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, DateTime> SnapshotImageFiles(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        return Directory.EnumerateFiles(directory)
            .Where(path => ImageOutputExtensions.Contains(Path.GetExtension(path)))
            .ToDictionary(path => path, path => File.GetLastWriteTimeUtc(path), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> FindGeneratedImageFiles(
        string directory,
        IReadOnlyDictionary<string, DateTime> before,
        DateTime startedUtc)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory)
            .Where(path => ImageOutputExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .Where(file =>
                !before.TryGetValue(file.FullName, out var previousWriteUtc)
                    ? file.LastWriteTimeUtc >= startedUtc
                    : file.LastWriteTimeUtc > previousWriteUtc)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .ToArray();
    }

    private sealed record PythonModuleRequirement(string DisplayName, string ModuleName);

    private sealed record PythonExecutableResolution(bool Succeeded, string? Executable, string Status, bool IsManaged = false)
    {
        public static PythonExecutableResolution Ready(string executable, string status, bool isManaged)
        {
            return new PythonExecutableResolution(true, executable, status, isManaged);
        }

        public static PythonExecutableResolution Failed(string status)
        {
            return new PythonExecutableResolution(false, null, status);
        }
    }

}
