// CC-DESC: Holds lightweight chat session metadata and lazily rebuilds selected chat messages.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class ChatSessionViewModel : ObservableObject
{
    private readonly List<ChatHistoryMessageData> _messages;
    private string _title;
    private DateTime _updatedUtc;
    private bool _isActive;

    public ChatSessionViewModel(ChatHistorySessionData data)
    {
        Id = string.IsNullOrWhiteSpace(data.Id) ? Guid.NewGuid().ToString("N") : data.Id;
        _title = string.IsNullOrWhiteSpace(data.Title) ? "New chat" : data.Title;
        CreatedUtc = data.CreatedUtc == default ? DateTime.UtcNow : data.CreatedUtc;
        _updatedUtc = data.UpdatedUtc == default ? CreatedUtc : data.UpdatedUtc;
        _messages = data.Messages?.ToList() ?? [];
    }

    public string Id { get; }
    public DateTime CreatedUtc { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public DateTime UpdatedUtc
    {
        get => _updatedUtc;
        private set
        {
            if (SetProperty(ref _updatedUtc, value))
            {
                OnPropertyChanged(nameof(UpdatedLabel));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public int MessageCount => _messages.Count;

    public string UpdatedLabel => UpdatedUtc.ToLocalTime().ToString("MMM d HH:mm");

    public string Summary => $"{MessageCount:N0} msg - {UpdatedLabel}";

    public static ChatSessionViewModel CreateNew()
    {
        var now = DateTime.UtcNow;
        return new ChatSessionViewModel(new ChatHistorySessionData
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "New chat",
            CreatedUtc = now,
            UpdatedUtc = now
        });
    }

    public IReadOnlyList<LocalLlmChatMessageViewModel> CreateMessages()
    {
        return _messages
            .OrderBy(message => message.CreatedUtc)
            .Select(CreateMessage)
            .ToArray();
    }

    public void Append(LocalLlmChatMessageViewModel message)
    {
        _messages.Add(ToData(message));
        UpdatedUtc = message.CreatedUtc;
        if (string.Equals(Title, "New chat", StringComparison.OrdinalIgnoreCase) && message.IsUser)
        {
            Title = BuildTitle(message.RawText);
        }

        OnPropertyChanged(nameof(MessageCount));
        OnPropertyChanged(nameof(Summary));
    }

    public ChatHistorySessionData ToData()
    {
        return new ChatHistorySessionData
        {
            Id = Id,
            Title = Title,
            CreatedUtc = CreatedUtc,
            UpdatedUtc = UpdatedUtc,
            Messages = _messages.ToList()
        };
    }

    private static LocalLlmChatMessageViewModel CreateMessage(ChatHistoryMessageData data)
    {
        return new LocalLlmChatMessageViewModel(
            data.Role,
            data.Text,
            data.ModelId,
            data.Phase,
            data.CapsuleSummary,
            data.Stats,
            data.Attachments.Select(CreateAttachment).ToArray(),
            data.CreatedUtc == default ? DateTime.UtcNow : data.CreatedUtc);
    }

    private static ContextControlAttachmentViewModel CreateAttachment(ChatHistoryAttachmentData data)
    {
        var attachment = new ContextControlAttachmentViewModel(data.Label, data.Path, data.Kind)
        {
            IncludeInPrompt = data.IncludeInPrompt
        };
        return attachment;
    }

    private static ChatHistoryMessageData ToData(LocalLlmChatMessageViewModel message)
    {
        return new ChatHistoryMessageData
        {
            Role = message.Role,
            Text = message.RawText,
            ModelId = message.ModelId,
            Phase = message.Phase,
            CapsuleSummary = message.CapsuleSummary,
            CreatedUtc = message.CreatedUtc,
            Stats = message.Stats,
            Attachments = message.AttachedFiles.Select(attachment => new ChatHistoryAttachmentData
            {
                Label = attachment.Label,
                Path = attachment.Path,
                Kind = attachment.Kind,
                IncludeInPrompt = attachment.IncludeInPrompt
            }).ToList()
        };
    }

    private static string BuildTitle(string text)
    {
        var clean = string.Join(
                " ",
                (text ?? "")
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "New chat";
        }

        return clean.Length <= 54 ? clean : clean[..54] + "...";
    }
}
