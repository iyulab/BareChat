using BareChat.Core.Domain;

namespace BareChat.Core;

/// <summary>
/// A "wake-up" notification transport, tried when the target user has no live connection.
/// Implementations: InAppChannel (SignalR, always), NativeBridgeChannel (WPF), WebPushChannel (PWA, M2).
/// </summary>
public interface INotificationChannel
{
    /// <summary>Attempts to wake the target user about a new message.</summary>
    Task NotifyAsync(string userId, ChatMessage message, CancellationToken ct = default);
}
