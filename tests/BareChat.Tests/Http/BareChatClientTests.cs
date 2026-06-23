using BareChat.Client;
using BareChat.Core.Domain;
using Microsoft.AspNetCore.Http.Connections;

namespace BareChat.Tests.Http;

/// <summary>End-to-end SDK tests against the sample host: hub send/receive + REST publish (D5).</summary>
public class BareChatClientTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public BareChatClientTests(ChatApp app) => _app = app;

    private BareChatClient NewClient(string user) => new(new BareChatClientOptions
    {
        BaseAddress = _app.Server.BaseAddress,
        AccessTokenProvider = () => user,
        // TestServer speaks LongPolling, not raw WebSockets; route through its handler.
        ConfigureConnection = o =>
        {
            o.Transports = HttpTransportType.LongPolling;
            o.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
        },
        RestMessageHandler = _app.Server.CreateHandler()
    });

    [Fact]
    public async Task Connect_subscribe_send_receives_live_message()
    {
        await using var client = NewClient("alice");
        var received = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += m => { if (m.Payload == "hello-sdk") received.TrySetResult(m); };

        await client.ConnectAsync();
        await client.SubscribeAsync("general");
        await client.SendTextAsync("general", "hello-sdk");

        var msg = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("alice", msg.SenderId);
        Assert.Equal("general", msg.ChannelId);
        Assert.Equal(MessageType.Text, msg.ContentType);
    }

    [Fact]
    public async Task Publish_via_rest_returns_persisted_system_message()
    {
        await using var client = NewClient("bob");

        var msg = await client.PublishAsync("general", "build #42 succeeded");

        Assert.NotNull(msg);
        Assert.Equal(MessageType.System, msg!.ContentType);
        Assert.Equal("build #42 succeeded", msg.Payload);
        Assert.Equal("bob", msg.SenderId);
    }
}
