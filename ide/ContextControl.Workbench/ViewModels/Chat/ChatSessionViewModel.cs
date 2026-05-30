// CC-DESC: Holds lightweight chat session metadata and lazily rebuilds selected chat messages.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class ChatSessionViewModel : ObservableObject
{
    private readonly List<ChatHistoryMessageData> _messages;
    private readonly List<ChatHistoryAttachmentData> _pendingAttachments;
    private string _title;
    private string _draftPromptText;
    private DateTime _updatedUtc;
    private bool _isActive;

    public ChatSessionViewModel(ChatHistorySessionData data)
    {
        Id = string.IsNullOrWhiteSpace(data.Id) ? Guid.NewGuid().ToString("N") : data.Id;
        _title = string.IsNullOrWhiteSpace(data.Title) ? "New chat" : data.Title;
        CreatedUtc = data.CreatedUtc == default ? DateTime.UtcNow : data.CreatedUtc;
        _updatedUtc = data.UpdatedUtc == default ? CreatedUtc : data.UpdatedUtc;
        _draftPromptText = data.DraftPromptText ?? "";
        _pendingAttachments = data.PendingAttachments?.ToList() ?? [];
        _messages = (data.Messages ?? [])
            .OrderBy(message => message.CreatedUtc)
            .ToList();
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

    public string DraftPromptText => _draftPromptText;

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
            .Select(CreateMessage)
            .ToArray();
    }

    public LocalLlmChatMessageViewModel CreateMessageAt(int index)
    {
        return CreateMessage(_messages[index]);
    }

    public string FindLastPatchText()
    {
        for (var index = _messages.Count - 1; index >= 0; index--)
        {
            var message = CreateMessage(_messages[index]);
            var patch = message.Snippets.LastOrDefault(snippet => snippet.IsPatch);
            if (patch is not null)
            {
                return patch.Text;
            }
        }

        return "";
    }

    public IReadOnlyList<ContextControlAttachmentViewModel> CreatePendingAttachments()
    {
        return _pendingAttachments
            .Select(CreateAttachment)
            .ToArray();
    }

    public void SetPendingAttachments(IEnumerable<ContextControlAttachmentViewModel> attachments)
    {
        _pendingAttachments.Clear();
        _pendingAttachments.AddRange((attachments ?? [])
            .Select(attachment => new ChatHistoryAttachmentData
            {
                Label = attachment.Label,
                Path = attachment.Path,
                Kind = attachment.Kind,
                IncludeInPrompt = attachment.IncludeInPrompt
            }));
    }

    public bool HasPendingAttachments => _pendingAttachments.Count > 0;

    public void SetDraftPromptText(string? text)
    {
        _draftPromptText = text ?? "";
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
            DraftPromptText = _draftPromptText,
            PendingAttachments = _pendingAttachments.ToList(),
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
            data.CreatedUtc == default ? DateTime.UtcNow : data.CreatedUtc,
            data.DiagnosticPrompt);
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
            DiagnosticPrompt = message.DiagnosticPrompt,
            CreatedUtc = message.CreatedUtc,
            Stats = message.Stats,
            Attachments = ShouldPersistMessageAttachments(message)
                ? message.AttachedFiles.Select(attachment => new ChatHistoryAttachmentData
                {
                    Label = attachment.Label,
                    Path = attachment.Path,
                    Kind = attachment.Kind,
                    IncludeInPrompt = attachment.IncludeInPrompt
                }).ToList()
                : []
        };
    }

    private static bool ShouldPersistMessageAttachments(LocalLlmChatMessageViewModel message)
    {
        return message.HasSentAttachments
            || (!message.IsUser
                && message.AttachedFiles.Any(attachment => string.Equals(attachment.Kind, "image", StringComparison.OrdinalIgnoreCase)));
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
