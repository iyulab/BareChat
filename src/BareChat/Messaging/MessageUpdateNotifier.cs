using BareChat.Core;
using BareChat.Core.Domain;
using Microsoft.AspNetCore.SignalR;

namespace BareChat.Messaging;

/// <summary>
/// Live broadcast of an edited/deleted message to a channel's online members. Unlike a new message,
/// an update needs no wake-up delivery (no push for an edit) — offline members simply load the updated
/// row from history next time.
/// </summary>
public interface IMessageUpdateNotifier
{
    Task NotifyUpdatedAsync(ChatMessage updated, CancellationToken ct = default);
}

/// <summary>SignalR implementation: sends <c>MessageUpdated</c> to every member's live connections.</summary>
public sealed class MessageUpdateNotifier : IMessageUpdateNotifier
{
    private readonly IHubContext<ChatHub> _hub;
    private readonly IChannelStore _channels;

    public MessageUpdateNotifier(IHubContext<ChatHub> hub, IChannelStore channels)
    {
        _hub = hub;
        _channels = channels;
    }

    public async Task NotifyUpdatedAsync(ChatMessage updated, CancellationToken ct = default)
    {
        var members = await _channels.GetMembersAsync(updated.ChannelId, ct).ConfigureAwait(false);
        foreach (var member in members)
            await _hub.Clients.User(member).SendAsync("MessageUpdated", updated, ct).ConfigureAwait(false);
    }
}
