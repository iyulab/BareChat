using System.Collections.Concurrent;
using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Storage;

/// <summary>In-memory <see cref="IChatStorageProvider"/> for tests and demos. Thread-safe.</summary>
public sealed class InMemoryChatStorageProvider : IChatStorageProvider
{
    private readonly ConcurrentDictionary<Guid, ChatMessage> _messages = new();

    public Task<ChatMessage> AddMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        _messages[message.MessageId] = message;
        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        string channelId, int limit = 50, DateTime? beforeUtc = null, CancellationToken ct = default)
    {
        var query = _messages.Values.Where(m => m.ChannelId == channelId);
        if (beforeUtc is { } before)
            query = query.Where(m => m.CreatedAtUtc < before);

        // newest `limit`, returned oldest-first for display
        var page = query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(limit)
            .OrderBy(m => m.CreatedAtUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<ChatMessage>>(page);
    }

    public Task<ChatMessage?> GetMessageAsync(Guid messageId, CancellationToken ct = default)
        => Task.FromResult(_messages.GetValueOrDefault(messageId));

    public Task<ChatMessage?> UpdateMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        if (!_messages.ContainsKey(message.MessageId))
            return Task.FromResult<ChatMessage?>(null);
        _messages[message.MessageId] = message;
        return Task.FromResult<ChatMessage?>(message);
    }

    public Task<int> CountMessagesSinceAsync(
        string channelId, DateTime afterUtc, string? excludeSenderId = null, CancellationToken ct = default)
    {
        var count = _messages.Values.Count(m =>
            m.ChannelId == channelId
            && m.CreatedAtUtc > afterUtc
            && !m.IsDeleted
            && (excludeSenderId is null || m.SenderId != excludeSenderId));
        return Task.FromResult(count);
    }

    public Task DeleteChannelMessagesAsync(string channelId, CancellationToken ct = default)
    {
        foreach (var (id, m) in _messages)
            if (m.ChannelId == channelId)
                _messages.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
