using Microsoft.AspNetCore.Http.Connections.Client;

namespace BareChat.Client;

/// <summary>Configuration for <see cref="BareChatClient"/>.</summary>
public sealed class BareChatClientOptions
{
    /// <summary>Host root URL the chat is mounted on, e.g. <c>https://host/</c>.</summary>
    public required Uri BaseAddress { get; set; }

    /// <summary>Mount prefix (matches the host's <c>RoutePrefix</c>). Default <c>/chat</c>.</summary>
    public string RoutePrefix { get; set; } = "/chat";

    /// <summary>Supplies a bearer token for the hub (<c>?access_token=</c>) and REST publish. Optional for same-origin/cookie hosts.</summary>
    public Func<string?>? AccessTokenProvider { get; set; }

    /// <summary>Reconnect automatically when the hub connection drops. Default true.</summary>
    public bool AutomaticReconnect { get; set; } = true;

    /// <summary>Escape hatch to tweak the underlying SignalR HTTP connection (transports, handler …). Advanced/test use.</summary>
    public Action<HttpConnectionOptions>? ConfigureConnection { get; set; }

    /// <summary>Optional message handler for the REST publish client (e.g. a test-server handler).</summary>
    public HttpMessageHandler? RestMessageHandler { get; set; }
}
