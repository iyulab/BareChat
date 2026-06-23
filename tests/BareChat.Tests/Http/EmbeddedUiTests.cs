using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BareChat.Tests.Http;

public class EmbeddedUiTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public EmbeddedUiTests(ChatApp app) => _app = app;

    [Fact]
    public async Task Shell_serves_html_with_injected_base()
    {
        var client = _app.CreateClient();
        var html = await client.GetStringAsync("/chat");

        Assert.Contains("window.BARECHAT_BASE = \"/chat\"", html);
        Assert.Contains("/chat/app.js", html);
        Assert.Contains("/chat/signalr.min.js", html);
    }

    [Fact]
    public async Task App_assets_are_served()
    {
        var client = _app.CreateClient();

        var js = await client.GetAsync("/chat/app.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Contains("javascript", js.Content.Headers.ContentType!.ToString());

        var css = await client.GetAsync("/chat/app.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);

        var signalr = await client.GetStringAsync("/chat/signalr.min.js");
        Assert.Contains("HubConnectionBuilder", signalr);
    }

    [Fact]
    public async Task Whoami_returns_authenticated_identity()
    {
        var client = _app.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/chat/api/whoami");
        req.Headers.Add("Cookie", "bc_user=alice");
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var me = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("alice", me.GetProperty("userId").GetString());
    }
}
