using System.Collections.Concurrent;
using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Storage;

/// <summary>In-memory <see cref="IChannelStore"/> for tests and demos. Thread-safe.</summary>
public sealed class InMemoryChannelStore : IChannelStore
{
    private readonly ConcurrentDictionary<string, Channel> _channels = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTime>> _members = new(StringComparer.Ordinal);
    // (channelId, userId) -> explicit last-read; absent until the user marks a channel read.
    private readonly ConcurrentDictionary<(string, string), DateTime> _lastRead = new();

    public Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Channel>>(_channels.Values.OrderBy(c => c.CreatedAtUtc).ToList());

    public Task<Channel?> GetChannelAsync(string channelId, CancellationToken ct = default)
        => Task.FromResult(_channels.GetValueOrDefault(channelId));

    public Task<Channel> CreateChannelAsync(Channel channel, CancellationToken ct = default)
    {
        if (!_channels.TryAdd(channel.ChannelId, channel))
            throw new InvalidOperationException($"Channel '{channel.ChannelId}' already exists.");
        _members.TryAdd(channel.ChannelId, new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal));
        return Task.FromResult(channel);
    }

    public Task DeleteChannelAsync(string channelId, CancellationToken ct = default)
    {
        _channels.TryRemove(channelId, out _);
        _members.TryRemove(channelId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Channel>> GetUserChannelsAsync(string userId, CancellationToken ct = default)
    {
        var result = _channels.Values
            .Where(c => _members.TryGetValue(c.ChannelId, out var m) && m.ContainsKey(userId))
            .OrderBy(c => c.CreatedAtUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<Channel>>(result);
    }

    public Task<IReadOnlyList<string>> GetMembersAsync(string channelId, CancellationToken ct = default)
    {
        var members = _members.TryGetValue(channelId, out var m)
            ? (IReadOnlyList<string>)m.Keys.ToList()
            : Array.Empty<string>();
        return Task.FromResult(members);
    }

    public Task JoinAsync(string channelId, string userId, CancellationToken ct = default)
    {
        if (!_channels.ContainsKey(channelId))
            throw new InvalidOperationException($"Channel '{channelId}' does not exist.");
        var set = _members.GetOrAdd(channelId, _ => new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal));
        set[userId] = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task LeaveAsync(string channelId, string userId, CancellationToken ct = default)
    {
        if (_members.TryGetValue(channelId, out var set))
            set.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> IsMemberAsync(string channelId, string userId, CancellationToken ct = default)
        => Task.FromResult(_members.TryGetValue(channelId, out var set) && set.ContainsKey(userId));

    public Task SetLastReadAtAsync(string channelId, string userId, DateTime readAtUtc, CancellationToken ct = default)
    {
        // No-op for non-members — read state belongs to a subscription.
        if (_members.TryGetValue(channelId, out var set) && set.ContainsKey(userId))
            _lastRead[(channelId, userId)] = readAtUtc;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, DateTime>> GetLastReadAtAsync(string userId, CancellationToken ct = default)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var (channelId, set) in _members)
        {
            if (!set.TryGetValue(userId, out var joinedAt)) continue;
            // Effective baseline: explicit last-read, else join time.
            result[channelId] = _lastRead.TryGetValue((channelId, userId), out var read) ? read : joinedAt;
        }
        return Task.FromResult<IReadOnlyDictionary<string, DateTime>>(result);
    }
}
