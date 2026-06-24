using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BareChat.Tests.Http;

/// <summary>Private channels (D9 extension): discovery hiding, membership-gated r/w, creator-only invite.</summary>
public class PrivateChannelTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public PrivateChannelTests(ChatApp app) => _app = app;

    private static HttpRequestMessage Req(HttpMethod method, string url, string user, object? body = null)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.Add("Cookie", $"bc_user={user}");
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    private static async Task<string> CreatePrivate(HttpClient client, string user, string name)
    {
        var resp = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", user, new { name, isPrivate = true }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dto.GetProperty("isPrivate").GetBoolean());
        return dto.GetProperty("id").GetString()!;
    }

    private async Task<List<string>> VisibleChannelIds(HttpClient client, string user)
    {
        var resp = await client.SendAsync(Req(HttpMethod.Get, "/chat/api/channels", user));
        resp.EnsureSuccessStatusCode();
        var channels = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        return channels!.Select(c => c.GetProperty("id").GetString()!).ToList();
    }

    [Fact]
    public async Task Private_channel_is_hidden_from_non_members_but_visible_to_creator()
    {
        var client = _app.CreateClient();
        var id = await CreatePrivate(client, "alice", "Alice Secret");

        Assert.Contains(id, await VisibleChannelIds(client, "alice"));   // creator (member)
        Assert.DoesNotContain(id, await VisibleChannelIds(client, "bob")); // non-member
    }

    [Fact]
    public async Task Non_member_cannot_read_or_write_private_channel()
    {
        var client = _app.CreateClient();
        var id = await CreatePrivate(client, "alice", "No Peeking");

        // Existence-hiding: a non-member can't distinguish a private channel they can't see from a
        // non-existent one — both return 404, never 403 (a 403 would leak that the channel exists).
        var read = await client.SendAsync(Req(HttpMethod.Get, $"/chat/api/channels/{id}/messages", "bob"));
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        var write = await client.SendAsync(Req(HttpMethod.Post, "/chat/messages", "bob",
            new { channelId = id, payload = "intrusion", contentType = "Text" }));
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    [Fact]
    public async Task Private_channel_existence_is_hidden_across_all_operations()
    {
        var client = _app.CreateClient();
        var id = await CreatePrivate(client, "alice", "Ghost Room");

        // Every per-channel operation a non-member attempts must look identical to a non-existent channel.
        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Post, $"/chat/api/channels/{id}/join", null),
            (HttpMethod.Post, $"/chat/api/channels/{id}/leave", null),
            (HttpMethod.Post, $"/chat/api/channels/{id}/read", null),
            (HttpMethod.Delete, $"/chat/api/channels/{id}", null),
            (HttpMethod.Post, $"/chat/api/channels/{id}/members", (object?)new { userId = "carol" }),
        })
        {
            var resp = await client.SendAsync(Req(method, url, "bob", body));
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }

    [Fact]
    public async Task Creator_can_invite_a_member_who_then_gains_access()
    {
        var client = _app.CreateClient();
        var id = await CreatePrivate(client, "alice", "Invite Test");

        var invite = await client.SendAsync(Req(HttpMethod.Post, $"/chat/api/channels/{id}/members", "alice", new { userId = "bob" }));
        Assert.Equal(HttpStatusCode.NoContent, invite.StatusCode);

        // bob now sees it and can read it
        Assert.Contains(id, await VisibleChannelIds(client, "bob"));
        var read = await client.SendAsync(Req(HttpMethod.Get, $"/chat/api/channels/{id}/messages", "bob"));
        read.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Non_creator_member_cannot_invite()
    {
        var client = _app.CreateClient();
        var id = await CreatePrivate(client, "alice", "Closed Club");
        // bob is a member (invited), but membership ≠ management: only the creator can invite. A member
        // can see the channel, so this is an honest 403 (not the existence-hiding 404 a non-member gets).
        var add = await client.SendAsync(Req(HttpMethod.Post, $"/chat/api/channels/{id}/members", "alice", new { userId = "bob" }));
        Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);

        var invite = await client.SendAsync(Req(HttpMethod.Post, $"/chat/api/channels/{id}/members", "bob", new { userId = "carol" }));
        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
    }

    [Fact]
    public async Task Public_channel_remains_self_joinable()
    {
        var client = _app.CreateClient();
        await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "alice", new { name = "Open Room" }));

        var join = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels/open-room/join", "bob"));
        Assert.Equal(HttpStatusCode.NoContent, join.StatusCode);
    }
}
