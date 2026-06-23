using BareChat.Core;
using BareChat.Core.Domain;
using Microsoft.AspNetCore.SignalR;

namespace BareChat.Messaging;

/// <summary>
/// Always-on live delivery over SignalR. Sends the <c>ReceiveMessage</c> event to all of a user's
/// live connections (mapped by the default user-id provider = NameIdentifier claim).
/// </summary>
public sealed class InAppChannel : INotificationChannel
{
    private readonly IHubContext<ChatHub> _hub;

    public InAppChannel(IHubContext<ChatHub> hub) => _hub = hub;

    public Task NotifyAsync(string userId, ChatMessage message, CancellationToken ct = default)
        => _hub.Clients.User(userId).SendAsync("ReceiveMessage", message, ct);
}
