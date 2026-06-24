namespace BareChat.Core.Domain;

/// <summary>
/// A conversation channel (Slack-style). Channels are public by default — anyone may discover and join.
/// A <see cref="IsPrivate"/> channel is hidden from non-members and gated by membership (see
/// <see cref="IChatAuthorizationProvider"/>). Membership is a persisted subscription
/// (see <see cref="ChannelMembership"/>); for public channels it is independent of r/w rights, while for
/// private channels it <em>is</em> the access grant.
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

    /// <summary>
    /// Private channels are not discoverable by non-members and cannot be self-joined — the creator adds
    /// members explicitly. Read/write is restricted to members. Default channels are never private.
    /// </summary>
    public bool IsPrivate { get; init; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
