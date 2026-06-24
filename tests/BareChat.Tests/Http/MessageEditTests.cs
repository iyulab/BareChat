using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BareChat.Tests.Http;

/// <summary>Author-only message edit / soft-delete (D6) over REST.</summary>
public class MessageEditTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public MessageEditTests(ChatApp app) => _app = app;

    private static HttpRequestMessage Req(HttpMethod method, string url, string user, object? body = null)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.Add("Cookie", $"bc_user={user}");
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    private async Task<string> Publish(HttpClient client, string user, string payload, string contentType = "Text")
    {
        var resp = await client.SendAsync(Req(HttpMethod.Post, "/chat/messages", user,
            new { channelId = "general", payload, contentType }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return dto.GetProperty("messageId").GetString()!;
    }

    [Fact]
    public async Task Author_can_edit_text_message()
    {
        var client = _app.CreateClient();
        var id = await Publish(client, "alice", "helo");

        var resp = await client.SendAsync(Req(HttpMethod.Put, $"/chat/api/messages/{id}", "alice", new { payload = "hello" }));
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("hello", dto.GetProperty("payload").GetString());
        Assert.NotEqual(JsonValueKind.Null, dto.GetProperty("editedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Non_author_cannot_edit()
    {
        var client = _app.CreateClient();
        var id = await Publish(client, "alice", "mine");

        var resp = await client.SendAsync(Req(HttpMethod.Put, $"/chat/api/messages/{id}", "bob", new { payload = "hijack" }));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task System_message_cannot_be_edited()
    {
        var client = _app.CreateClient();
        var id = await Publish(client, "alice", "alarm", contentType: "System");

        var resp = await client.SendAsync(Req(HttpMethod.Put, $"/chat/api/messages/{id}", "alice", new { payload = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Author_can_delete_and_payload_is_cleared()
    {
        var client = _app.CreateClient();
        var id = await Publish(client, "alice", "delete me");

        var del = await client.SendAsync(Req(HttpMethod.Delete, $"/chat/api/messages/{id}", "alice"));
        del.EnsureSuccessStatusCode();
        var dto = await del.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(dto.GetProperty("isDeleted").GetBoolean());
        Assert.Equal("", dto.GetProperty("payload").GetString());

        // history reflects the tombstone (content no longer retrievable)
        var history = await client.SendAsync(Req(HttpMethod.Get, "/chat/api/channels/general/messages", "alice"));
        var messages = await history.Content.ReadFromJsonAsync<List<JsonElement>>();
        var tomb = messages!.Single(m => m.GetProperty("messageId").GetString() == id);
        Assert.True(tomb.GetProperty("isDeleted").GetBoolean());
        Assert.Equal("", tomb.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task Editing_deleted_message_is_rejected()
    {
        var client = _app.CreateClient();
        var id = await Publish(client, "alice", "to delete");
        await client.SendAsync(Req(HttpMethod.Delete, $"/chat/api/messages/{id}", "alice"));

        var resp = await client.SendAsync(Req(HttpMethod.Put, $"/chat/api/messages/{id}", "alice", new { payload = "back" }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Non_author_cannot_delete()
    {
        var client = _app.CreateClient();
        var id = await Publish(client, "alice", "protected");

        var resp = await client.SendAsync(Req(HttpMethod.Delete, $"/chat/api/messages/{id}", "bob"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_unknown_message_returns_not_found()
    {
        var client = _app.CreateClient();
        var resp = await client.SendAsync(Req(HttpMethod.Put,
            $"/chat/api/messages/{Guid.NewGuid()}", "alice", new { payload = "x" }));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
