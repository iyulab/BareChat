using BareChat.Core;
using BareChat.Core.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BareChat.Endpoints;

/// <summary>
/// Web Push subscription management: hand the client the VAPID public key and store/remove its browser
/// subscription. Actual push delivery is done by the <c>WebPushChannel</c> wake-up channel.
/// </summary>
public static class PushEndpoints
{
    public static void MapPush(this IEndpointRouteBuilder app, string prefix)
    {
        var api = app.MapGroup($"{prefix}/api/push");

        // The client needs this to call pushManager.subscribe(). 404 when push isn't configured.
        api.MapGet("/vapid-public-key", (IOptions<BareChatOptions> opt) =>
        {
            var push = opt.Value.Push;
            return push.Enabled ? Results.Ok(new { publicKey = push.PublicKey }) : Results.NotFound();
        });

        api.MapPost("/subscriptions", async (
            HttpContext http, IChatAuthProvider auth, IPushSubscriptionStore store, PushSubscriptionRequest req) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Endpoint) ||
                string.IsNullOrWhiteSpace(req.Keys?.P256dh) ||
                string.IsNullOrWhiteSpace(req.Keys?.Auth))
                return Results.BadRequest(new { error = "endpoint and keys.p256dh/auth are required." });

            await store.SaveAsync(new PushSubscription
            {
                Endpoint = req.Endpoint,
                P256dh = req.Keys.P256dh,
                Auth = req.Keys.Auth,
                UserId = user.UserId
            });
            return Results.NoContent();
        });

        api.MapDelete("/subscriptions", async (
            HttpContext http, IChatAuthProvider auth, IPushSubscriptionStore store, [FromBody] UnsubscribeRequest req) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Endpoint)) return Results.BadRequest(new { error = "endpoint is required." });

            await store.RemoveAsync(req.Endpoint);
            return Results.NoContent();
        });
    }
}

/// <summary>Native Web Push subscription shape: <c>{ endpoint, keys: { p256dh, auth } }</c>.</summary>
public sealed record PushSubscriptionRequest(string Endpoint, PushKeys? Keys);
public sealed record PushKeys(string P256dh, string Auth);
public sealed record UnsubscribeRequest(string Endpoint);
