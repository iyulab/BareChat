using BareChat.Core.Domain;

namespace BareChat.Core;

/// <summary>
/// Persists Web Push subscriptions so the <c>WebPushChannel</c> can reach a user's devices when offline.
/// Subscriptions are keyed by endpoint; a user may have several. Infrastructure-agnostic (Core seam):
/// SQLite/InMemory implementations live in the BareChat package.
/// </summary>
public interface IPushSubscriptionStore
{
    /// <summary>Upserts a subscription by its endpoint (re-subscribe updates keys/owner).</summary>
    Task SaveAsync(PushSubscription subscription, CancellationToken ct = default);

    /// <summary>Removes a subscription by endpoint. No-op when absent (e.g. on 404/410 from the push service).</summary>
    Task RemoveAsync(string endpoint, CancellationToken ct = default);

    /// <summary>All current subscriptions for a user.</summary>
    Task<IReadOnlyCollection<PushSubscription>> GetByUserAsync(string userId, CancellationToken ct = default);
}
