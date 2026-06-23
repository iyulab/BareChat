using System.Collections.Concurrent;
using BareChat.Core;

namespace BareChat.Presence;

/// <summary>
/// In-memory <see cref="IPresenceTracker"/>: maps userId → set of live connection ids.
/// Single-node only; scale-out replaces this with a backplane-synced tracker (M2).
/// </summary>
public sealed class InMemoryPresenceTracker : IPresenceTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connections = new(StringComparer.Ordinal);

    public Task SetOnlineAsync(string userId, string connectionId, CancellationToken ct = default)
    {
        var set = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        set[connectionId] = 0;
        return Task.CompletedTask;
    }

    public Task SetOfflineAsync(string userId, string connectionId, CancellationToken ct = default)
    {
        if (_connections.TryGetValue(userId, out var set))
        {
            set.TryRemove(connectionId, out _);
            if (set.IsEmpty)
                _connections.TryRemove(userId, out _);
        }
        return Task.CompletedTask;
    }

    public Task<bool> IsOnlineAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(_connections.TryGetValue(userId, out var set) && !set.IsEmpty);

    public Task<IReadOnlyCollection<string>> GetConnectionsAsync(string userId, CancellationToken ct = default)
    {
        IReadOnlyCollection<string> result = _connections.TryGetValue(userId, out var set)
            ? set.Keys.ToList()
            : Array.Empty<string>();
        return Task.FromResult(result);
    }
}
