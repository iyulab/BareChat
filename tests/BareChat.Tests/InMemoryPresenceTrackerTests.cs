using BareChat.Presence;

namespace BareChat.Tests;

public class InMemoryPresenceTrackerTests
{
    [Fact]
    public async Task User_is_online_after_first_connection_and_offline_after_last()
    {
        var p = new InMemoryPresenceTracker();
        Assert.False(await p.IsOnlineAsync("alice"));

        await p.SetOnlineAsync("alice", "c1");
        await p.SetOnlineAsync("alice", "c2");
        Assert.True(await p.IsOnlineAsync("alice"));
        Assert.Equal(2, (await p.GetConnectionsAsync("alice")).Count);

        await p.SetOfflineAsync("alice", "c1");
        Assert.True(await p.IsOnlineAsync("alice")); // c2 still live

        await p.SetOfflineAsync("alice", "c2");
        Assert.False(await p.IsOnlineAsync("alice"));
        Assert.Empty(await p.GetConnectionsAsync("alice"));
    }
}
