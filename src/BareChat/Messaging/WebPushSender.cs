using System.Net;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;
using DomainPushSubscription = BareChat.Core.Domain.PushSubscription;

namespace BareChat.Messaging;

/// <summary>
/// Thin abstraction over the actual Web Push transport so <see cref="WebPushChannel"/> stays unit-testable
/// and the third-party library is isolated to one adapter.
/// </summary>
public interface IWebPushSender
{
    /// <summary>Encrypts and delivers <paramref name="payload"/> to a subscription.
    /// Returns <c>false</c> when the push service reports the subscription is gone (404/410) and it should be pruned.</summary>
    Task<bool> SendAsync(DomainPushSubscription subscription, string payload, CancellationToken ct = default);
}

/// <summary>
/// <see cref="IWebPushSender"/> backed by Lib.Net.Http.WebPush. Holds one reusable <see cref="PushServiceClient"/>
/// and a VAPID authentication built from <see cref="PushOptions"/>. No-ops when push is not configured.
/// </summary>
public sealed class WebPushSender : IWebPushSender, IDisposable
{
    private readonly PushServiceClient _client = new();
    private readonly VapidAuthentication? _vapid;

    public WebPushSender(IOptions<BareChatOptions> options)
    {
        var push = options.Value.Push;
        if (push.Enabled)
        {
            _vapid = new VapidAuthentication(push.PublicKey!, push.PrivateKey!) { Subject = push.Subject };
            _client.DefaultAuthentication = _vapid;
        }
    }

    public async Task<bool> SendAsync(DomainPushSubscription subscription, string payload, CancellationToken ct = default)
    {
        if (_vapid is null) return true;   // push disabled → nothing delivered, nothing to prune

        var libSubscription = new PushSubscription
        {
            Endpoint = subscription.Endpoint,
            Keys = new Dictionary<string, string>
            {
                ["p256dh"] = subscription.P256dh,
                ["auth"] = subscription.Auth
            }
        };

        try
        {
            await _client.RequestPushMessageDeliveryAsync(libSubscription, new PushMessage(payload), ct).ConfigureAwait(false);
            return true;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            return false;   // subscription expired/unsubscribed → caller prunes
        }
    }

    public void Dispose() => _vapid?.Dispose();
}
