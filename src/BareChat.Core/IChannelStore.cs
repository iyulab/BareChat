using BareChat.Core.Domain;

namespace BareChat.Core;

/// <summary>
/// Persistence for channels and memberships (subscriptions). Separated from message storage
/// (single responsibility); the SQLite implementations share one <c>chat.db</c>.
/// </summary>
public interface IChannelStore
{
    /// <summary>All channels (public-only model — everyone can discover any channel).</summary>
    Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken ct = default);

    Task<Channel?> GetChannelAsync(string channelId, CancellationToken ct = default);

    /// <summary>Creates a channel. Throws if the slug already exists.</summary>
    Task<Channel> CreateChannelAsync(Channel channel, CancellationToken ct = default);

    /// <summary>Deletes a channel and its memberships. Caller enforces "creator only" and "not default".</summary>
    Task DeleteChannelAsync(string channelId, CancellationToken ct = default);

    /// <summary>Channels the user is subscribed to (their channel list).</summary>
    Task<IReadOnlyList<Channel>> GetUserChannelsAsync(string userId, CancellationToken ct = default);

    /// <summary>User ids subscribed to a channel (notification targeting).</summary>
    Task<IReadOnlyList<string>> GetMembersAsync(string channelId, CancellationToken ct = default);

    Task JoinAsync(string channelId, string userId, CancellationToken ct = default);

    Task LeaveAsync(string channelId, string userId, CancellationToken ct = default);

    Task<bool> IsMemberAsync(string channelId, string userId, CancellationToken ct = default);
}
