using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BareChat.Tests.Http;

public class ChatApiTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public ChatApiTests(ChatApp app) => _app = app;

    private static HttpRequestMessage Req(HttpMethod method, string url, string? user, object? body = null)
    {
        var r = new HttpRequestMessage(method, url);
        if (user is not null) r.Headers.Add("Cookie", $"bc_user={user}");
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    [Fact]
    public async Task Anonymous_request_is_unauthorized()
    {
        var client = _app.CreateClient();
        var resp = await client.SendAsync(Req(HttpMethod.Get, "/chat/api/channels", user: null));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Default_channel_is_listed_and_marked_default()
    {
        var client = _app.CreateClient();
        var resp = await client.SendAsync(Req(HttpMethod.Get, "/chat/api/channels", "alice"));
        resp.EnsureSuccessStatusCode();

        var channels = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        var general = channels!.Single(c => c.GetProperty("id").GetString() == "general");
        Assert.True(general.GetProperty("isDefault").GetBoolean());
    }

    [Fact]
    public async Task Create_channel_slugifies_and_creator_is_member()
    {
        var client = _app.CreateClient();
        var resp = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "alice", new { name = "Project Apollo" }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("project-apollo", dto.GetProperty("id").GetString());
        Assert.True(dto.GetProperty("isMember").GetBoolean());
        Assert.Equal("alice", dto.GetProperty("createdBy").GetString());
    }

    [Fact]
    public async Task Duplicate_channel_returns_conflict()
    {
        var client = _app.CreateClient();
        await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "alice", new { name = "dupe-room" }));
        var second = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "bob", new { name = "dupe-room" }));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Default_channel_cannot_be_deleted()
    {
        var client = _app.CreateClient();
        var resp = await client.SendAsync(Req(HttpMethod.Delete, "/chat/api/channels/general", "alice"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Only_creator_can_delete_channel()
    {
        var client = _app.CreateClient();
        await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "alice", new { name = "alice-room" }));

        var byBob = await client.SendAsync(Req(HttpMethod.Delete, "/chat/api/channels/alice-room", "bob"));
        Assert.Equal(HttpStatusCode.Forbidden, byBob.StatusCode);

        var byAlice = await client.SendAsync(Req(HttpMethod.Delete, "/chat/api/channels/alice-room", "alice"));
        Assert.Equal(HttpStatusCode.NoContent, byAlice.StatusCode);
    }

    [Fact]
    public async Task Publish_then_history_returns_the_message()
    {
        var client = _app.CreateClient();
        var publish = await client.SendAsync(Req(HttpMethod.Post, "/chat/messages", "alice",
            new { channelId = "general", payload = "equipment #3 over threshold", contentType = "System" }));
        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);

        var history = await client.SendAsync(Req(HttpMethod.Get, "/chat/api/channels/general/messages", "alice"));
        history.EnsureSuccessStatusCode();
        var messages = await history.Content.ReadFromJsonAsync<List<JsonElement>>();

        Assert.Contains(messages!, m => m.GetProperty("payload").GetString() == "equipment #3 over threshold"
                                        && m.GetProperty("contentType").GetString() == "System");
    }

    [Fact]
    public async Task Join_and_leave_channel()
    {
        var client = _app.CreateClient();
        await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "alice", new { name = "join-test" }));

        var join = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels/join-test/join", "bob"));
        Assert.Equal(HttpStatusCode.NoContent, join.StatusCode);

        var mine = await client.SendAsync(Req(HttpMethod.Get, "/chat/api/channels/mine", "bob"));
        var channels = await mine.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Contains(channels!, c => c.GetProperty("id").GetString() == "join-test");

        var leave = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels/join-test/leave", "bob"));
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
    }
}
