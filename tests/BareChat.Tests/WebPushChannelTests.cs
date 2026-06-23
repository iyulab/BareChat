using BareChat.Core;
using BareChat.Core.Domain;
using BareChat.Messaging;
using BareChat.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BareChat.Tests;

public class WebPushChannelTests
{
    private sealed class FakeSender : IWebPushSender
    {
        public List<(string Endpoint, string Payload)> Sent { get; } = new();
        public Func<string, bool>? AliveByEndpoint { get; set; }
        public bool Throw { get; set; }

        public Task<bool> SendAsync(PushSubscription subscription, string payload, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("transient");
            Sent.Add((subscription.Endpoint, payload));
            return Task.FromResult(AliveByEndpoint?.Invoke(subscription.Endpoint) ?? true);
        }
    }

    private static IOptions<BareChatOptions> Options(bool enabled)
    {
        var o = new BareChatOptions();
        if (enabled) { o.Push.PublicKey = "pub"; o.Push.PrivateKey = "priv"; }
        return Microsoft.Extensions.Options.Options.Create(o);
    }

    private static async Task<InMemoryPushSubscriptionStore> StoreWith(params (string endpoint, string user)[] subs)
    {
        var store = new InMemoryPushSubscriptionStore();
        foreach (var (e, u) in subs)
            await store.SaveAsync(new PushSubscription { Endpoint = e, P256dh = "p", Auth = "a", UserId = u });
        return store;
    }

    private static WebPushChannel Channel(IPushSubscriptionStore store, IWebPushSender sender, bool enabled) =>
        new(store, sender, Options(enabled), NullLogger<WebPushChannel>.Instance);

    [Fact]
    public async Task Disabled_push_sends_nothing()
    {
        var store = await StoreWith(("https://p/1", "alice"));
        var sender = new FakeSender();
        await Channel(store, sender, enabled: false)
            .NotifyAsync("alice", new ChatMessage { ChannelId = "general", SenderName = "Bob", Payload = "hi" });
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Sends_to_each_of_a_users_subscriptions_with_payload()
    {
        var store = await StoreWith(("https://p/1", "alice"), ("https://p/2", "alice"), ("https://p/3", "bob"));
        var sender = new FakeSender();
        await Channel(store, sender, enabled: true)
            .NotifyAsync("alice", new ChatMessage { ChannelId = "general", SenderName = "Bob", Payload = "hi" });

        Assert.Equal(2, sender.Sent.Count);                       // only alice's two subs
        Assert.All(sender.Sent, s => Assert.Contains("general", s.Payload));
        Assert.All(sender.Sent, s => Assert.Contains("Bob", s.Payload));
    }

    [Fact]
    public async Task Gone_subscriptions_are_pruned()
    {
        var store = await StoreWith(("https://p/gone", "alice"), ("https://p/ok", "alice"));
        var sender = new FakeSender { AliveByEndpoint = e => e != "https://p/gone" };
        await Channel(store, sender, enabled: true)
            .NotifyAsync("alice", new ChatMessage { ChannelId = "general", Payload = "hi" });

        var remaining = await store.GetByUserAsync("alice");
        Assert.Equal("https://p/ok", Assert.Single(remaining).Endpoint);
    }

    [Fact]
    public async Task Transient_sender_failure_is_isolated_and_not_pruned()
    {
        var store = await StoreWith(("https://p/1", "alice"));
        var sender = new FakeSender { Throw = true };
        await Channel(store, sender, enabled: true)
            .NotifyAsync("alice", new ChatMessage { ChannelId = "general", Payload = "hi" });   // must not throw

        Assert.Single(await store.GetByUserAsync("alice"));   // failure ≠ gone → kept
    }

    [Fact]
    public async Task Image_message_uses_image_placeholder_in_payload()
    {
        var store = await StoreWith(("https://p/1", "alice"));
        var sender = new FakeSender();
        await Channel(store, sender, enabled: true)
            .NotifyAsync("alice", new ChatMessage { ChannelId = "general", ContentType = MessageType.Image, Payload = "/chat/api/blobs/x" });

        Assert.Contains("[image]", Assert.Single(sender.Sent).Payload);
    }
}
