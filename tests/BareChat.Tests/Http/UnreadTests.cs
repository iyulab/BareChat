using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BareChat.Tests.Http;

/// <summary>End-to-end coverage for cross-session unread (server-side <c>lastReadAt</c>).</summary>
public class UnreadTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public UnreadTests(ChatApp app) => _app = app;

    private static HttpRequestMessage Req(HttpMethod method, string url, string user, object? body = null)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.Add("Cookie", $"bc_user={user}");
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    private async Task<int> UnreadOf(HttpClient client, string user, string channelId)
    {
        var resp = await client.SendAsync(Req(HttpMethod.Get, "/chat/api/channels", user));
        resp.EnsureSuccessStatusCode();
        var channels = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        var ch = channels!.Single(c => c.GetProperty("id").GetString() == channelId);
        return ch.GetProperty("unreadCount").GetInt32();
    }

    private static async Task Publish(HttpClient client, string user, string channelId, string payload)
    {
        var resp = await client.SendAsync(Req(HttpMethod.Post, "/chat/messages", user,
            new { channelId, payload, contentType = "System" }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Unread_counts_others_messages_then_resets_on_read()
    {
        var client = _app.CreateClient();
        // alice creates the room (auto-joins → baseline = join time)
        await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "alice", new { name = "unread-room" }));
        await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels/unread-room/join", "bob"));

        // bob posts two messages after alice's baseline
        await Publish(client, "bob", "unread-room", "ping 1");
        await Publish(client, "bob", "unread-room", "ping 2");

        Assert.Equal(2, await UnreadOf(client, "alice", "unread-room"));

        // alice marks the channel read → unread clears
        var read = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels/unread-room/read", "alice"));
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal(0, await UnreadOf(client, "alice", "unread-room"));

        // a later message bumps it again
        await Publish(client, "bob", "unread-room", "ping 3");
        Assert.Equal(1, await UnreadOf(client, "alice", "unread-room"));
    }

    [Fact]
    public async Task Own_messages_do_not_count_as_unread()
    {
        var client = _app.CreateClient();
        await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "carol", new { name = "self-room" }));

        await Publish(client, "carol", "self-room", "note to self");

        Assert.Equal(0, await UnreadOf(client, "carol", "self-room"));
    }

    [Fact]
    public async Task Non_member_channel_reports_zero_unread()
    {
        var client = _app.CreateClient();
        // dave creates a room and posts; erin is not a member.
        await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "dave", new { name = "dave-room" }));
        await Publish(client, "dave", "dave-room", "hello");

        Assert.Equal(0, await UnreadOf(client, "erin", "dave-room"));
    }

    [Fact]
    public async Task Read_on_unknown_channel_returns_not_found()
    {
        var client = _app.CreateClient();
        var resp = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels/ghost/read", "alice"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
