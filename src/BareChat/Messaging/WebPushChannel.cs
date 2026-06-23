using System.Text.Json;
using BareChat.Core;
using BareChat.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BareChat.Messaging;

/// <summary>
/// Wake-up transport for the PWA: when an offline member has stored push subscriptions, encrypts a small
/// payload and delivers it to each via Web Push. Subscriptions the push service reports as gone are pruned.
/// No-ops when push is unconfigured, so it's safe to always register.
/// </summary>
public sealed class WebPushChannel : IWakeUpNotificationChannel
{
    private readonly IPushSubscriptionStore _store;
    private readonly IWebPushSender _sender;
    private readonly BareChatOptions _options;
    private readonly ILogger<WebPushChannel> _logger;

    public WebPushChannel(
        IPushSubscriptionStore store,
        IWebPushSender sender,
        IOptions<BareChatOptions> options,
        ILogger<WebPushChannel> logger)
    {
        _store = store;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyAsync(string userId, ChatMessage message, CancellationToken ct = default)
    {
        if (!_options.Push.Enabled) return;

        var subscriptions = await _store.GetByUserAsync(userId, ct).ConfigureAwait(false);
        if (subscriptions.Count == 0) return;

        var payload = BuildPayload(message);
        foreach (var subscription in subscriptions)
        {
            bool alive;
            try
            {
                alive = await _sender.SendAsync(subscription, payload, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web push delivery failed for user {User}", userId);
                continue;
            }

            if (!alive)
                await _store.RemoveAsync(subscription.Endpoint, ct).ConfigureAwait(false);
        }
    }

    private static string BuildPayload(ChatMessage message)
    {
        var preview = message.ContentType == MessageType.Image ? "[image]" : message.Payload;
        return JsonSerializer.Serialize(new
        {
            title = string.IsNullOrEmpty(message.SenderName) ? message.SenderId : message.SenderName,
            body = preview,
            channelId = message.ChannelId,
            tag = message.ChannelId
        });
    }
}
