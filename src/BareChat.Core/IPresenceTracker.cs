namespace BareChat.Core;

/// <summary>
/// Tracks which users currently have live connections. Drives notification routing: the publisher delivers
/// live to online members and falls back to wake-up channels for offline ones.
/// <para><b>Scale-out:</b> the default implementation is per-node (in-memory). With a SignalR backplane,
/// live delivery (<c>Clients.User</c>) already fans out across nodes, but presence does not — a user
/// connected only to another node reads as offline locally, which would route a spurious wake-up push.
/// For correct multi-node behavior, replace this with a distributed implementation (e.g. Redis-backed)
/// alongside the backplane. This interface is the seam for that.</para>
/// </summary>
public interface IPresenceTracker
{
    Task SetOnlineAsync(string userId, string connectionId, CancellationToken ct = default);
    Task SetOfflineAsync(string userId, string connectionId, CancellationToken ct = default);
    Task<bool> IsOnlineAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyCollection<string>> GetConnectionsAsync(string userId, CancellationToken ct = default);
}
