namespace BareChat.Core.Domain;

/// <summary>A single message posted to a channel.</summary>
public record ChatMessage
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public string ChannelId { get; init; } = "general";
    public string SenderId { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;
    public string SenderAvatarUrl { get; init; } = string.Empty;
    public MessageType ContentType { get; init; } = MessageType.Text;
    public string Payload { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    // soft-delete / edit affordance (fields only in M1; editing UI is M3)
    public bool IsDeleted { get; init; }
    public DateTime? EditedAtUtc { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>Kind of message payload. <see cref="System"/> is used by programmatic publish (event feed).</summary>
public enum MessageType
{
    Text = 0,
    Image = 1,
    System = 2
}
