using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Tests.Conformance;

/// <summary>Behavioral contract every <see cref="IChannelStore"/> must satisfy (InMemory + SQLite run it).</summary>
public abstract class ChannelStoreConformanceTests
{
    protected abstract IChannelStore CreateStore();

    private static Channel NewChannel(string id, bool isDefault = false) =>
        new() { ChannelId = id, Name = id, CreatedBy = "system", IsDefault = isDefault };

    [Fact]
    public async Task Create_then_get_returns_channel()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("general", isDefault: true));

        var got = await store.GetChannelAsync("general");

        Assert.NotNull(got);
        Assert.True(got!.IsDefault);
        Assert.Equal("general", got.Name);
    }

    [Fact]
    public async Task Get_unknown_returns_null()
    {
        var store = CreateStore();
        Assert.Null(await store.GetChannelAsync("ghost"));
    }

    [Fact]
    public async Task Create_duplicate_slug_throws()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("dup"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateChannelAsync(NewChannel("dup")));
    }

    [Fact]
    public async Task Join_makes_user_member_and_lists_in_user_channels()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("general"));

        await store.JoinAsync("general", "alice");

        Assert.True(await store.IsMemberAsync("general", "alice"));
        var mine = await store.GetUserChannelsAsync("alice");
        Assert.Single(mine);
        Assert.Equal("general", mine[0].ChannelId);
    }

    [Fact]
    public async Task Join_is_idempotent()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("general"));

        await store.JoinAsync("general", "alice");
        await store.JoinAsync("general", "alice");

        Assert.Single(await store.GetMembersAsync("general"));
    }

    [Fact]
    public async Task Join_nonexistent_channel_throws()
    {
        var store = CreateStore();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.JoinAsync("ghost", "alice"));
    }

    [Fact]
    public async Task Leave_removes_membership()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("general"));
        await store.JoinAsync("general", "alice");

        await store.LeaveAsync("general", "alice");

        Assert.False(await store.IsMemberAsync("general", "alice"));
        Assert.Empty(await store.GetUserChannelsAsync("alice"));
    }

    [Fact]
    public async Task Delete_removes_channel_and_memberships()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("temp"));
        await store.JoinAsync("temp", "alice");

        await store.DeleteChannelAsync("temp");

        Assert.Null(await store.GetChannelAsync("temp"));
        Assert.Empty(await store.GetUserChannelsAsync("alice"));
    }

    [Fact]
    public async Task GetMembers_lists_all_subscribers()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("general"));
        await store.JoinAsync("general", "alice");
        await store.JoinAsync("general", "bob");

        var members = await store.GetMembersAsync("general");

        Assert.Equal(2, members.Count);
        Assert.Contains("alice", members);
        Assert.Contains("bob", members);
    }
}
