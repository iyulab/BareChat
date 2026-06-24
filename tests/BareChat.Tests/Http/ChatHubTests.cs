using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace BareChat.Tests.Http;

/// <summary>
/// The hub is a server-side trust boundary: it must reject blank message/image payloads itself, not rely on
/// the embedded UI's client-side guard (other shells or the SDK could invoke it directly). Mirrors the REST
/// edit policy (<c>PUT /messages</c> rejects blank payloads).
/// </summary>
public class ChatHubTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public ChatHubTests(ChatApp app) => _app = app;

    private HubConnection Connect(string user)
    {
        var url = new Uri(_app.Server.BaseAddress, $"chat/hub?access_token={user}").ToString();
        return new HubConnectionBuilder()
            .WithUrl(url, o =>
            {
                o.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task SendMessage_rejects_blank_payload(string payload)
    {
        var conn = Connect("alice");
        await conn.StartAsync();
        try
        {
            var ex = await Assert.ThrowsAsync<HubException>(() => conn.InvokeAsync("SendMessage", "general", payload));
            Assert.Contains("payload is required", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { await conn.DisposeAsync(); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendImage_rejects_blank_url(string url)
    {
        var conn = Connect("alice");
        await conn.StartAsync();
        try
        {
            var ex = await Assert.ThrowsAsync<HubException>(() => conn.InvokeAsync("SendImage", "general", url));
            Assert.Contains("url is required", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { await conn.DisposeAsync(); }
    }

    [Fact]
    public async Task SendMessage_accepts_non_blank_payload()
    {
        var conn = Connect("alice");
        await conn.StartAsync();
        try
        {
            // No throw == accepted; the REST history confirms it actually persisted.
            await conn.InvokeAsync("SendMessage", "general", "hello from the hub");
            var http = _app.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Get, "/chat/api/channels/general/messages?limit=100");
            req.Headers.Add("Cookie", "bc_user=alice");
            var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            Assert.Contains("hello from the hub", await resp.Content.ReadAsStringAsync());
        }
        finally { await conn.DisposeAsync(); }
    }
}
