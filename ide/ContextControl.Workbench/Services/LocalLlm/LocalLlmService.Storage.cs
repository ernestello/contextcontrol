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
    public static string DefaultOllamaModelsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");

    public static string ResolveOllamaModelsDirectory(string? configuredDirectory)
    {
        var configured = NormalizeModelsDirectory(configuredDirectory);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var userEnvironment = NormalizeModelsDirectory(Environment.GetEnvironmentVariable(
            OllamaModelsEnvironmentVariable,
            EnvironmentVariableTarget.User));
        if (!string.IsNullOrWhiteSpace(userEnvironment))
        {
            return userEnvironment;
        }

        var processEnvironment = NormalizeModelsDirectory(Environment.GetEnvironmentVariable(OllamaModelsEnvironmentVariable));
        return string.IsNullOrWhiteSpace(processEnvironment)
            ? DefaultOllamaModelsDirectory
            : processEnvironment;
    }

    public static LocalLlmStorageConfigurationResult ConfigureOllamaModelsDirectory(string? directory)
    {
        var resolved = ResolveOllamaModelsDirectory(directory);
        try
        {
            System.IO.Directory.CreateDirectory(resolved);
            Environment.SetEnvironmentVariable(OllamaModelsEnvironmentVariable, resolved, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(OllamaModelsEnvironmentVariable, resolved, EnvironmentVariableTarget.Process);
            return new LocalLlmStorageConfigurationResult(
                true,
                resolved,
                $"Ollama model storage set to {resolved}. Restart Ollama if it is already running.");
        }
        catch (Exception ex)
        {
            return new LocalLlmStorageConfigurationResult(
                false,
                resolved,
                $"Could not set Ollama model storage: {ex.Message}");
        }
    }

    public static void ApplyOllamaModelsDirectoryToProcess(string? directory)
    {
        var resolved = ResolveOllamaModelsDirectory(directory);
        Environment.SetEnvironmentVariable(OllamaModelsEnvironmentVariable, resolved, EnvironmentVariableTarget.Process);
    }

    private static LocalLlmCatalogModel CatalogModel(
        string id,
        string displayName,
        string releaseDate,
        string iconSource,
        string downloadSize,
        string license,
        string minimumRequirement,
        string advertisedContext,
        string comfortableContext,
        string sourceBudget,
        string expectedSpeed,
        string practicalUse,
        double minimumVramGiB,
        double recommendedVramGiB,
        bool worksOnCpu)
    {
        return new LocalLlmCatalogModel(
            id,
            displayName,
            releaseDate,
            iconSource,
            downloadSize,
            license,
            minimumRequirement,
            advertisedContext,
            comfortableContext,
            sourceBudget,
            expectedSpeed,
            practicalUse,
            minimumVramGiB,
            recommendedVramGiB,
            worksOnCpu,
            $"ollama pull {id}");
    }

    private static string NormalizeModelsDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }

}
