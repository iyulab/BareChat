namespace BareChat.Core.Domain;

/// <summary>
/// A user's persisted subscription to a channel. Determines the user's channel list and
/// notification targeting only — it does NOT grant or restrict read/write rights (public-only model).
/// </summary>
public record ChannelMembership
{
    public string ChannelId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTime JoinedAtUtc { get; init; } = DateTime.UtcNow;
}
