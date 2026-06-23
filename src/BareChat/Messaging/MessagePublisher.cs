using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Messaging;

/// <summary>Persists a message and delivers it to its channel's members. Independent of SignalR (testable).</summary>
public interface IMessagePublisher
{
    Task<ChatMessage> PublishAsync(ChatMessage message, CancellationToken ct = default);
}

/// <summary>
/// Default pipeline: store → for each channel member, live-deliver via the in-app channel when online.
/// Offline members get nothing in M1; wake-up channels (WebPush/NativeBridge) extend this routing later.
/// </summary>
public sealed class MessagePublisher : IMessagePublisher
{
    private readonly IChatStorageProvider _storage;
    private readonly IChannelStore _channels;
    private readonly IPresenceTracker _presence;
    private readonly INotificationChannel _live;

    public MessagePublisher(
        IChatStorageProvider storage,
        IChannelStore channels,
        IPresenceTracker presence,
        INotificationChannel live)
    {
        _storage = storage;
        _channels = channels;
        _presence = presence;
        _live = live;
    }

    public async Task<ChatMessage> PublishAsync(ChatMessage message, CancellationToken ct = default)
    {
        var saved = await _storage.AddMessageAsync(message, ct).ConfigureAwait(false);

        var members = await _channels.GetMembersAsync(message.ChannelId, ct).ConfigureAwait(false);
        foreach (var member in members)
        {
            if (await _presence.IsOnlineAsync(member, ct).ConfigureAwait(false))
                await _live.NotifyAsync(member, saved, ct).ConfigureAwait(false);
            // offline → wake-up channels (M2+)
        }

        return saved;
    }
}
