using BareChat;
using BareChat.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BareChat.Tests.Http;

/// <summary>Verifies the dependency-free <c>?access_token=</c> → Bearer lift for the hub path (D3).</summary>
public class AccessTokenMiddlewareTests
{
    private static async Task<IHost> EchoHostAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(s => s.AddOptions<BareChatOptions>())   // RoutePrefix default "/chat"
                .Configure(app =>
                {
                    app.UseBareChatAccessToken();
                    app.Run(ctx => ctx.Response.WriteAsync(ctx.Request.Headers.Authorization.ToString()));
                }))
            .StartAsync();
        return host;
    }

    [Fact]
    public async Task Lifts_access_token_to_bearer_on_hub_path()
    {
        using var host = await EchoHostAsync();
        var auth = await host.GetTestClient().GetStringAsync("/chat/hub?access_token=abc123");
        Assert.Equal("Bearer abc123", auth);
    }

    [Fact]
    public async Task Does_not_lift_off_the_hub_path()
    {
        using var host = await EchoHostAsync();
        var auth = await host.GetTestClient().GetStringAsync("/chat/api/whoami?access_token=abc123");
        Assert.Equal("", auth);   // REST should use a real Authorization header, not the query
    }

    [Fact]
    public async Task Does_not_overwrite_an_existing_authorization_header()
    {
        using var host = await EchoHostAsync();
        var client = host.GetTestClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/chat/hub?access_token=fromquery");
        req.Headers.Add("Authorization", "Bearer fromheader");
        var resp = await client.SendAsync(req);
        Assert.Equal("Bearer fromheader", await resp.Content.ReadAsStringAsync());
    }
}

/// <summary>End-to-end: a cross-origin client authenticates the SignalR hub via <c>?access_token=</c> (D3).</summary>
public class AccessTokenHubTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public AccessTokenHubTests(ChatApp app) => _app = app;

    private HubConnection BuildConnection(string? accessToken)
    {
        var url = new Uri(_app.Server.BaseAddress, "chat/hub").ToString();
        if (accessToken is not null) url += $"?access_token={accessToken}";
        return new HubConnectionBuilder()
            .WithUrl(url, o =>
            {
                o.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();
    }

    [Fact]
    public async Task Hub_authenticates_via_access_token_query()
    {
        var conn = BuildConnection("alice");
        await conn.StartAsync();
        try
        {
            // JoinChannel requires an authenticated user; succeeds only if the token authenticated the connection.
            await conn.InvokeAsync("JoinChannel", "general");
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }

    [Fact]
    public async Task Hub_rejects_unauthenticated_invocation_without_token()
    {
        var conn = BuildConnection(accessToken: null);
        await conn.StartAsync();
        try
        {
            var ex = await Assert.ThrowsAsync<HubException>(() => conn.InvokeAsync("JoinChannel", "general"));
            Assert.Contains("authenticat", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await conn.DisposeAsync();
        }
    }
}
