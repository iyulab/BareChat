using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace BareChat.Tests.Http;

public class PushApiTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public PushApiTests(ChatApp app) => _app = app;

    private static HttpRequestMessage Authed(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Cookie", "bc_user=alice");
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task Vapid_public_key_is_404_when_push_not_configured()
    {
        var client = _app.CreateClient();
        var resp = await client.GetAsync("/chat/api/push/vapid-public-key");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Vapid_public_key_is_returned_when_configured()
    {
        var configured = _app.WithWebHostBuilder(b =>
        {
            b.UseSetting("BareChat:Push:PublicKey", "test-public-key");
            b.UseSetting("BareChat:Push:PrivateKey", "test-private-key");
        });
        var client = configured.CreateClient();

        var resp = await client.GetAsync("/chat/api/push/vapid-public-key");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("test-public-key", json.GetProperty("publicKey").GetString());
    }

    [Fact]
    public async Task Subscribe_requires_authentication()
    {
        var client = _app.CreateClient();
        var resp = await client.PostAsJsonAsync("/chat/api/push/subscriptions",
            new { endpoint = "https://push/x", keys = new { p256dh = "p", auth = "a" } });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Subscribe_then_unsubscribe_round_trip()
    {
        var client = _app.CreateClient();

        var sub = await client.SendAsync(Authed(HttpMethod.Post, "/chat/api/push/subscriptions",
            new { endpoint = "https://push/roundtrip", keys = new { p256dh = "pk", auth = "ak" } }));
        Assert.Equal(HttpStatusCode.NoContent, sub.StatusCode);

        var unsub = await client.SendAsync(Authed(HttpMethod.Delete, "/chat/api/push/subscriptions",
            new { endpoint = "https://push/roundtrip" }));
        Assert.Equal(HttpStatusCode.NoContent, unsub.StatusCode);
    }

    [Fact]
    public async Task Subscribe_with_missing_keys_is_bad_request()
    {
        var client = _app.CreateClient();
        var resp = await client.SendAsync(Authed(HttpMethod.Post, "/chat/api/push/subscriptions",
            new { endpoint = "https://push/x" }));   // no keys
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
