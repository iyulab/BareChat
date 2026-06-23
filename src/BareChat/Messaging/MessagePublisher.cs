using BareChat.Core;
using BareChat.Core.Domain;
using Microsoft.Extensions.Logging;

namespace BareChat.Messaging;

/// <summary>Persists a message and delivers it to its channel's members. Independent of SignalR (testable).</summary>
public interface IMessagePublisher
{
    Task<ChatMessage> PublishAsync(ChatMessage message, CancellationToken ct = default);
}

/// <summary>
/// Default pipeline: store → for each channel member, route by presence. Online members get live delivery
/// via the in-app channel; offline members are tried on every registered wake-up channel
/// (<see cref="IWakeUpNotificationChannel"/>: WebPush/NativeBridge). Wake-up delivery is best-effort and
/// per-channel isolated — a dead push transport never fails the send or blocks other channels/members.
/// </summary>
public sealed class MessagePublisher : IMessagePublisher
{
    private readonly IChatStorageProvider _storage;
    private readonly IChannelStore _channels;
    private readonly IPresenceTracker _presence;
    private readonly INotificationChannel _live;
    private readonly IReadOnlyList<IWakeUpNotificationChannel> _wakeUp;
    private readonly ILogger<MessagePublisher> _logger;

    public MessagePublisher(
        IChatStorageProvider storage,
        IChannelStore channels,
        IPresenceTracker presence,
        INotificationChannel live,
        IEnumerable<IWakeUpNotificationChannel> wakeUpChannels,
        ILogger<MessagePublisher> logger)
    {
        _storage = storage;
        _channels = channels;
        _presence = presence;
        _live = live;
        _wakeUp = wakeUpChannels as IReadOnlyList<IWakeUpNotificationChannel> ?? wakeUpChannels.ToList();
        _logger = logger;
    }

    public async Task<ChatMessage> PublishAsync(ChatMessage message, CancellationToken ct = default)
    {
        var saved = await _storage.AddMessageAsync(message, ct).ConfigureAwait(false);

        var members = await _channels.GetMembersAsync(message.ChannelId, ct).ConfigureAwait(false);
        foreach (var member in members)
        {
            if (await _presence.IsOnlineAsync(member, ct).ConfigureAwait(false))
                await _live.NotifyAsync(member, saved, ct).ConfigureAwait(false);
            else
                await WakeUpAsync(member, saved, ct).ConfigureAwait(false);
        }

        return saved;
    }

    private async Task WakeUpAsync(string member, ChatMessage message, CancellationToken ct)
    {
        foreach (var channel in _wakeUp)
        {
            try
            {
                await channel.NotifyAsync(member, message, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Best-effort: one transport's failure must not abort the publish or the other channels.
                _logger.LogWarning(ex, "Wake-up channel {Channel} failed for user {User}",
                    channel.GetType().Name, member);
            }
        }
    }
}
