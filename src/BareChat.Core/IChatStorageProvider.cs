using BareChat.Core.Domain;

namespace BareChat.Core;

/// <summary>Persistence and retrieval of chat messages.</summary>
public interface IChatStorageProvider
{
    Task<ChatMessage> AddMessageAsync(ChatMessage message, CancellationToken ct = default);

    /// <summary>
    /// Most recent messages of a channel, newest-last. <paramref name="beforeUtc"/> pages older history.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        string channelId, int limit = 50, DateTime? beforeUtc = null, CancellationToken ct = default);

    Task<ChatMessage?> GetMessageAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// Persists changes to an existing message's mutable fields (payload, edit/delete markers) by id.
    /// Returns the stored message, or <c>null</c> if no message with that id exists. Immutable fields
    /// (sender, channel, creation time) are not changed.
    /// </summary>
    Task<ChatMessage?> UpdateMessageAsync(ChatMessage message, CancellationToken ct = default);

    /// <summary>
    /// Counts non-deleted messages in a channel created strictly after <paramref name="afterUtc"/>.
    /// Used to derive cross-session unread badges. Messages sent by <paramref name="excludeSenderId"/>
    /// (the asking user) are not counted — you are never "unread" on your own messages.
    /// </summary>
    Task<int> CountMessagesSinceAsync(
        string channelId, DateTime afterUtc, string? excludeSenderId = null, CancellationToken ct = default);
}
