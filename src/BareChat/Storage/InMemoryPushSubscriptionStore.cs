using System.Collections.Concurrent;
using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Storage;

/// <summary>In-memory <see cref="IPushSubscriptionStore"/> (tests/demo). Keyed by endpoint.</summary>
public sealed class InMemoryPushSubscriptionStore : IPushSubscriptionStore
{
    private readonly ConcurrentDictionary<string, PushSubscription> _byEndpoint = new(StringComparer.Ordinal);

    public Task SaveAsync(PushSubscription subscription, CancellationToken ct = default)
    {
        _byEndpoint[subscription.Endpoint] = subscription;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string endpoint, CancellationToken ct = default)
    {
        _byEndpoint.TryRemove(endpoint, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<PushSubscription>> GetByUserAsync(string userId, CancellationToken ct = default)
    {
        IReadOnlyCollection<PushSubscription> result =
            _byEndpoint.Values.Where(s => string.Equals(s.UserId, userId, StringComparison.Ordinal)).ToList();
        return Task.FromResult(result);
    }
}
