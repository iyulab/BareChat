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

        var read = await client.SendAsync(Req(HttpMethod.Get, $"/chat/api/channels/{id}/messages", "bob"));
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        var write = await client.SendAsync(Req(HttpMethod.Post, "/chat/messages", "bob",
            new { channelId = id, payload = "intrusion", contentType = "Text" }));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Private_channel_cannot_be_self_joined()
    {
        var client = _app.CreateClient();
        var id = await CreatePrivate(client, "alice", "Members Only");

        var join = await client.SendAsync(Req(HttpMethod.Post, $"/chat/api/channels/{id}/join", "bob"));
        Assert.Equal(HttpStatusCode.Forbidden, join.StatusCode);
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
    public async Task Non_creator_cannot_invite()
    {
        var client = _app.CreateClient();
        var id = await CreatePrivate(client, "alice", "Closed Club");

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
