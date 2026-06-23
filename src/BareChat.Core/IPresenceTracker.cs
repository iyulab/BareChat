namespace BareChat.Core;

/// <summary>
/// Tracks which users currently have live connections. Single node uses an in-memory store;
/// scale-out synchronizes via a backplane (M2). Drives notification routing (live vs wake-up).
/// </summary>
public interface IPresenceTracker
{
    Task SetOnlineAsync(string userId, string connectionId, CancellationToken ct = default);
    Task SetOfflineAsync(string userId, string connectionId, CancellationToken ct = default);
    Task<bool> IsOnlineAsync(string userId, CancellationToken ct = default);
    Task<IReadOnlyCollection<string>> GetConnectionsAsync(string userId, CancellationToken ct = default);
}
