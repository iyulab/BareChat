using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BareChat.Tests.Http;

/// <summary>
/// Deleting a channel must also delete its messages — otherwise a later channel that slugifies to the same id
/// would resurrect the old history (a leak for a private channel). The delete endpoint orchestrates both the
/// channel store and the message store.
/// </summary>
public class ChannelDeletionTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public ChannelDeletionTests(ChatApp app) => _app = app;

    private static HttpRequestMessage Req(HttpMethod method, string url, string user, object? body = null)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.Add("Cookie", $"bc_user={user}");
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    [Fact]
    public async Task Deleting_a_channel_purges_its_messages_so_a_reused_slug_starts_empty()
    {
        var client = _app.CreateClient();

        // create a channel and seed it with a message (programmatic publish → persisted history)
        var create = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "alice", new { name = "Ephemeral Room" }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var slug = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var publish = await client.SendAsync(Req(HttpMethod.Post, "/chat/messages", "alice",
            new { channelId = slug, payload = "top secret", contentType = "Text" }));
        Assert.Equal(HttpStatusCode.Created, publish.StatusCode);

        var before = await client.SendAsync(Req(HttpMethod.Get, $"/chat/api/channels/{slug}/messages", "alice"));
        Assert.Contains("top secret", await before.Content.ReadAsStringAsync());

        // delete the channel
        var delete = await client.SendAsync(Req(HttpMethod.Delete, $"/chat/api/channels/{slug}", "alice"));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // recreate a channel that slugifies to the same id — its history must be empty (no orphan resurfacing)
        var recreate = await client.SendAsync(Req(HttpMethod.Post, "/chat/api/channels", "alice", new { name = "Ephemeral Room" }));
        Assert.Equal(HttpStatusCode.Created, recreate.StatusCode);
        Assert.Equal(slug, (await recreate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());

        var after = await client.SendAsync(Req(HttpMethod.Get, $"/chat/api/channels/{slug}/messages", "alice"));
        after.EnsureSuccessStatusCode();
        var messages = await after.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Empty(messages!);
    }
}
