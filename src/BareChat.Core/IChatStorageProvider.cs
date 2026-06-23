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
}
