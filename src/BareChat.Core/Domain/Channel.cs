namespace BareChat.Core.Domain;

/// <summary>
/// A public conversation channel (Slack-style). All channels are public; visibility is not modeled.
/// Membership is a persisted subscription (see <see cref="ChannelMembership"/>), independent of r/w rights.
/// </summary>
public record Channel
{
    /// <summary>URL-safe slug, unique. e.g. "general", "line-a".</summary>
    public string ChannelId { get; init; } = string.Empty;

    /// <summary>Human-facing display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Creator user id. Seeded channels use "system".</summary>
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>Default channel cannot be deleted or left; new users auto-join it.</summary>
    public bool IsDefault { get; init; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
