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
    public async Task<LocalLlmChatResult> SendChatAsync(
        string modelId,
        string message,
        IReadOnlyList<string> attachmentPaths,
        CancellationToken cancellationToken = default)
    {
        return await SendChatAsync(modelId, message, attachmentPaths, null, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalLlmChatResult> SendChatAsync(
        string modelId,
        string message,
        IReadOnlyList<string> attachmentPaths,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        var preparedMessage = BuildChatMessage(message, attachmentPaths);
        return await SendChatAsync(
            new LocalLlmRequest(modelId, preparedMessage, "chat", attachmentPaths.Select(path => Path.GetFileName(path) ?? path).ToArray()),
            progress,
            terminal,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalLlmChatResult> SendChatAsync(
        LocalLlmRequest request,
        IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            return new LocalLlmChatResult(false, "Choose an installed local model first.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return new LocalLlmChatResult(false, "Write a chat message first.");
        }

        var suppressThinking = ShouldSuppressThinkingForPhase(request.Phase);
        IReadOnlyList<string>? encodedImages = null;
        if (request.ImagePaths is { Count: > 0 })
        {
            try
            {
                encodedImages = await EncodeImageAttachmentsAsync(request.ImagePaths, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new LocalLlmChatResult(false, $"Could not read image attachment: {ex.Message}");
            }

            if (encodedImages.Count == 0)
            {
                return new LocalLlmChatResult(false, "No readable image attachments were found.");
            }
        }

        var chatRequest = new OllamaChatRequest(
            request.ModelId,
            [new OllamaChatMessage("user", request.Prompt.Trim(), encodedImages)],
            Stream: true,
            Options: request.ContextWindowTokens is > 0
                ? new OllamaChatOptions(request.ContextWindowTokens.Value)
                : null,
            Think: suppressThinking ? false : null);

        try
        {
            var thinkFlag = suppressThinking ? " --think false" : "";
            terminal?.Report(request.ContextWindowTokens is > 0
                ? $"> ollama chat {chatRequest.Model} --num-ctx {request.ContextWindowTokens.Value}{thinkFlag}"
                : $"> ollama chat {chatRequest.Model}{thinkFlag}");
            progress?.Report(new LocalLlmGenerationProgress("Loading model and preparing prompt...", null, null, null, null, null, null, null, false));
            using var http = CreateHttpClient(request.ContextWindowTokens is > 8192 ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(10));
            using var content = new StringContent(JsonSerializer.Serialize(chatRequest, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(new Uri(OllamaBaseUri, "/api/chat"), content, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new LocalLlmChatResult(false, $"Ollama returned {(int)response.StatusCode}: {FirstLine(responseText)}");
            }

            var answerBuilder = new StringBuilder();
            OllamaChatResponse? finalResponse = null;
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(responseStream);
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions);
                var delta = chatResponse?.Message?.Content;
                if (!string.IsNullOrEmpty(delta))
                {
                    answerBuilder.Append(delta);
                }

                progress?.Report(new LocalLlmGenerationProgress(
                    chatResponse?.Done == true ? "Generation complete." : "Generating local response...",
                    delta,
                    chatResponse?.PromptEvalCount,
                    chatResponse?.EvalCount,
                    chatResponse?.TotalDuration,
                    chatResponse?.LoadDuration,
                    chatResponse?.PromptEvalDuration,
                    chatResponse?.EvalDuration,
                    chatResponse?.Done == true));

                if (chatResponse?.Done == true)
                {
                    finalResponse = chatResponse;
                    terminal?.Report(BuildGenerationSummary(chatResponse));
                }
            }

            var answer = answerBuilder.ToString().Trim();
            var stats = finalResponse is null ? null : BuildUsageStats(finalResponse);
            return string.IsNullOrWhiteSpace(answer)
                ? new LocalLlmChatResult(false, "Ollama returned an empty response.")
                : new LocalLlmChatResult(true, $"Local chat completed with {chatRequest.Model}.", answer, stats);
        }
        catch (HttpRequestException ex)
        {
            return new LocalLlmChatResult(false, $"Ollama is not reachable at {OllamaBaseUri}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new LocalLlmChatResult(false, "Local chat timed out.");
        }
    }

    private static bool ShouldSuppressThinkingForPhase(string? phase)
    {
        var clean = (phase ?? "").Trim();
        return !string.IsNullOrWhiteSpace(clean)
            && !clean.Equals("chat", StringComparison.OrdinalIgnoreCase);
    }

}
