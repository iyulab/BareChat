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
    public async Task Create_round_trips_private_flag()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(new Channel { ChannelId = "secret", Name = "Secret", CreatedBy = "alice", IsPrivate = true });

        var got = await store.GetChannelAsync("secret");

        Assert.NotNull(got);
        Assert.True(got!.IsPrivate);
    }

    [Fact]
    public async Task Channels_default_to_public()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("open"));

        Assert.False((await store.GetChannelAsync("open"))!.IsPrivate);
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

    // ---- read state (cross-session unread baseline) ----

    [Fact]
    public async Task GetLastReadAt_defaults_to_join_time_when_never_marked()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("general"));
        var before = DateTime.UtcNow.AddSeconds(-1);
        await store.JoinAsync("general", "alice");
        var after = DateTime.UtcNow.AddSeconds(1);

        var baselines = await store.GetLastReadAtAsync("alice");

        Assert.True(baselines.TryGetValue("general", out var baseline));
        Assert.InRange(baseline, before, after);   // == join time
    }

    [Fact]
    public async Task SetLastReadAt_overrides_baseline()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("general"));
        await store.JoinAsync("general", "alice");
        var marked = new DateTime(2030, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        await store.SetLastReadAtAsync("general", "alice", marked);

        var baselines = await store.GetLastReadAtAsync("alice");
        Assert.Equal(marked, baselines["general"]);
    }

    [Fact]
    public async Task GetLastReadAt_only_includes_subscribed_channels()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("general"));
        await store.CreateChannelAsync(NewChannel("random"));
        await store.JoinAsync("general", "alice");

        var baselines = await store.GetLastReadAtAsync("alice");

        Assert.True(baselines.ContainsKey("general"));
        Assert.False(baselines.ContainsKey("random"));
    }

    [Fact]
    public async Task SetLastReadAt_is_noop_for_nonmember()
    {
        var store = CreateStore();
        await store.CreateChannelAsync(NewChannel("general"));

        await store.SetLastReadAtAsync("general", "stranger", DateTime.UtcNow);

        var baselines = await store.GetLastReadAtAsync("stranger");
        Assert.Empty(baselines);   // no membership → no read state retained
    }
}
