using BareChat.Authorization;
using BareChat.Core.Domain;
using BareChat.Storage;

namespace BareChat.Tests;

/// <summary>Visibility-aware authorization: public open to all, private gated by membership.</summary>
public class ChannelMembershipAuthorizationProviderTests
{
    private static ChatUserContext User(string id) =>
        new() { UserId = id, DisplayName = id, IsAuthenticated = true };

    private static async Task<(ChannelMembershipAuthorizationProvider authz, InMemoryChannelStore store)> SetupAsync()
    {
        var store = new InMemoryChannelStore();
        await store.CreateChannelAsync(new Channel { ChannelId = "public", Name = "Public", CreatedBy = "alice" });
        await store.CreateChannelAsync(new Channel { ChannelId = "secret", Name = "Secret", CreatedBy = "alice", IsPrivate = true });
        await store.JoinAsync("secret", "alice");
        return (new ChannelMembershipAuthorizationProvider(store), store);
    }

    [Fact]
    public async Task Unauthenticated_is_denied()
    {
        var (authz, _) = await SetupAsync();
        Assert.False(await authz.CanReadAsync(ChatUserContext.Anonymous, "public"));
    }

    [Fact]
    public async Task Public_channel_allows_any_authenticated_user()
    {
        var (authz, _) = await SetupAsync();
        Assert.True(await authz.CanReadAsync(User("bob"), "public"));
        Assert.True(await authz.CanWriteAsync(User("bob"), "public"));
    }

    [Fact]
    public async Task Private_channel_allows_member()
    {
        var (authz, _) = await SetupAsync();
        Assert.True(await authz.CanReadAsync(User("alice"), "secret"));
        Assert.True(await authz.CanWriteAsync(User("alice"), "secret"));
    }

    [Fact]
    public async Task Private_channel_denies_non_member()
    {
        var (authz, _) = await SetupAsync();
        Assert.False(await authz.CanReadAsync(User("bob"), "secret"));
        Assert.False(await authz.CanWriteAsync(User("bob"), "secret"));
    }

    [Fact]
    public async Task Unknown_channel_is_denied()
    {
        var (authz, _) = await SetupAsync();
        Assert.False(await authz.CanReadAsync(User("alice"), "ghost"));
    }
}
