using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Tests.Conformance;

/// <summary>Behavioral contract every <see cref="IPushSubscriptionStore"/> must satisfy (InMemory + SQLite run it).</summary>
public abstract class PushSubscriptionStoreConformanceTests
{
    protected abstract IPushSubscriptionStore CreateStore();

    private static PushSubscription Sub(string endpoint, string user) => new()
    {
        Endpoint = endpoint,
        P256dh = "p-" + endpoint,
        Auth = "a-" + endpoint,
        UserId = user
    };

    [Fact]
    public async Task Save_then_get_by_user_returns_subscription()
    {
        var store = CreateStore();
        await store.SaveAsync(Sub("https://push/1", "alice"));

        var subs = await store.GetByUserAsync("alice");

        var s = Assert.Single(subs);
        Assert.Equal("https://push/1", s.Endpoint);
        Assert.Equal("p-https://push/1", s.P256dh);
        Assert.Equal("a-https://push/1", s.Auth);
    }

    [Fact]
    public async Task Get_by_user_filters_by_owner()
    {
        var store = CreateStore();
        await store.SaveAsync(Sub("https://push/a", "alice"));
        await store.SaveAsync(Sub("https://push/b", "bob"));

        Assert.Single(await store.GetByUserAsync("alice"));
        Assert.Empty(await store.GetByUserAsync("carol"));
    }

    [Fact]
    public async Task A_user_can_have_multiple_subscriptions()
    {
        var store = CreateStore();
        await store.SaveAsync(Sub("https://push/desktop", "alice"));
        await store.SaveAsync(Sub("https://push/phone", "alice"));

        var subs = await store.GetByUserAsync("alice");
        Assert.Equal(2, subs.Count);
    }

    [Fact]
    public async Task Save_upserts_by_endpoint()
    {
        var store = CreateStore();
        await store.SaveAsync(Sub("https://push/1", "alice"));
        // Same endpoint re-subscribed with new keys / different owner → updated, not duplicated.
        await store.SaveAsync(new PushSubscription { Endpoint = "https://push/1", P256dh = "new-p", Auth = "new-a", UserId = "bob" });

        Assert.Empty(await store.GetByUserAsync("alice"));
        var s = Assert.Single(await store.GetByUserAsync("bob"));
        Assert.Equal("new-p", s.P256dh);
    }

    [Fact]
    public async Task Remove_deletes_subscription()
    {
        var store = CreateStore();
        await store.SaveAsync(Sub("https://push/1", "alice"));

        await store.RemoveAsync("https://push/1");

        Assert.Empty(await store.GetByUserAsync("alice"));
    }

    [Fact]
    public async Task Remove_unknown_endpoint_is_noop()
    {
        var store = CreateStore();
        await store.RemoveAsync("https://push/ghost");   // must not throw
    }
}
