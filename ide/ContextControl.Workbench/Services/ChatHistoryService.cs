// CC-DESC: Persists lightweight local LLM chat sessions without storing attachment contents.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextControl.Workbench.Services;

public sealed class ChatHistoryService
{
    private const int MaxSessions = 40;
    private const int MaxMessagesPerSession = 120;
    private const int MaxSavedMessageCharacters = 220_000;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly string _historyPath;

    public ChatHistoryService(string contextControlRoot)
    {
        var root = string.IsNullOrWhiteSpace(contextControlRoot)
            ? AppContext.BaseDirectory
            : contextControlRoot;
        _historyPath = Path.Combine(root, ".ccWorkbench.chat-history.json");
    }

    public string HistoryPath => _historyPath;

    public ChatHistoryDocument Load()
    {
        if (!File.Exists(_historyPath))
        {
            return new ChatHistoryDocument();
        }

        try
        {
            return JsonSerializer.Deserialize<ChatHistoryDocument>(File.ReadAllText(_historyPath), JsonOptions)
                ?? new ChatHistoryDocument();
        }
        catch
        {
            return new ChatHistoryDocument();
        }
    }

    public void Save(ChatHistoryDocument document)
    {
        var parent = Path.GetDirectoryName(_historyPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var cleanDocument = new ChatHistoryDocument
        {
            SelectedSessionId = document.SelectedSessionId,
            Sessions = document.Sessions
                .Where(session => !string.IsNullOrWhiteSpace(session.Id))
                .OrderByDescending(session => session.UpdatedUtc)
                .Take(MaxSessions)
                .Select(CleanSession)
                .ToList()
        };

        File.WriteAllText(_historyPath, JsonSerializer.Serialize(cleanDocument, JsonOptions) + Environment.NewLine, Utf8NoBom);
    }

    private static ChatHistorySessionData CleanSession(ChatHistorySessionData session)
    {
        return new ChatHistorySessionData
        {
            Id = session.Id,
            Title = TrimText(session.Title, 80),
            CreatedUtc = session.CreatedUtc == default ? DateTime.UtcNow : session.CreatedUtc,
            UpdatedUtc = session.UpdatedUtc == default ? DateTime.UtcNow : session.UpdatedUtc,
            Messages = session.Messages
                .TakeLast(MaxMessagesPerSession)
                .Select(CleanMessage)
                .ToList()
        };
    }

    private static ChatHistoryMessageData CleanMessage(ChatHistoryMessageData message)
    {
        return new ChatHistoryMessageData
        {
            Role = TrimText(message.Role, 24),
            Text = TrimLargeMessage(message.Text),
            ModelId = TrimText(message.ModelId, 96),
            Phase = TrimText(message.Phase, 48),
            CapsuleSummary = TrimText(message.CapsuleSummary, 160),
            CreatedUtc = message.CreatedUtc == default ? DateTime.UtcNow : message.CreatedUtc,
            Stats = message.Stats,
            Attachments = message.Attachments
                .Take(24)
                .Select(attachment => new ChatHistoryAttachmentData
                {
                    Label = TrimText(attachment.Label, 120),
                    Path = TrimText(attachment.Path, 520),
                    Kind = TrimText(attachment.Kind, 32),
                    IncludeInPrompt = attachment.IncludeInPrompt
                })
                .ToList()
        };
    }

    private static string TrimLargeMessage(string? value)
    {
        var clean = value ?? "";
        if (clean.Length <= MaxSavedMessageCharacters)
        {
            return clean;
        }

        const string marker = "\n\n[... chat message truncated for history size ...]\n\n";
        var headLength = MaxSavedMessageCharacters / 2;
        var tailLength = MaxSavedMessageCharacters - headLength - marker.Length;
        return clean[..headLength] + marker + clean[^tailLength..];
    }

    private static string TrimText(string? value, int maxLength)
    {
        var clean = value ?? "";
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class ChatHistoryDocument
{
    public string? SelectedSessionId { get; set; }
    public List<ChatHistorySessionData> Sessions { get; set; } = [];
}

public sealed class ChatHistorySessionData
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public List<ChatHistoryMessageData> Messages { get; set; } = [];
}

public sealed class ChatHistoryMessageData
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string Phase { get; set; } = "";
    public string CapsuleSummary { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public LocalLlmUsageStats? Stats { get; set; }
    public List<ChatHistoryAttachmentData> Attachments { get; set; } = [];
}

public sealed class ChatHistoryAttachmentData
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool IncludeInPrompt { get; set; }
}
